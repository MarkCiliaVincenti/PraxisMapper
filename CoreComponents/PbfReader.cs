﻿using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using OsmSharp.Complete;
using ProtoBuf;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static PraxisCore.DbTables;

namespace PraxisCore.PbfReader
{
    /// <summary>
    /// PraxisMapper's customized, multithreaded PBF parser. Saves on RAM usage by relying on disk access when needed. Can resume a previous session if stopped for some reason.
    /// </summary>
    public class PbfReader
    {
        //The 5th generation of logic for pulling geometry out of a pbf file. This one is written specfically for PraxisMapper, and
        //doesn't depend on OsmSharp for reading the raw data now. OsmSharp's still used for object types now that there's our own
        //FeatureInterpreter instead of theirs. 

        static int initialCapacity = 8009; //ConcurrentDictionary says initial capacity shouldn't be divisible by a small prime number, so i picked the prime closes to 8,000 for initial capacity
        static int initialConcurrency = Environment.ProcessorCount;

        public bool saveToTsv = true;//Defaults to the common intermediate output.
        public bool saveToDB = false;
        public bool onlyMatchedAreas = false; //if true, only process geometry if the tags come back with IsGamplayElement== true;
        public string processingMode = "normal"; //normal: use geometry as it exists. Center: save the center point of any geometry provided instead of its actual value.
        public string styleSet = "mapTiles"; //which style set to use when parsing entries
        public bool keepIndexFiles = false;

        public string outputPath = "";
        public string filenameHeader = "";

        public bool lowResourceMode = false;
        public bool reprocessFile = false; //if true, we load TSV data from a previous run and re-process that by the rules.
        public bool keepAllBlocksInRam = false; //if true, keep all decompressed blocks in memory instead of purging out unused ones each block.

        FileInfo fi;
        FileStream fs; // The input file. Output files are either WriteAllText or their own streamwriter.

        //<osmId, blockId>
        ConcurrentDictionary<long, long> relationFinder = new ConcurrentDictionary<long, long>(initialConcurrency, initialCapacity);

        //blockId, <minNode, maxNode>.
        ConcurrentDictionary<long, Tuple<long, long>> nodeFinder2 = new ConcurrentDictionary<long, Tuple<long, long>>(initialConcurrency, initialCapacity);

        //blockId, minNode, maxNode.
        List<Tuple<long, long, long>> nodeFinderList = new List<Tuple<long, long, long>>(initialCapacity);

        //<blockId, maxWayId> since ways are sorted in order.
        ConcurrentDictionary<long, long> wayFinder = new ConcurrentDictionary<long, long>(initialConcurrency, initialCapacity);// Concurrent needed because loading is threaded.

        List<Tuple<long, long>> wayFinderList = new List<Tuple<long, long>>(initialCapacity);
        int nodeFinderTotal = 0;
        int wayFinderTotal = 0;

        Dictionary<long, long> blockPositions = new Dictionary<long, long>(initialCapacity);
        Dictionary<long, int> blockSizes = new Dictionary<long, int>(initialCapacity);

        Envelope bounds = null; //If not null, reject elements not within it
        IPreparedGeometry boundsEntry = null; //use for precise detection of what to include.

        ConcurrentDictionary<long, PrimitiveBlock> activeBlocks = new ConcurrentDictionary<long, PrimitiveBlock>(initialConcurrency, initialCapacity);
        ConcurrentDictionary<long, bool> accessedBlocks = new ConcurrentDictionary<long, bool>(initialConcurrency, initialCapacity);

        object msLock = new object(); //reading blocks from disk.

        long nextBlockId = 0;
        long firstWayBlock = 0;
        long firstRelationBlock = 0;
        int startNodeBtreeIndex = 0;
        int startWayBtreeIndex = 0;
        int wayHintsMax = 12; //Ignore hints if it would be slower checking all of them than just doing a BTree search on 2^12 (4096) blocks
        int nodeHintsMax = 12;

        ConcurrentBag<Task> relList = new ConcurrentBag<Task>(); //Individual, smaller tasks.
        ConcurrentBag<TimeSpan> timeList = new ConcurrentBag<TimeSpan>(); //how long each block took to process.

        //for reference. These are likely to be lost if the application dies partway through processing, since these sit outside the general block-by-block plan.
        //private HashSet<long> knownSlowRelations = new HashSet<long>() {
        //    9488835, //Labrador Sea. 25,000 ways. Stack Overflows on converting to CompleteRelation through defaultFeatureInterpreter.
        //    1205151, //Lake Huron, 14,000 ways. Can Stack overflow joining rings.
        //    148838, //United States. 1029 members but a very large geographic area
        //    9428957, //Gulf of St. Lawrence. 11,000 ways. Can finish processing, so it's somewhere between 11k and 14k that the stack overflow hits.
        //    4039900, //Lake Erie is 1100 ways, originally took ~56 seconds start to finish, now runs in 3-6 seconds on its own.
        //};

        public bool displayStatus = true;

        CancellationTokenSource tokensource = new CancellationTokenSource();
        CancellationToken token;

        public PbfReader()
        {
            token = tokensource.Token;
            Serializer.PrepareSerializer<PrimitiveBlock>();
            Serializer.PrepareSerializer<Blob>();
        }

        /// <summary>
        /// Returns how many blocks are in the current PBF file.
        /// </summary>
        /// <returns>long of blocks in the opened file</returns>
        public long BlockCount()
        {
            return blockPositions.Count;
        }

        /// <summary>
        /// Opens up a file for reading. 
        /// </summary>
        /// <param name="filename">the path to the file to read.</param>
        private void Open(string filename)
        {
            fi = new FileInfo(filename);
            fs = File.OpenRead(filename);
        }

        /// <summary>
        /// Closes the currently open file
        /// </summary>
        private void Close()
        {
            fs.Close();
            fs.Dispose();
            tokensource.Cancel();
        }

        //Currently only converts items to center points.
        private void ReprocessFileToCenters(string filename)
        {
            //load up each line of a file from a previous run, and then re-process it according to the current settings.
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            Log.WriteLog("Loading " + filename + " for processing at " + DateTime.Now);
            var fr = File.OpenRead(filename);
            var sr = new StreamReader(fr);
            sw.Start();
            var reprocFileStream = new StreamWriter(new FileStream(outputPath + filenameHeader + Path.GetFileNameWithoutExtension(filename) + "-reprocessed.geomData", FileMode.OpenOrCreate));

            while (!sr.EndOfStream)
            {
                StringBuilder sb = new StringBuilder();
                string entry = sr.ReadLine();
                DbTables.Place md = GeometrySupport.ConvertSingleTsvPlace(entry);

                if (bounds != null && (!bounds.Intersects(md.ElementGeometry.EnvelopeInternal)))
                    continue;

                if (processingMode == "center")
                    md.ElementGeometry = md.ElementGeometry.Centroid;

                sb.Append(md.SourceItemID).Append('\t').Append(md.SourceItemType).Append('\t').Append(md.ElementGeometry.AsText()).Append('\t').Append(md.AreaSize).Append('\t').Append(md.PrivacyId).Append("\r\n");
                reprocFileStream.WriteLine(sb.ToString());
            }
            sr.Close(); sr.Dispose(); fr.Close(); fr.Dispose();
            reprocFileStream.Close(); reprocFileStream.Dispose();
        }

        /// <summary>
        /// Runs through the entire process to convert a PBF file into usable PraxisMapper data. The server bounds for this process must be identified via other functions.
        /// </summary>
        /// <param name="filename">The path to the PBF file to read</param>
        /// <param name="onlyTagMatchedEntries">If true, only load data in the file that meets a rule in TagParser. If false, processes all elements in the file.</param>
        public void ProcessFile(string filename, long relationId = 0)
        {
            try
            {
                if (reprocessFile)
                {
                    ReprocessFileToCenters(filename);
                    return;
                }

                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();

                Open(filename);
                LoadBlockInfo();
                nextBlockId = 0;
                if (relationFinder.IsEmpty)
                {
                    IndexFile();
                    SaveBlockInfo();
                    nextBlockId = BlockCount() - 1;
                    SaveCurrentBlock(BlockCount());
                }
                else
                {
                    var lastBlock = FindLastCompletedBlock();
                    if (lastBlock == -1)
                    {
                        nextBlockId = BlockCount() - 1;
                        SaveCurrentBlock(BlockCount());
                    }
                    else
                        nextBlockId = lastBlock - 1;
                }

                if (displayStatus)
                    ShowWaitInfo();

                filenameHeader += styleSet + "-";

                if (relationId != 0)
                {
                    filenameHeader += relationId.ToString() + "-";
                    //Get the source relation first
                    var relation = GetRelation(relationId);
                    var NTSrelation = GeometrySupport.ConvertOsmEntryToPlace(relation);
                    bounds = NTSrelation.ElementGeometry.EnvelopeInternal;
                    var pgf = new PreparedGeometryFactory();
                    boundsEntry = pgf.Create(NTSrelation.ElementGeometry);
                }

                if (!lowResourceMode) //typical path
                {
                    for (var block = nextBlockId; block >= firstWayBlock; block--)
                    {
                        try
                        {
                            System.Diagnostics.Stopwatch swBlock = new System.Diagnostics.Stopwatch();
                            swBlock.Start();
                            long thisBlockId = block;
                            var geoData = GetGeometryFromBlock(thisBlockId, onlyMatchedAreas);
                            //There are large relation blocks where you can see how much time is spent writing them or waiting for one entry to
                            //process as the apps drops to a single thread in use, but I can't do much about those if I want to be able to resume a process.
                            if (geoData != null) //This process function is sufficiently parallel that I don't want to throw it off to a Task. The only sequential part is writing the data to the file, and I need that to keep accurate track of which blocks have beeen written to the file.
                            {
                                ProcessReaderResults(geoData, block);
                            }
                            SaveCurrentBlock(block);
                            swBlock.Stop();
                            timeList.Add(swBlock.Elapsed);
                            Log.WriteLog("Block " + block + " processed in " + swBlock.Elapsed);
                        }
                        catch
                        {
                            Log.WriteLog("Failed to process block " + block + " normally, trying the low-resourse option", Log.VerbosityLevels.Errors);
                            Thread.Sleep(3000); //Not necessary, but I want to give the GC a chance to clean up stuff before we pick back up.
                            LastChanceRead(block);
                        }
                    }
                    Log.WriteLog("Processing all node blocks....");
                    ProcessAllNodeBlocks(firstWayBlock);
                }
                else
                {
                    //low resource mode
                    //run each entry one at a time, save to disk immediately, don't multithread.
                    for (var block = nextBlockId; block > 0; block--)
                    {
                        LastChanceRead(block);
                    }
                }
                Close();
                CleanupFiles();
                sw.Stop();
                Log.WriteLog("File completed at " + DateTime.Now + ", session lasted " + sw.Elapsed);
            }
            catch (Exception ex)
            {
                while (ex.InnerException != null)
                    ex = ex.InnerException;
                Log.WriteLog("Error processing file: " + ex.Message + ex.StackTrace);
            }
        }

        public void LastChanceRead(long block)
        {
            var thisBlock = GetBlock(block);

            var geoListOfOne = new List<ICompleteOsmGeo>();
            if (thisBlock.primitivegroup[0].relations.Count > 0)
            {
                foreach (var relId in thisBlock.primitivegroup[0].relations)
                {
                    Log.WriteLog("Loading relation with " + relId.memids.Count + " members");
                    geoListOfOne.Add(GetRelation(relId.id, onlyMatchedAreas));
                    ProcessReaderResults(geoListOfOne, block);
                    activeBlocks.Clear();
                    geoListOfOne.Clear();
                }
            }
            else if (thisBlock.primitivegroup[0].ways.Count > 0)
            {
                foreach (var wayId in thisBlock.primitivegroup[0].ways)
                {
                    geoListOfOne.Add(GetWay(wayId.id, null, onlyMatchedAreas));
                    ProcessReaderResults(geoListOfOne, block);
                    activeBlocks.Clear();
                    geoListOfOne.Clear();
                }
            }
            else if (thisBlock.primitivegroup[0].nodes.Count > 0)
            {
                var nodes = GetTaggedNodesFromBlock(thisBlock, onlyMatchedAreas);
                ProcessReaderResults(nodes, block);
            }
            SaveCurrentBlock(block);
        }

        public void ProcessAllNodeBlocks(long maxNodeBlock)
        {
            //Throw each node block into its own thread.
            Parallel.For(1, maxNodeBlock, (block) =>
            {
                var blockData = GetBlock(block);
                var geoData = GetTaggedNodesFromBlock(blockData, onlyMatchedAreas);
                if (geoData != null)
                    ProcessReaderResults(geoData, block);
            });
        }

        //Build the index for entries in this PBF file.
        private void IndexFile()
        {
            Log.WriteLog("Indexing file...");
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            fs.Position = 0;
            long blockCounter = 0;
            blockPositions = new Dictionary<long, long>(initialCapacity);
            blockSizes = new Dictionary<long, int>(initialCapacity);
            relationFinder = new ConcurrentDictionary<long, long>(initialConcurrency, initialCapacity);
            nodeFinder2 = new ConcurrentDictionary<long, Tuple<long, long>>(initialConcurrency, initialCapacity);
            wayFinder = new ConcurrentDictionary<long, long>(initialConcurrency, initialCapacity);

            BlobHeader bh = new BlobHeader();
            Blob b = new Blob();

            HeaderBlock hb = new HeaderBlock();
            PrimitiveBlock pb = new PrimitiveBlock();

            //Only one OsmHeader, at the start
            Serializer.MergeWithLengthPrefix(fs, bh, PrefixStyle.Fixed32BigEndian);
            hb = Serializer.Deserialize<HeaderBlock>(fs, length: bh.datasize); //only one of these per file    
            blockPositions.Add(0, fs.Position);
            blockSizes.Add(0, bh.datasize);

            List<Task> waiting = new List<Task>(initialCapacity);
            int relationCounter = 0;
            int wayCounter = 0;

            //header block is 0, start data blocks at 1
            while (fs.Position != fs.Length)
            {
                blockCounter++;
                Serializer.MergeWithLengthPrefix(fs, bh, PrefixStyle.Fixed32BigEndian);
                blockPositions.Add(blockCounter, fs.Position);
                blockSizes.Add(blockCounter, bh.datasize);

                byte[] thisblob = new byte[bh.datasize];
                fs.Read(thisblob, 0, bh.datasize);

                var passedBC = blockCounter;
                var tasked = Task.Run(() => //Threading makes this run approx. twice as fast.
                {
                    var pb2 = DecodeBlock(thisblob);

                    var group = pb2.primitivegroup[0]; //If i get a file with multiple PrimitiveGroups in a block, make this a ForEach loop instead.
                    if (group.ways.Count > 0)
                    {
                        var wMax = group.ways.Last().id;
                        wayFinder.TryAdd(passedBC, wMax);
                        wayCounter++;
                    }
                    else if (group.relations.Count > 0)
                    {
                        relationCounter++;
                        foreach (var r in group.relations)
                        {
                            relationFinder.TryAdd(r.id, passedBC);
                        }
                    }
                    else
                    {
                        long minNode = 0;
                        long maxNode = 0;
                        if (group.dense != null)
                        {
                            minNode = group.dense.id[0];
                            maxNode = group.dense.id.Sum();
                            nodeFinder2.TryAdd(passedBC, new Tuple<long, long>(minNode, maxNode));
                        }
                    }
                });

                waiting.Add(tasked);
            }
            Task.WaitAll(waiting.ToArray());
            //this logic does require the wayIndex to be in blockID order, which they are (at least from Geofabrik).
            foreach (var w in wayFinder.OrderBy(w => w.Key))
            {
                wayFinderList.Add(Tuple.Create(w.Key, w.Value));
            }
            Log.WriteLog("Found " + blockCounter + " blocks. " + relationCounter + " relation blocks and " + wayCounter + " way blocks.");

            SetOptimizationValues();
            sw.Stop();
            Log.WriteLog("File indexed in " + sw.Elapsed);
        }

        /// <summary>
        /// If a block is already in memory, load it. If it isn't, load it from disk and add it to memory.
        /// </summary>
        /// <param name="blockId">the ID for the block in question</param>
        /// <returns>the PrimitiveBlock requested</returns>
        private PrimitiveBlock GetBlock(long blockId)
        {
            //Track that this entry was requested for this processing block.
            //If the block is in memory, return it.
            //If not, load it from disk and return it.
            PrimitiveBlock results;
            if (!activeBlocks.TryGetValue(blockId, out results))
            {
                results = GetBlockFromFile(blockId);
                activeBlocks.TryAdd(blockId, results);
                accessedBlocks.TryAdd(blockId, true);
            }

            return results;
        }

        /// <summary>
        /// Loads a PrimitiveBlock from the PBF file.
        /// </summary>
        /// <param name="blockId">the block to read from the file</param>
        /// <returns>the PrimitiveBlock requested</returns>
        private PrimitiveBlock GetBlockFromFile(long blockId)
        {
            long pos1 = blockPositions[blockId];
            int size1 = blockSizes[blockId];
            byte[] thisblob1 = new byte[size1];
            lock (msLock)
            {
                fs.Seek(pos1, SeekOrigin.Begin);
                fs.Read(thisblob1, 0, size1);
            }

            var ms2 = new MemoryStream(thisblob1);
            var b2 = Serializer.Deserialize<Blob>(ms2);
            var ms3 = new MemoryStream(b2.zlib_data);
            var dms2 = new ZLibStream(ms3, CompressionMode.Decompress);
            var pulledBlock = Serializer.Deserialize<PrimitiveBlock>(dms2);
            return pulledBlock;
        }

        /// <summary>
        /// Converts the byte array for a block into the PrimitiveBlock object.
        /// </summary>
        /// <param name="blockBytes">the bytes making up the block</param>
        /// <returns>the PrimitiveBlock object requested.</returns>
        private static PrimitiveBlock DecodeBlock(byte[] blockBytes)
        {
            var ms2 = new MemoryStream(blockBytes);
            var b2 = Serializer.Deserialize<Blob>(ms2);
            var ms3 = new MemoryStream(b2.zlib_data);
            var dms2 = new ZLibStream(ms3, CompressionMode.Decompress);

            var pulledBlock = Serializer.Deserialize<PrimitiveBlock>(dms2);
            return pulledBlock;
        }

        private static Relation findRelationInBlockList(List<Relation> primRels, long relId)
        {
            int min = 0;
            int max = primRels.Count;
            int current = max / 2;
            int prevCheck = 0;
            while (min != max && prevCheck != current) //This is a B-Tree search on an array
            {
                var check = primRels[current];
                if (check.id < relId) //This max is below our way, shift min up
                {
                    min = current;
                }
                else if (check.id > relId) //this max is over our way, shift max down
                {
                    max = current;
                }
                else
                    return check;

                prevCheck = current;
                current = (min + max) / 2;
            }
            return null;
        }

        /// <summary>
        /// Processes the requested relation into an OSMSharp CompleteRelation from the currently opened file
        /// </summary>
        /// <param name="relationId">the relation to load and process</param>
        /// <param name="ignoreUnmatched">if true, skip entries that don't get a TagParser match applied to them.</param>
        /// <returns>an OSMSharp CompleteRelation, or null if entries are missing, the elements were unmatched and ignoreUnmatched is true, or there were errors creating the object.</returns>
        private OsmSharp.Complete.CompleteRelation GetRelation(long relationId, bool ignoreUnmatched = false)
        {
            try
            {
                var relationBlockValues = relationFinder[relationId];
                PrimitiveBlock relationBlock = GetBlock(relationBlockValues);

                var relPrimGroup = relationBlock.primitivegroup[0];
                var rel = findRelationInBlockList(relPrimGroup.relations, relationId);
                bool canProcess = false;
                //sanity check - if this relation doesn't have inner or outer role members,
                //its not one i can process.
                foreach (var role in rel.roles_sid)
                {
                    string roleType = System.Text.Encoding.UTF8.GetString(relationBlock.stringtable.s[role]);
                    if (roleType == "inner" || roleType == "outer")
                    {
                        canProcess = true; //I need at least one outer, and inners require outers.
                        break;
                    }
                }

                if (!canProcess)
                    return null;

                //If I only want elements that show up in the map, and exclude areas I don't currently match,
                //I have to knows my tags BEFORE doing the rest of the processing.
                OsmSharp.Complete.CompleteRelation r = new OsmSharp.Complete.CompleteRelation();
                r.Id = relationId;
                r.Tags = new OsmSharp.Tags.TagsCollection(rel.keys.Count);

                for (int i = 0; i < rel.keys.Count; i++)
                {
                    r.Tags.Add(new OsmSharp.Tags.Tag(System.Text.Encoding.UTF8.GetString(relationBlock.stringtable.s[(int)rel.keys[i]]), System.Text.Encoding.UTF8.GetString(relationBlock.stringtable.s[(int)rel.vals[i]])));
                }

                if (ignoreUnmatched)
                {
                    var tpe = TagParser.GetStyleForOsmWay(r.Tags, styleSet);
                    if (tpe.Name == TagParser.defaultStyle.Name)
                        return null; //This is 'unmatched', skip processing this entry.
                }

                //Now get a list of block i know i need now.
                int capacity = rel.memids.Count;
                List<long> wayBlocks = new List<long>(capacity);

                //memIds is delta-encoded
                long idToFind = 0;
                for (int i = 0; i < capacity; i++)
                {
                    idToFind += rel.memids[i];
                    Relation.MemberType typeToFind = rel.types[i];

                    switch (typeToFind)
                    {
                        case Relation.MemberType.NODE:
                            //The FeatureInterpreter doesn't use nodes from a relation
                            break;
                        case Relation.MemberType.WAY:
                            var wayKey = FindBlockKeyForWay(idToFind, wayBlocks);
                            if (!wayBlocks.Contains(wayKey))
                                wayBlocks.Add(wayKey);
                            break;
                        case Relation.MemberType.RELATION: //ignore meta-relations
                            break;
                    }
                }

                //This makes sure we only load each element once. If a relation references an element more than once (it shouldnt)
                //this saves us from re-creating the same entry.
                //Dictionary<long, OsmSharp.Complete.CompleteWay> loadedWays = new Dictionary<long, OsmSharp.Complete.CompleteWay>(capacity);
                r.Members = new OsmSharp.Complete.CompleteRelationMember[capacity];
                idToFind = 0;
                for (int i = 0; i < capacity; i++)
                {
                    idToFind += rel.memids[i];
                    Relation.MemberType typeToFind = rel.types[i];
                    OsmSharp.Complete.CompleteRelationMember c = new OsmSharp.Complete.CompleteRelationMember();
                    c.Role = System.Text.Encoding.UTF8.GetString(relationBlock.stringtable.s[rel.roles_sid[i]]);
                    switch (typeToFind)
                    {
                        case Relation.MemberType.NODE:
                            break;
                        case Relation.MemberType.WAY:
                            //if (!loadedWays.ContainsKey(idToFind))
                            //loadedWays.Add(idToFind, GetWay(idToFind, wayBlocks, false));
                            c.Member = GetWay(idToFind, wayBlocks, false); //loadedWays[idToFind];
                            break;
                    }
                    r.Members[i] = c;
                }

                //Some memory cleanup slightly early, in an attempt to free up RAM faster.
                //loadedWays.Clear();
                //loadedWays = null;
                rel = null;
                return r;
            }
            catch (Exception ex)
            {
                Log.WriteLog("relation failed:" + ex.Message, Log.VerbosityLevels.Errors);
                return null;
            }
        }

        private static Way findWayInBlockList(List<Way> primWays, long wayId)
        {
            int min = 0;
            int max = primWays.Count;
            int current = max / 2;
            int prevCheck = 0;
            while (min != max && prevCheck != current) //This is a B-Tree search on an array
            {
                var check = primWays[current];
                if (check.id < wayId) //This max is below our way, shift min up
                {
                    min = current;
                }
                else if (check.id > wayId) //this max is over our way, shift max down
                {
                    max = current;
                }
                else
                    return check;

                prevCheck = current;
                current = (min + max) / 2;
            }
            return null;
        }

        /// <summary>
        /// Processes the requested way from the currently open file into an OSMSharp CompleteWay
        /// </summary>
        /// <param name="wayId">the way Id to process</param>
        /// <param name="hints">a list of currently loaded blocks to check before doing a full BTree search for entries</param>
        /// <param name="ignoreUnmatched">if true, returns null if this element's tags only match the default style.</param>
        /// <returns>the CompleteWay object requested, or null if skipUntagged or ignoreUnmatched checks skip this elements, or if there is an error processing the way</returns>
        private OsmSharp.Complete.CompleteWay GetWay(long wayId, List<long> hints = null, bool ignoreUnmatched = false)
        {
            try
            {
                var wayBlockValues = FindBlockKeyForWay(wayId, hints);

                PrimitiveBlock wayBlock = GetBlock(wayBlockValues);
                var wayPrimGroup = wayBlock.primitivegroup[0];
                var way = findWayInBlockList(wayPrimGroup.ways, wayId);
                if (way == null)
                    return null; //way wasn't in the block it was supposed to be in.

                return GetWay(way, wayBlock.stringtable.s, ignoreUnmatched);
            }
            catch (Exception ex)
            {
                Log.WriteLog("GetWay failed: " + ex.Message + ex.StackTrace, Log.VerbosityLevels.Errors);
                return null; //Failed to get way, probably because a node didn't exist in the file.
            }
        }

        public record struct NodeBlock(long blockId, long nodeId);

        /// <summary>
        /// Processes the requested way from the currently open file into an OSMSharp CompleteWay
        /// </summary>
        /// <param name="way">the way, in PBF form</param>
        /// <param name="ignoreUnmatched">if true, returns null if this element's tags only match the default style.</param>
        /// <returns>the CompleteWay object requested, or null if skipUntagged or ignoreUnmatched checks skip this elements, or if there is an error processing the way</returns>
        private OsmSharp.Complete.CompleteWay GetWay(Way way, List<byte[]> stringTable, bool ignoreUnmatched = false)
        {
            try
            {
                OsmSharp.Complete.CompleteWay finalway = new OsmSharp.Complete.CompleteWay();
                finalway.Id = way.id;
                finalway.Tags = new OsmSharp.Tags.TagsCollection(way.keys.Count);

                //We always need to apply tags here, so we can either skip after (if IgnoredUmatched is set) or to pass along tag values correctly.
                for (int i = 0; i < way.keys.Count; i++)
                    finalway.Tags.Add(new OsmSharp.Tags.Tag(System.Text.Encoding.UTF8.GetString(stringTable[(int)way.keys[i]]), System.Text.Encoding.UTF8.GetString(stringTable[(int)way.vals[i]])));

                if (ignoreUnmatched)
                {
                    if (TagParser.GetStyleForOsmWay(finalway.Tags, styleSet).Name == TagParser.defaultStyle.Name)
                        return null; //don't process this one, we said not to load entries that aren't already in our style list.
                }

                //NOTES:
                //This gets all the entries we want from each node, then loads those all in 1 pass per referenced block.
                //This is significantly faster than doing a GetBlock per node when 1 block has mulitple entries
                //its a little complicated but a solid performance boost.
                long idToFind = 0; //more deltas 
                //blockId, nodeID
                List<NodeBlock> nodesPerBlock = new List<NodeBlock>();
                List<long> hints = new List<long>(nodeHintsMax);
                //long hint = 0;
                for (int i = 0; i < way.refs.Count; i++)
                {
                    idToFind += way.refs[i];
                    var blockID = FindBlockKeyForNode(idToFind, hints);
                    //var blockID = FindBlockKeyForNode(idToFind, hint);
                    //hint = blockID;
                    if (!hints.Contains(blockID))
                    hints.Add(blockID);
                    nodesPerBlock.Add(new NodeBlock(blockID, idToFind));
                }
                var nodesByBlock = nodesPerBlock.ToLookup(k => k.blockId, v => v.nodeId);

                finalway.Nodes = new OsmSharp.Node[way.refs.Count];
                ConcurrentDictionary<long, OsmSharp.Node> AllNodes = new ConcurrentDictionary<long, OsmSharp.Node>(Environment.ProcessorCount, way.refs.Count);
                Parallel.ForEach(nodesByBlock, (block) =>
                {
                    var someNodes = GetAllNeededNodesInBlock(block.Key, block.Distinct().OrderBy(b => b).ToArray());
                    foreach (var n in someNodes)
                        AllNodes.TryAdd(n.Key, n.Value);
                });

                idToFind = 0;
                for (int i = 0; i < way.refs.Count; i++)
                {
                    idToFind += way.refs[i]; //delta coding.
                    finalway.Nodes[i] = AllNodes[idToFind];
                }

                return finalway;
            }
            catch (Exception ex)
            {
                Log.WriteLog("GetWay failed: " + ex.Message + ex.StackTrace, Log.VerbosityLevels.Errors);
                return null; //Failed to get way, probably because a node didn't exist in the file.
            }
        }

        /// <summary>
        /// Returns the Nodes that have tags applied from a block.
        /// </summary>
        /// <param name="block">the block of Nodes to search through</param>
        /// <param name="ignoreUnmatched">if true, skip nodes that have tags that only match the default TaParser style.</param>
        /// <returns>a list of Nodes with tags, which may have a length of 0.</returns>
        private List<OsmSharp.Node> GetTaggedNodesFromBlock(PrimitiveBlock block, bool ignoreUnmatched = false)
        {
            List<OsmSharp.Node> taggedNodes = new List<OsmSharp.Node>(400); //2% of nodes have tags, 10x that to get this set for the majority of blocks.
            var dense = block.primitivegroup[0].dense;

            //Shortcut: if dense.keys.count == dense.id.count, there's no tagged nodes at all here (0 means 'no keys', and all 0's means every entry has no keys)
            if (dense.keys_vals.Count == dense.id.Count)
                return taggedNodes;

            //sort out tags ahead of time.
            int entryCounter = 0;
            List<Tuple<int, string, string>> idKeyVal = new List<Tuple<int, string, string>>((dense.keys_vals.Count - dense.id.Count) / 2);
            for (int i = 0; i < dense.keys_vals.Count; i++)
            {
                if (dense.keys_vals[i] == 0)
                {
                    //skip to next entry.
                    entryCounter++;
                    continue;
                }

                idKeyVal.Add(
                    Tuple.Create(entryCounter,
                System.Text.Encoding.UTF8.GetString(block.stringtable.s[dense.keys_vals[i]]),
                System.Text.Encoding.UTF8.GetString(block.stringtable.s[dense.keys_vals[i + 1]])
                ));
                i++;
            }
            var decodedTags = idKeyVal.ToLookup(k => k.Item1, v => new OsmSharp.Tags.Tag(v.Item2, v.Item3));
            var lastTaggedNode = decodedTags.Max(i => i.Key);

            var index = -1;
            long nodeId = 0;
            long lat = 0;
            long lon = 0;
            foreach (var denseNode in dense.id)
            {
                index++;
                nodeId += denseNode;
                lat += dense.lat[index];
                lon += dense.lon[index];

                if (!decodedTags[index].Any())
                    continue;

                //now, start loading keys/values
                OsmSharp.Tags.TagsCollection tc = new OsmSharp.Tags.TagsCollection(decodedTags[index]);

                if (ignoreUnmatched)
                {
                    if (TagParser.GetStyleForOsmWay(tc, styleSet) == TagParser.defaultStyle)
                        continue;
                }

                OsmSharp.Node n = new OsmSharp.Node();
                n.Id = nodeId;
                n.Latitude = DecodeLatLon(lat, block.lat_offset, block.granularity);
                n.Longitude = DecodeLatLon(lon, block.lon_offset, block.granularity);
                n.Tags = tc;

                //if bounds checking, drop nodes that aren't needed.
                if (bounds == null || (n.Latitude >= bounds.MinY && n.Latitude <= bounds.MaxY && n.Longitude >= bounds.MinX && n.Longitude <= bounds.MaxX))
                    taggedNodes.Add(n);

                if (index >= lastTaggedNode)
                    break;
            }

            return taggedNodes;
        }

        /// <summary>
        /// Pulls out all requested nodes from a block. Significantly faster to pull all nodes per block this way than to run through the list for each node.
        /// </summary>
        /// <param name="blockId">the block to pull nodes out of</param>
        /// <param name="nodeIds">the IDs of nodes to load from this block</param>
        /// <returns>a Dictionary of the node ID and corresponding values.</returns>
        private Dictionary<long, OsmSharp.Node> GetAllNeededNodesInBlock(long blockId, long[] nodeIds)
        {
            Dictionary<long, OsmSharp.Node> results = new Dictionary<long, OsmSharp.Node>(nodeIds.Length);
            int arrayIndex = 0;

            var block = GetBlock(blockId);
            var group = block.primitivegroup[0].dense;

            int index = -1;
            long nodeCounter = 0;
            long latDelta = 0;
            long lonDelta = 0;
            var denseIds = group.id;
            var dLat = group.lat;
            var dLon = group.lon;
            var nodeToFind = nodeIds[arrayIndex];
            while (arrayIndex < 8000)
            {
                index++;

                nodeCounter += denseIds[index];
                latDelta += dLat[index];
                lonDelta += dLon[index];

                //if (nodeIds[arrayIndex] == nodeCounter)
                if (nodeToFind == nodeCounter)
                {
                    OsmSharp.Node filled = new OsmSharp.Node();
                    filled.Id = nodeCounter;
                    filled.Latitude = DecodeLatLon(latDelta, block.lat_offset, block.granularity);
                    filled.Longitude = DecodeLatLon(lonDelta, block.lon_offset, block.granularity);
                    results.Add(nodeCounter, filled);
                    arrayIndex++;
                    if (arrayIndex == nodeIds.Length)
                        return results;
                    nodeToFind = nodeIds[arrayIndex];
                }
            }
            return results;
        }

        /// <summary>
        /// Determine if a block is expected to have the given node by its nodeID, using its indexed values
        /// </summary>
        /// <param name="key">the NodeId to check for in this block</param>
        /// <param name="value">the Tuple of min and max node IDs in a block.</param>
        /// <returns>true if key is between the 2 Tuple values, or false ifnot.</returns>
        private static bool NodeHasKey(long key, Tuple<long, long> value)
        {
            //key is block id
            //value is the tuple list. 1 is min, 2 is max.
            if (value.Item1 > key) //this node's minimum is larger than our node, skip
                return false;

            if (value.Item2 < key) //this node's maximum is smaller than our node, skip
                return false;
            return true;
        }

        /// <summary>
        /// Determine which node in the file has the given Node, using a BTree search on the index.
        /// </summary>
        /// <param name="nodeId">The node to find in the currently opened file</param>
        /// <param name="hints">a list of blocks to check first, assuming that nodes previously searched are likely to be near each other. Ignored if more than 20 entries are in the list. </param>
        /// <returns>the block ID containing the requested node</returns>
        /// <exception cref="Exception">Throws an exception if the nodeId isn't found in the current file.</exception>
        private long FindBlockKeyForNode(long nodeId, List<long> hints = null) //BTree
        {
            //This is the most-called function in this class, and therefore the most performance-dependent.

            //Hints is a list of blocks we're already found in the relevant way. Odds are high that
            //any node I need to find is in the same block as another node I've found.
            //This should save a lot of time searching the list when I have already found some blocks
            //and shoudn't waste too much time if it isn't in a block already found.
            if (hints != null && hints.Count < nodeHintsMax) //skip hints if the BTree search is fewer checks.
            {
                foreach (var h in hints)
                {
                    var entry = nodeFinder2[h];
                    if (NodeHasKey(nodeId, entry))
                        return h;
                }
            }

            //ways will hit a couple thousand blocks, nodes hit hundred of thousands of blocks.
            //This might help performance on ways, but will be much more noticeable on nodes.
            int min = 0;
            int max = nodeFinderTotal;
            int current = startNodeBtreeIndex;

            while (min != max)
            {
                var check = nodeFinderList[current];
                if (check.Item2 > nodeId) //this node's minimum is larger than our node, shift up
                    max = current;
                else if (check.Item3 < nodeId) //this node's maximum is smaller than our node, shift down.
                    min = current;
                else
                    return check.Item1;

                current = (min + max) / 2;
            }
            throw new Exception("Node Not Found");
        }

        private long FindBlockKeyForNode(long nodeId, long hint) //BTree
        {
            //This is the most-called function in this class, and therefore the most performance-dependent.

            //Hints is a list of blocks we're already found in the relevant way. Odds are high that
            //any node I need to find is in the same block as another node I've found.
            //This should save a lot of time searching the list when I have already found some blocks
            //and shoudn't waste too much time if it isn't in a block already found.
            if (hint != 0) //skip hints if the BTree search is fewer checks.
            {
                var entry = nodeFinder2[hint];
                if (NodeHasKey(nodeId, entry))
                    return hint;
            }

            //ways will hit a couple thousand blocks, nodes hit hundred of thousands of blocks.
            //This might help performance on ways, but will be much more noticeable on nodes.
            int min = 0;
            int max = nodeFinderTotal;
            int current = startNodeBtreeIndex;

            while (min != max)
            {
                var check = nodeFinderList[current];
                if (check.Item2 > nodeId) //this node's minimum is larger than our node, shift up
                    max = current;
                else if (check.Item3 < nodeId) //this node's maximum is smaller than our node, shift down.
                    min = current;
                else
                    return check.Item1;

                current = (min + max) / 2;
            }
            throw new Exception("Node Not Found");
        }

        /// <summary>
        /// Determine which node in the file has the given Way, using a BTree search on the index.
        /// </summary>
        /// <param name="wayId">The way to find in the currently opened file</param>
        /// <param name="hints">a list of blocks to check first, assuming that blocks previously searched are likely to be near each other. Ignored if more than 20 entries are in the list. </param>
        /// <returns>the block ID containing the requested way</returns>
        /// <exception cref="Exception">Throws an exception if the way isn't found in the current file.</exception>

        private long FindBlockKeyForWay(long wayId, List<long> hints) //BTree
        {
            if (hints != null && hints.Count < wayHintsMax) //skip hints if the BTree search is fewer checks.
                foreach (var h in hints)
                {
                    //we can check this, but we need to look at the previous block too.
                    if (wayFinder[h] >= wayId && (h == firstWayBlock || wayFinder[h - 1] < wayId))
                        return h;
                }

            int min = 0;
            int max = wayFinderTotal;
            int current = startWayBtreeIndex;
            while (min != max)
            {
                var check = wayFinderList[current];
                if (check.Item2 < wayId) //This max is below our way, shift min up
                {
                    min = current;
                }
                else if (check.Item2 >= wayId) //this max is over our way, check previous block if this one is correct OR shift max down if not
                {
                    if (current == 0 || wayFinderList[current - 1].Item2 < wayId) //our way is below current max, above previous max, this is the block we want
                        return check.Item1;
                    else
                        max = current;
                }

                current = (min + max) / 2;
            }

            //couldnt find this way
            throw new Exception("Way Not Found");
        }

        private long FindBlockKeyForWay(long wayId, long hint) //BTree
        {
            if (hint != null) //skip hints if the BTree search is fewer checks.
                              //we can check this, but we need to look at the previous block too.
                if (wayFinder[hint] >= wayId && (wayFinder[hint - 1] < wayId || hint == firstWayBlock))
                    return hint;


            int min = 0;
            int max = wayFinderTotal;
            int current = startWayBtreeIndex;
            while (min != max)
            {
                var check = wayFinderList[current];
                if (check.Item2 < wayId) //This max is below our way, shift min up
                {
                    min = current;
                }
                else if (check.Item2 >= wayId) //this max is over our way, check previous block if this one is correct OR shift max down if not
                {
                    if (current == 0 || wayFinderList[current - 1].Item2 < wayId) //our way is below current max, above previous max, this is the block we want
                        return check.Item1;
                    else
                        max = current;
                }

                current = (min + max) / 2;
            }

            //couldnt find this way
            throw new Exception("Way Not Found");
        }

        /// <summary>
        /// Processes all entries in a PBF block for use in a PraxisMapper server.
        /// </summary>
        /// <param name="blockId">the block to process</param>
        /// <param name="onlyTagMatchedEntries">if true, skips elements that match the default style for the TagParser style set</param>
        /// <returns>A ConcurrentBag of OSMSharp CompleteGeo objects.</returns>
        public ConcurrentBag<OsmSharp.Complete.ICompleteOsmGeo> GetGeometryFromBlock(long blockId, bool onlyTagMatchedEntries = false)
        {
            //This grabs the chosen block, populates everything in it to an OsmSharp.Complete object and returns that list
            ConcurrentBag<OsmSharp.Complete.ICompleteOsmGeo> results = new ConcurrentBag<OsmSharp.Complete.ICompleteOsmGeo>();
            try
            {
                var block = GetBlock(blockId);
                //Attempting to clear up some memory slightly faster, but this should be redundant.
                relList.Clear();
                foreach (var primgroup in block.primitivegroup)
                {
                    if (primgroup.relations != null && primgroup.relations.Count > 0)
                    {
                        //Some relation blocks can hit 22GB of RAM on their own. Low-resource machines will fail, and should roll into the LastChance path automatically.
                        foreach (var r in primgroup.relations)
                            relList.Add(Task.Run(() => results.Add(GetRelation(r.id, onlyTagMatchedEntries))));

                        Task.WaitAll(relList.ToArray());
                    }
                    else if (primgroup.ways != null && primgroup.ways.Count > 0)
                    {
                        List<long> hint = new List<long>() { blockId };
                        foreach (var r in primgroup.ways)
                        {
                            relList.Add(Task.Run(() => results.Add(GetWay(r, block.stringtable.s, onlyTagMatchedEntries))));
                        }
                    }
                    else
                    {
                        //Useful node lists are so small, they lose performance from splitting each step into 1 task per entry.
                        //Inline all that here as one task and return null to skip the rest.
                        relList.Add(Task.Run(() =>
                        {
                            try
                            {
                                var nodes = GetTaggedNodesFromBlock(block, onlyTagMatchedEntries);
                                results = new ConcurrentBag<ICompleteOsmGeo>(nodes);
                            }
                            catch (Exception ex)
                            {
                                Log.WriteLog("Processing node failed: " + ex.Message, Log.VerbosityLevels.Errors);
                            }
                        }));
                    }
                }

                Task.WaitAll(relList.ToArray());

                //Moved this logic here to free up RAM by removing blocks once we're done reading data from the hard drive. Should result in fewer errors at the ProcessReaderResults step.
                //Slightly more complex: only remove blocks we didn't access last call. saves some serialization effort. Small RAM trade for 30% speed increase.
                if (!keepAllBlocksInRam)
                    foreach (var blockRead in activeBlocks)
                    {
                        if (!accessedBlocks.ContainsKey(blockRead.Key))
                            activeBlocks.TryRemove(blockRead.Key, out var x);
                    }
                accessedBlocks.Clear();
                return results;
            }
            catch (Exception ex)
            {
                Log.WriteLog("Error getting geometry: " + ex.Message, Log.VerbosityLevels.Errors);
                throw ex; //In order to reprocess this block in last-chance mode.
                //return null;
            }
        }

        //Taken from OsmSharp (MIT License)
        /// <summary>
        /// Turns a PBF's dense stored data into a standard latitude or longitude value in degrees.
        /// </summary>
        /// <param name="valueOffset">the valueOffset for the block data is loaded from</param>
        /// <param name="offset">the offset for the node currently loaded</param>
        /// <param name="granularity">the granularity value of the block data is loaded from</param>
        /// <returns>a double represeting the lat or lon value for the given dense values</returns>
        private static double DecodeLatLon(long valueOffset, long offset, long granularity)
        {
            return .000000001 * (offset + (granularity * valueOffset));
        }
        //end OsmSharp copied functions.

        /// <summary>
        /// Saves the indexes created for the currently opened file to their own files, so that they can be read instead of created if processing needs to resume later.
        /// </summary>
        private void SaveBlockInfo()
        {
            string filename = outputPath + fi.Name + ".blockinfo";
            //now deserialize
            string[] data = new string[blockPositions.Count];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = i + ":" + blockPositions[i] + ":" + blockSizes[i];
            }
            File.WriteAllLines(filename, data);

            filename = outputPath + fi.Name + ".relationIndex";
            data = new string[relationFinder.Count];
            int j = 0;
            foreach (var wf in relationFinder)
            {
                data[j] = wf.Key + ":" + wf.Value;
                j++;
            }
            File.WriteAllLines(filename, data);

            filename = outputPath + fi.Name + ".wayIndex";
            data = new string[wayFinderTotal];
            j = 0;
            foreach (var wf in wayFinderList)
            {
                data[j] = wf.Item1 + ":" + wf.Item2;
                j++;
            }
            File.WriteAllLines(filename, data);

            filename = outputPath + fi.Name + ".nodeIndex";
            data = new string[nodeFinder2.Count];
            j = 0;
            foreach (var wf in nodeFinder2)
            {
                data[j] = wf.Key + ":" + wf.Value.Item1 + ":" + wf.Value.Item2;
                j++;
            }
            File.WriteAllLines(filename, data);
        }

        /// <summary>
        /// Loads indexed data from a previous run from file, to skip reprocessing the entire file.
        /// </summary>
        private void LoadBlockInfo()
        {
            try
            {
                string filename = outputPath + fi.Name + ".blockinfo";
                string[] data = File.ReadAllLines(filename);
                blockPositions = new Dictionary<long, long>(data.Length);
                blockSizes = new Dictionary<long, int>(data.Length);

                for (int i = 0; i < data.Length; i++)
                {
                    string[] subdata = data[i].Split(":");
                    blockPositions[i] = long.Parse(subdata[1]);
                    blockSizes[i] = int.Parse(subdata[2]);
                }

                filename = outputPath + fi.Name + ".relationIndex";
                data = File.ReadAllLines(filename);
                foreach (var line in data)
                {
                    string[] subData2 = line.Split(":");
                    relationFinder.TryAdd(long.Parse(subData2[0]), long.Parse(subData2[1]));
                }

                filename = outputPath + fi.Name + ".wayIndex";
                data = File.ReadAllLines(filename);
                foreach (var line in data)
                {
                    string[] subData2 = line.Split(":");
                    wayFinder.TryAdd(long.Parse(subData2[0]), long.Parse(subData2[1]));
                    wayFinderList.Add(Tuple.Create(long.Parse(subData2[0]), long.Parse(subData2[1])));
                }

                filename = outputPath + fi.Name + ".nodeIndex";
                data = File.ReadAllLines(filename);
                foreach (var line in data)
                {
                    string[] subData2 = line.Split(":");
                    nodeFinder2.TryAdd(long.Parse(subData2[0]), Tuple.Create(long.Parse(subData2[1]), long.Parse(subData2[2])));
                }

                SetOptimizationValues();

            }
            catch (Exception ex)
            {
                return;
            }
        }

        /// <summary>
        /// Use the indexed data to store a few values needed for optimiazations to work at their best.
        /// </summary>
        private void SetOptimizationValues()
        {
            foreach (var entry in nodeFinder2)
            {
                nodeFinderList.Add(Tuple.Create(entry.Key, entry.Value.Item1, entry.Value.Item2));
            }

            nodeFinderTotal = nodeFinderList.Count;
            wayFinderTotal = wayFinderList.Count;
            firstWayBlock = wayFinder.Keys.Min();
            firstRelationBlock = relationFinder.Values.Min();
            startNodeBtreeIndex = nodeFinderTotal / 2;
            startWayBtreeIndex = wayFinderTotal / 2;
            nodeHintsMax = (int)Math.Log2(nodeFinderTotal);
            wayHintsMax = (int)Math.Log2(wayFinderTotal);
        }

        /// <summary>
        /// Saves the currently completed block to a file, so we can resume without reprocessing existing data if needed.
        /// </summary>
        /// <param name="blockID">the block most recently processed</param>
        private void SaveCurrentBlock(long blockID)
        {
            string filename = outputPath + fi.Name + ".progress";
            File.WriteAllText(filename, blockID.ToString());
        }

        //Loads the most recently completed block from a file to resume without doing duplicate work.
        private long FindLastCompletedBlock()
        {
            try
            {
                string filename = outputPath + fi.Name + ".progress";
                long blockID = long.Parse(File.ReadAllText(filename));
                return blockID;
            }
            catch (Exception ex)
            {
                return -1;
            }
        }

        /// <summary>
        /// Delete indexes and progress file.
        /// </summary>
        private void CleanupFiles()
        {
            try
            {
                if (!keepIndexFiles)
                {
                    foreach (var file in Directory.EnumerateFiles(outputPath, "*.blockinfo"))
                        File.Delete(file);

                    foreach (var file in Directory.EnumerateFiles(outputPath, "*.relationIndex"))
                        File.Delete(file);

                    foreach (var file in Directory.EnumerateFiles(outputPath, "*.nodeIndex"))
                        File.Delete(file);

                    foreach (var file in Directory.EnumerateFiles(outputPath, "*.wayIndex"))
                        File.Delete(file);
                }

                foreach (var file in Directory.EnumerateFiles(outputPath, "*.progress"))
                    File.Delete(file);
            }
            catch (Exception ex)
            {
                Log.WriteLog("Error cleaning up files: " + ex.Message, Log.VerbosityLevels.Errors);
            }
        }

        /// <summary>
        /// Called to display periodic performance summaries on the console while a file is being processed.
        /// </summary>
        public void ShowWaitInfo()
        {
            Task.Run(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    Log.WriteLog("Current stats:");
                    Log.WriteLog("Blocks completed this run: " + timeList.Count);
                    Log.WriteLog("Processing tasks: " + relList.Count(r => !r.IsCompleted));
                    if (!timeList.IsEmpty)
                    {
                        Log.WriteLog("Average time per block: " + timeList.Average(t => t.TotalSeconds) + " seconds");

                        var slowBlocks = BlockCount() - firstWayBlock;
                        var fastBlocks = firstWayBlock - 1;
                        var slowBlocksLeft = slowBlocks - timeList.Count;
                        var estimatedTimeLeft = (slowBlocksLeft > 0 ? (timeList.Average(t => t.TotalSeconds) * slowBlocksLeft) : 0);
                        estimatedTimeLeft += (nodeFinderTotal - timeList.Count) * .05; //very loose estimate on node duration
                        TimeSpan t = new TimeSpan((long)estimatedTimeLeft * 10000000);
                        Log.WriteLog("Estimated Time Remaining: " + t);
                    }
                    Thread.Sleep(60000);
                }
            }, token);
        }

        /// <summary>
        /// Take a list of OSMSharp CompleteGeo items, and convert them into PraxisMapper's Place objects.
        /// </summary>
        /// <param name="items">the OSMSharp CompleteGeo items to convert</param>
        /// <param name="saveFilename">The filename to save data to. Ignored if saveToDB is true</param>
        /// <param name="saveToDb">If true, insert the items directly to the database instead of exporting to files.</param>
        /// <param name="onlyTagMatchedElements">if true, only loads in elements that dont' match the default entry for a TagParser style set</param>
        /// <returns>the Task handling the conversion process</returns>
        public void ProcessReaderResults(IEnumerable<OsmSharp.Complete.ICompleteOsmGeo> items, long blockId)
        {
            //This one is easy, we just dump the geodata to the file.
            string saveFilename = outputPath + Path.GetFileNameWithoutExtension(fi.Name) + "-" + blockId;
            ConcurrentBag<DbTables.Place> elements = new ConcurrentBag<DbTables.Place>();

            if (items == null || !items.Any())
                return;

            relList = new ConcurrentBag<Task>();
            foreach (var r in items)
            {
                if (r != null)
                    relList.Add(Task.Run(() => { var e = GeometrySupport.ConvertOsmEntryToPlace(r); if (e != null) elements.Add(e); }));
            }
            Task.WaitAll(relList.ToArray());
            //relList = new ConcurrentBag<Task>();

            if (onlyMatchedAreas)
                elements = new ConcurrentBag<DbTables.Place>(elements.Where(e => TagParser.GetStyleForOsmWay(e.Tags, styleSet).Name != TagParser.defaultStyle.Name));

            if (boundsEntry != null)
                elements = new ConcurrentBag<DbTables.Place>(elements.Where(e => boundsEntry.Intersects(e.ElementGeometry)));

            if (elements.IsEmpty)
                return;

            //Single check per block to fix points having 0 size.
            if (elements.First().SourceItemType == 1)
                foreach (var e in elements)
                    e.AreaSize = ConstantValues.resolutionCell10;

            if (processingMode == "center")
                foreach (var e in elements)
                    e.ElementGeometry = e.ElementGeometry.Centroid;

            if (saveToDB) //If this is on, we skip the file-writing part and send this data directly to the DB. Single threaded, but doesn't waste disk space with intermediate files.
            {
                var db = new PraxisContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                db.Places.AddRange(elements);
                db.SaveChanges();
                return;
            }
            else
            {
                //Starts with some data allocated in each 2 stringBuilders to minimize reallocations. In my test setup, 10kb is the median value for all files, and 100kb is enough for 90% of blocks
                StringBuilder geometryBuilds = new StringBuilder(100000); //100kb
                StringBuilder tagBuilds = new StringBuilder(40000); //40kb, tags are usually smaller than geometry.
                foreach (var md in elements)
                {
                    geometryBuilds.Append(md.SourceItemID).Append('\t').Append(md.SourceItemType).Append('\t').Append(md.ElementGeometry.AsText()).Append('\t').Append(md.AreaSize).Append('\t').Append(Guid.NewGuid()).Append("\r\n");
                    foreach (var t in md.Tags)
                        tagBuilds.Append(md.SourceItemID).Append('\t').Append(md.SourceItemType).Append('\t').Append(t.Key).Append('\t').Append(t.Value.Replace("\r", "").Replace("\n", "")).Append("\r\n"); //Might also need to sanitize / and ' ?
                }
                try
                {
                    Parallel.Invoke(
                        () => File.AppendAllText(saveFilename + ".geomData", geometryBuilds.ToString()),
                        () => File.AppendAllText(saveFilename + ".tagsData", tagBuilds.ToString())
                    );
                }
                catch (Exception ex)
                {
                    Log.WriteLog("Error writing data to disk:" + ex.Message, Log.VerbosityLevels.Errors);
                }
            }

            return; //some invalid options were passed and we didnt run through anything.
        }

        /// <summary>
        /// Pull a single relation out of the given PBF file as an OSMSharp CompleteRelation. Will index the file as normal if needed, but does not clean up the indexed file to allow for reuse later.
        /// </summary>
        /// <param name="filename">The filename containing the relation</param>
        /// <param name="relationId">the relation to process</param>
        /// <returns>The CompleteRelation requested, or null if it was unable to be created from the file.</returns>
        public CompleteRelation LoadOneRelationFromFile(string filename, long relationId)
        {
            Log.WriteLog("Starting to load one relation from file.");
            try
            {
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                Open(filename);
                LoadBlockInfo();
                nextBlockId = 0;
                if (relationFinder.IsEmpty)
                {
                    IndexFile();
                    SaveBlockInfo();
                    SaveCurrentBlock(BlockCount());
                }
                nextBlockId = BlockCount() - 1;

                if (displayStatus)
                    ShowWaitInfo();

                var relation = GetRelation(relationId);
                Close();
                sw.Stop();
                Log.WriteLog("Processing completed at " + DateTime.Now + ", session lasted " + sw.Elapsed);
                return relation;
            }
            catch (Exception ex)
            {
                while (ex.InnerException != null)
                    ex = ex.InnerException;
                Log.WriteLog("Error processing file: " + ex.Message + ex.StackTrace);
                return null;
            }
        }

        /// <summary>
        /// Pull a single Way out of the given PBF file as an OSMSharp CompleteWay. Will index the file as normal if needed, but does not clean up the indexed file to allow for reuse later.
        /// </summary>
        /// <param name="filename">The filename containing the relation</param>
        /// <param name="wayId">the relation to process</param>
        /// <returns>the CompleteWay requested, or null if it was unable to be created from the file.</returns>
        public CompleteWay LoadOneWayFromFile(string filename, long wayId)
        {
            Log.WriteLog("Starting to load one relation from file.");
            try
            {
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                Open(filename);
                LoadBlockInfo();
                nextBlockId = 0;
                if (relationFinder.IsEmpty)
                {
                    IndexFile();
                    SaveBlockInfo();
                    SaveCurrentBlock(BlockCount());
                }
                nextBlockId = BlockCount() - 1;

                if (displayStatus)
                    ShowWaitInfo();

                var way = GetWay(wayId, ignoreUnmatched: false);
                Close();
                sw.Stop();
                Log.WriteLog("Processing completed at " + DateTime.Now + ", session lasted " + sw.Elapsed);
                return way;
            }
            catch (Exception ex)
            {
                while (ex.InnerException != null)
                    ex = ex.InnerException;
                Log.WriteLog("Error processing file: " + ex.Message + ex.StackTrace);
                return null;
            }
        }
    }
}
