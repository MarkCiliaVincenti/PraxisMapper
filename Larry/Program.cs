﻿using CryptSharp;
using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using PraxisCore;
using PraxisCore.PbfReader;
using PraxisCore.Support;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static PraxisCore.ConstantValues;
using static PraxisCore.DbTables;
using static PraxisCore.Place;
using static PraxisCore.Singletons;

//TODO: Ponder using https://download.bbbike.org/osm/ as a data source to get a custom extract of an area (for when users want a local-focused app, probably via a wizard GUI)
//OR could use an additional input for filterbox.

namespace Larry
{
    class Program
    {
        static IConfigurationRoot config;
        static List<DbTables.Place> memorySource;
        static IMapTiles MapTiles;

        static void Main(string[] args)
        {
            var builder = new ConfigurationBuilder()
            .AddJsonFile("Larry.config.json");
            config = builder.Build();

            ApplyConfigValues();
            //If multiple args are supplied, run them in the order that make sense, not the order the args are supplied.
            if (args.Any(a => a == "-createDB")) //setup the destination database
            {
                createDb();
            }
            
            if (!args.Any(a => a == "-makeServerDb")) //This will not be available until after creating the DB slightly later.
                TagParser.Initialize(config["ForceTagParserDefaults"] == "True", MapTiles); //This last bit of config must be done after DB creation check

            Log.WriteLog("Larry started at " + DateTime.Now);

            if (args.Count() == 0)
            {
                Log.WriteLog("You must pass an arguement to this application", Log.VerbosityLevels.High);
                //TODO: list valid commands or point at the docs file
                return;
            }

            //if (args.Any(a => a.StartsWith("-getPbf:")))
            //{
            //    //Wants 3 pieces. Drops in placeholders if some are missing. Giving no parameters downloads Ohio.
            //    string arg = args.First(a => a.StartsWith("-getPbf:")).Replace("-getPbf:", "");
            //    var splitData = arg.Split('|'); //remember the first one will be empty.
            //    string level1 = splitData.Count() >= 4 ? splitData[3] : "north-america";
            //    string level2 = splitData.Count() >= 3 ? splitData[2] : "us";
            //    string level3 = splitData.Count() >= 2 ? splitData[1] : "ohio";

            //    DownloadPbfFile(level1, level2, level3, config["PbfFolder"]);
            //}

            if (args.Any(a => a == "-resetPbf"))
            {
                ResetFiles(config["PbfFolder"]);
            }

            if (args.Any(a => a == "-resetGeomData"))
            {
                ResetFiles(config["OutputDataFolder"]);
            }

            if (args.Any(a => a == "-resetStyles"))
            {
                var db = new PraxisContext();
                db.ResetStyles();
            }

            if (args.Any(a => a == "-processPbfs"))
            {
                processPbfs();
            }

            if (args.Any(a => a == "-loadProcessedData"))
            {
                loadProcessedData();
            }

            //This is the single command to get a server going, assuming you have done all the setup steps yourself beforehand and your config is correct. 
            if (args.Any(a => a == "-makeServerDb"))
            {
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                SetEnvValues();
                var db = new PraxisContext();
                createDb();
                db.DropIndexes();
                processPbfs();
                loadProcessedData();
                db.SetServerBounds(long.Parse(config["UseOneRelationID"]));
                Log.WriteLog("Server setup complete in " + sw.Elapsed);
            }

            if (args.Any(a => a == "-makeWholeServer")) //Not a release 1 feature, but taking notes now.
            {
                SetEnvValues();
                //This is the wizard command, try to check and do everything at once.
                Log.WriteLog("Checking for installed DB per config (" + config["DbMode"] + ")");
                PraxisContext db;
                try
                {
                    db = new PraxisContext();
                }
                //Specific exceptions should hint at what to do, a general one covers ones I dont know how to handle.
                catch (Exception ex)
                {
                    Log.WriteLog("Hit an error checking for the existing database that I'm not sure how to handle:" + ex.Message);
                    return;
                }

                Log.WriteLog("Creating the Praxis DB per the connection string...");
                try
                {
                    createDb();
                }
                catch (Exception ex)
                {
                    //figure out why i can't create. Probably account settings?
                }

                PwdSpeedTest();


                //Check for MariaDB and install/configure if missing (including service account)
                //check for a PBF file and prompt to download one if none found
                //if data files are present, use them. otherwise process the PBF file per settings
                //Pre-generate gameplay map tiles, but present it as an option. It's faster to do it ahead of time but uses up more DB space if you aren't gonna need them all immediately.
                //Possible: Grab the Solar2D example app, adjust it to work with the server running on this machine.
                //--check external IP, update .lua source file to point to this pc.
                //Fire up the Kestral exe to get the server working
                //Open up a browser to the adminview slippytile page.
                //}
            }

            if (args.Any(a => a == "-updateDatabase"))
            {
                UpdateExistingEntries(config["OutputDataFolder"]);
            }

            if (args.Any(a => a.StartsWith("-createStandaloneRelation")))
            {
                //This makes a standalone DB for a specific relation passed in as a paramter. 
                int relationId = Int32.Parse(config["UseOneRelationID"]);
                StandaloneCreation.CreateStandaloneDB(relationId, null, false, true); //How map tiles are handled is determined by the optional parameters
            }

            if (args.Any(a => a.StartsWith("-createStandaloneBox")))
            {
                //This makes a standalone DB for a specific area passed in as a paramter.
                //If you want to cover a region in a less-specific way, or the best available relation is much larger than you thought, this might be better.
                string[] bounds = args.First(a => a.StartsWith("-createStandaloneBox")).Split('|');
                GeoArea boundsArea = new GeoArea(bounds[1].ToDouble(), bounds[2].ToDouble(), bounds[3].ToDouble(), bounds[4].ToDouble());

                //in order, these go south/west/north/east.
                StandaloneCreation.CreateStandaloneDB(0, boundsArea, false, true); //How map tiles are handled is determined by the optional parameters
            }

            if (args.Any(a => a.StartsWith("-createStandalonePoint")))
            {
                //This makes a standalone DB centered on a specific point, it will grab a Cell6's area around that point.
                string[] bounds = args.First(a => a.StartsWith("-createStandalonePoint")).Split('|');

                var resSplit = resolutionCell6 / 2;
                GeoArea boundsArea = new GeoArea(bounds[1].ToDouble() - resSplit, bounds[2].ToDouble() - resSplit, bounds[1].ToDouble() + resSplit, bounds[2].ToDouble() + resSplit);

                //in order, these go south/west/north/east.
                StandaloneCreation.CreateStandaloneDB(0, boundsArea, false, true); //How map tiles are handled is determined by the optional parameters
            }

            if (args.Any(a => a == "-autoCreateMapTiles")) 
            {
                var db = new PraxisContext();
                var bounds = db.SetServerBounds(long.Parse(config["UseOneRelationID"]));
                MapTileSupport.PregenMapTilesForArea(bounds);
            }

            if (args.Any(a => a == "-findServerBounds"))
            {
                var db = new PraxisContext();
                db.SetServerBounds(long.Parse(config["UseOneRelationID"]));
            }

            if (args.Any(a => a.StartsWith("-drawOneImage:")))
            {
                DrawOneImage(args.First(a => a.StartsWith("-drawOneImage:")).Split(":")[1]);
            }

            if (args.Any(a => a.StartsWith("-processCoastlines:")))
            {
                string filename = args.First(a => a.StartsWith("-processCoastlines:")).Split(":")[1];
                ReadCoastlineShapefile(filename);
            }

            if (args.Any(a => a == "-makePosterImage"))
            {
                DrawPosterOfServer();
            }

            if (args.Any(a => a == "-pwdSpeedTest"))
            {
                PwdSpeedTest();
            }

            if (args.Any(a => a == "-setEnvValues"))
            {
                SetEnvValues();
            }

            //This is not currently finished or testing in the current setup. Will return in a future release.
            //if (args.Any(a => a.StartsWith("-populateEmptyArea:")))
            //{
            //    populateEmptyAreas(args.First(a => a.StartsWith("-populateEmptyArea:")).Split(":")[1]);
            //}
        }

        private static void SetEnvValues()
        {
            Log.WriteLog("Setting preferred NET 6 environment variables for performance. A restart may be required for them to apply.");
            System.Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1", EnvironmentVariableTarget.Machine);
            System.Environment.SetEnvironmentVariable("COMPlus_TieredCompilation", "1", EnvironmentVariableTarget.Machine);
            System.Environment.SetEnvironmentVariable("DOTNET_TieredPGO", "1", EnvironmentVariableTarget.Machine);
        }

        private static void PwdSpeedTest()
        {
            Log.WriteLog("Determining the correct value for Rounds on this computer for saving passwords...");
            System.Diagnostics.Stopwatch encryptTimer = new System.Diagnostics.Stopwatch();
            int rounds = 6;
            while (encryptTimer.ElapsedMilliseconds < 250)
            {
                rounds++;
                var options = new CrypterOptions() {
                        { CrypterOption.Rounds, rounds}
                    };
                encryptTimer.Restart();
                BlowfishCrypter crypter = new BlowfishCrypter();
                var salt = crypter.GenerateSalt(options);
                var results = crypter.Crypt("anythingWillDo", salt);
                encryptTimer.Stop();
                Log.WriteLog("Time with Rounds:" + rounds + ": " + encryptTimer.ElapsedMilliseconds + "ms");

            }
            Log.WriteLog("Suggestion: Set the PasswordRounds configuration variable to " + rounds + " in PraxisMapper's appsettings.json file");
        }

        private static void createDb()
        {
            Log.WriteLog("Creating database with current database settings.");
            var db = new PraxisContext();
            db.MakePraxisDB();
        }

        private static void processPbfs()
        {
            List<string> filenames = System.IO.Directory.EnumerateFiles(config["PbfFolder"], "*.pbf").ToList();
            foreach (string filename in filenames)
            {
                Log.WriteLog("Loading " + filename + " at " + DateTime.Now);
                PbfReader r = new PbfReader();
                r.outputPath = config["OutputDataFolder"];
                r.styleSet = config["TagParserStyleSet"];
                r.processingMode = config["processingMode"]; // "normal" and "center" allowed
                r.saveToTsv = config["UseTsvOutput"] == "True";
                r.saveToDB = false; //This is slower than doing both steps separately because loading to the DB is single-threaded this way.
                r.onlyMatchedAreas = config["OnlyTaggedAreas"] == "True";
                r.reprocessFile = config["reprocessFiles"] == "True";
                r.ProcessFile(filename, long.Parse(config["UseOneRelationID"]));
                File.Move(filename, filename + "done");
            }
        }

        private static void loadProcessedData()
        {
            Log.WriteLog("Starting load from processed files at " + DateTime.Now);
            System.Diagnostics.Stopwatch fullProcess = new System.Diagnostics.Stopwatch();
            fullProcess.Start();
            PraxisContext db = new PraxisContext();
            db.Database.SetCommandTimeout(Int32.MaxValue);
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            List<string> geomFilenames = Directory.EnumerateFiles(config["OutputDataFolder"], "*.geomData").ToList();
            List<string> tagsFilenames = Directory.EnumerateFiles(config["OutputDataFolder"], "*.tagsData").ToList();

            if (config["KeepElementsInMemory"] == "True") //ignore DB, doing some one-off operation.
            {
                //Skip database work. Use an in-memory list for a temporary operation.
                foreach (var fileName in geomFilenames)
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    Log.WriteLog("Loading " + fileName + " to memory at " + DateTime.Now);
                    var entries = File.ReadAllLines(fileName);
                    foreach (var entry in entries)
                    {
                        DbTables.Place stored = GeometrySupport.ConvertSingleTsvStoredElement(entry);
                        memorySource.Add(stored);
                    }

                    Log.WriteLog("File loaded to memory in " + sw.Elapsed);
                    sw.Stop();
                }
                foreach (var fileName in tagsFilenames)
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    Log.WriteLog("Loading " + fileName + " to memory at " + DateTime.Now);
                    var entries = File.ReadAllLines(fileName);
                    foreach (var entry in entries)
                    {
                        PlaceTags stored = GeometrySupport.ConvertSingleTsvTag(entry);
                        var taggedGeo = memorySource.First(m => m.SourceItemType == stored.SourceItemType && m.SourceItemID == stored.SourceItemId);
                        //MemorySource will need to be a more efficient collection for searching if this is to be a major feature, but this functions.
                        taggedGeo.Tags.Add(stored);
                    }

                    Log.WriteLog("File applied to memory in " + sw.Elapsed);
                    sw.Stop();
                }
                return;
            }
            else if (config["UseMariaDBInFile"] == "True") //Use the LOAD DATA INFILE command to skip the EF for loading.
            {
                foreach (var fileName in geomFilenames)
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    sw.Start();
                    var mariaPath = fileName.Replace("\\", "\\\\");
                    db.Database.ExecuteSqlRaw("LOAD DATA INFILE '" + mariaPath + "' IGNORE INTO TABLE StoredOsmElements fields terminated by '\t' lines terminated by '\r\n' (sourceItemID, sourceItemType, @elementGeometry, AreaSize, privacyId) SET elementGeometry = ST_GeomFromText(@elementGeometry) ");
                    sw.Stop();
                    Log.WriteLog("Geometry loaded from " + fileName + " in " + sw.Elapsed);
                    File.Move(fileName, fileName + "done");
                }

                foreach (var fileName in tagsFilenames)
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    sw.Start();
                    var mariaPath = fileName.Replace("\\", "\\\\");
                    db.Database.ExecuteSqlRaw("LOAD DATA INFILE '" + mariaPath + "' IGNORE INTO TABLE ElementTags fields terminated by '\t' lines terminated by '\r\n' (SourceItemId, SourceItemType, `key`, `value`)");
                    sw.Stop();
                    Log.WriteLog("Tags loaded from " + fileName + " in " + sw.Elapsed);
                    File.Move(fileName, fileName + "done");
                }
            }
            else //Main path.
            {
                Parallel.ForEach(geomFilenames, fileName => 
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    sw.Start();
                    var db = new PraxisContext();
                    db.Database.SetCommandTimeout(Int32.MaxValue);
                    db.ChangeTracker.AutoDetectChangesEnabled = false;
                    var lines = File.ReadAllLines(fileName); //Might be faster to use streams and dodge the memory allocation?
                    foreach (var line in lines)
                    {
                        db.Places.Add(GeometrySupport.ConvertSingleTsvStoredElement(line));
                    }
                    db.SaveChanges();
                    sw.Stop();
                    Log.WriteLog("Geometry loaded from " + fileName + " in " + sw.Elapsed);
                    File.Move(fileName, fileName + "done");
                });
                Parallel.ForEach(tagsFilenames, fileName =>
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    sw.Start();
                    var db = new PraxisContext();
                    db.Database.SetCommandTimeout(Int32.MaxValue);
                    db.ChangeTracker.AutoDetectChangesEnabled = false;
                    var lines = File.ReadAllLines(fileName);
                    foreach (var line in lines)
                    {
                        db.PlaceTags.Add(GeometrySupport.ConvertSingleTsvTag(line));
                    }
                    db.SaveChanges();
                    sw.Stop();
                    Log.WriteLog("Tags loaded from " + fileName + " in " + sw.Elapsed);
                    File.Move(fileName, fileName + "done");
                });
            }

            fullProcess.Stop();
            Log.WriteLog("Files processed in " + fullProcess.Elapsed);
            fullProcess.Restart();
            db.RecreateIndexes();
            fullProcess.Stop();
            Log.WriteLog("Indexes generated in " + fullProcess.Elapsed);
        }

        private static void DrawOneImage(string code)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            TagParser.ApplyTags(memorySource, "mapTiles");
            ImageStats istats = new ImageStats(OpenLocationCode.DecodeValid(code), 1024, 1024);
            var paintOps = MapTileSupport.GetPaintOpsForStoredElements(memorySource, "mapTiles", istats);
            File.WriteAllBytes(config["OutputDataFolder"] + code + ".png", MapTileSupport.DrawPlusCode(code, paintOps, "mapTiles"));
            sw.Stop();
            Log.WriteLog("image drawn from memory in " + sw.Elapsed);
        }

        private static void DrawPosterOfServer()
        {
            var db = new PraxisContext();
            var bounds = db.ServerSettings.First();

            var geoArea = new GeoArea(bounds.SouthBound, bounds.WestBound, bounds.NorthBound, bounds.EastBound);
            //do the math to scale image.
            //the smaller side is set to 24", the larger size scales up proportionally up to a max of 36"
            //if the longer side is > 36", scale both down by the difference?

            //36x24 is target poster size, at 300 dpi, our image size will allow for a half-inch of margin on both axes.
            var dpi = 300;
            var maxXSide = 35 * dpi;
            var maxYSide = 23 * dpi;
            var xSize = 0;
            var ySize = 0;

            var heightScale = geoArea.LatitudeHeight / geoArea.LongitudeWidth; //Y pixels per X pixel
            if (heightScale > 1) // Y axis is longer than X axis
            {
                heightScale = geoArea.LongitudeWidth / geoArea.LatitudeHeight;
                maxXSide = 23 * dpi;
                maxYSide = 35 * dpi;
                ySize = maxYSide;
                xSize = (int)(maxXSide * heightScale);
            }
            else
            {
                xSize = maxXSide;
                ySize = (int)(maxYSide * heightScale);
            }

            Log.WriteLog("Loading all places from DB");
            var places = GetPlaces(geoArea);
            var iStats = new ImageStats(geoArea, xSize, ySize);
            Log.WriteLog("Generating paint operations");
            var paintOps = MapTileSupport.GetPaintOpsForStoredElements(places, "mapTiles", iStats);
            Log.WriteLog("Drawing image");
            var image = MapTiles.DrawAreaAtSize(iStats, paintOps);

            File.WriteAllBytes("ServerPoster.png", image);
            Log.WriteLog("Image saved to disk");
        }

        //Removed for now because I do not currently use it for any of my game modes, but I may still want to use this or something based on this in the future.
        //private static void populateEmptyAreas(string cell6)
        //{
        //    var db = new PraxisContext();
        //    CodeArea box6 = OpenLocationCode.DecodeValid(cell6);
        //    var location6 = Converters.GeoAreaToPolygon(box6);
        //    var places = db.StoredOsmElements.Where(md => md.elementGeometry.Intersects(location6)).ToList(); //TODO: filter this down to only areas with IsGameElement == true
        //    var fakeplaces = places.Where(p => p.IsGenerated).ToList();

        //    for (int x = 0; x < 20; x++)
        //    {
        //        for (int y = 0; y < 20; y++)
        //        {
        //            string cell8 = cell6 + OpenLocationCode.CodeAlphabet[x] + OpenLocationCode.CodeAlphabet[y];
        //            CodeArea box = OpenLocationCode.DecodeValid(cell8);
        //            var location = Converters.GeoAreaToPolygon(box);
        //            if (!places.Any(md => md.elementGeometry.Intersects(location)) && !fakeplaces.Any(md => md.elementGeometry.Intersects(location)))
        //                CreateInterestingPlaces(cell8);
        //        }
        //    }
        //}

        private static void ApplyConfigValues()
        {
            PraxisContext.connectionString = config["DbConnectionString"];
            PraxisContext.serverMode = config["DbMode"];

            if (config["MapTilesEngine"] == "SkiaSharp")
            {
                var asm = Assembly.LoadFrom(@"PraxisMapTilesSkiaSharp.dll");
                MapTiles = (IMapTiles)Activator.CreateInstance(asm.GetType("PraxisCore.MapTiles"));
            }
            else if (config["MapTilesEngine"] == "ImageSharp")
            {
                var asm2 = Assembly.LoadFrom(@"PraxisMapTilesImageSharp.dll");
                MapTiles = (IMapTiles)Activator.CreateInstance(asm2.GetType("PraxisCore.MapTiles"));
            }
            IMapTiles.GameTileScale = config["mapTileScaleFactor"].ToInt();
            IMapTiles.SlippyTileSizeSquare = config["slippyTileSize"].ToInt();
            IMapTiles.BufferSize = config["placesAreaBuffer"].ToDouble();

            if (config["UseHighAccuracy"] != "True")
            {
                factory = NtsGeometryServices.Instance.CreateGeometryFactory(new PrecisionModel(1000000), 4326); //SRID matches 10-character Plus code values.  Precision model means round all points to 7 decimal places to not exceed float's useful range.
                SimplifyAreas = true; //rounds off points that are within a Cell10's distance of each other. Makes fancy architecture and highly detailed paths less pretty on map tiles, but works for gameplay data.
            }

            Log.Verbosity = (Log.VerbosityLevels)config["LogLevel"].ToInt();

            if (config["KeepElementsInMemory"] == "True")
                memorySource = new List<DbTables.Place>(20000);

            if (config["UseMariaDBInFile"] == "True" && config["DbMode"] != "MariaDB")
            {
                Log.WriteLog("You set a MariaDB-only option on and aren't using MariaDB! Fix the configs to use MariaDB or disable the InFile setting and run again.", Log.VerbosityLevels.High);
                return;
            }
        }

        public static void DownloadPbfFile(string topLevel, string subLevel1, string subLevel2, string destinationFolder)
        {
            //pull a fresh copy of a file from geofabrik.de (or other mirror potentially)
            //save it to the same folder as configured for pbf files (might be passed in)
            //web paths http://download.geofabrik.de/north-america/us/ohio-latest.osm.pbf
            //root, then each parent division. Starting with USA isn't too hard.
            //TODO: set this up to get files with different sub-level counts.
            var wc = new WebClient();
            wc.DownloadFile("http://download.geofabrik.de/" + topLevel + "/" + subLevel1 + "/" + subLevel2 + "-latest.osm.pbf", destinationFolder + subLevel2 + "-latest.osm.pbf");
        }

        public static void ReadCoastlineShapefile(string shapePath)
        {
            string fileBaseName = config["OutputDataFolder"] + "coastlines";
            EGIS.ShapeFileLib.ShapeFile sf = new EGIS.ShapeFileLib.ShapeFile(shapePath);
            var recordCount = sf.RecordCount;
            StringBuilder geometryBuilds = new StringBuilder();
            StringBuilder tagBuilds = new StringBuilder();
            for (int i = 0; i < recordCount; i++)
            {
                var shapeData = sf.GetShapeDataD(i);
                var poly = Converters.ShapefileRecordToPolygon(shapeData);
                geometryBuilds.Append(100000000000 + i).Append("\t").Append("2").Append("\t").Append(poly.AsText()).Append("\t").Append(poly.Area).Append("\t").Append(Guid.NewGuid()).Append("\r\n");
                tagBuilds.Append(100000000000 + i).Append("\t").Append("2").Append("\t").Append("natural").Append("\t").Append("coastline").Append("\r\n"); 
            }
            File.WriteAllText(fileBaseName + ".geomData", geometryBuilds.ToString());
            File.WriteAllText(fileBaseName + ".tagData", tagBuilds.ToString());
        }

        public static void UpdateExistingEntries(string path)
        {
            List<string> filenames = Directory.EnumerateFiles(path, "*.geomData").ToList();
            ParallelOptions po = new ParallelOptions();
            Parallel.ForEach(filenames, po, (filename) =>
            {
                try
                {
                    var db = new PraxisContext();
                    Log.WriteLog("Loading " + filename);
                    var entries = GeometrySupport.ReadStoredElementsFileToMemory(filename); //tagsData file loaded automatically here.
                    Log.WriteLog(entries.Count() + " entries to update in database for " + filename);
                    db.UpdateExistingEntries(entries);
                    File.Move(filename, filename + "Done");
                    Log.WriteLog(filename + " completed at " + DateTime.Now);
                }
                catch (Exception ex)
                {
                    Log.WriteLog("Error multithreading: " + ex.Message + ex.StackTrace, Log.VerbosityLevels.Errors);
                }
            });
        }

        public static void ResetFiles(string folder)
        {
            List<string> filenames = System.IO.Directory.EnumerateFiles(folder, "*.*Done").ToList();
            foreach (var file in filenames)
            {
                File.Move(file, file.Substring(0, file.Length - 4));
            }
        }
    }
}
