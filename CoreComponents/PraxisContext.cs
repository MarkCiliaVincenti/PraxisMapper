﻿using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using static PraxisCore.DbTables;

namespace PraxisCore
{
    /// <summary>
    /// A self-contained database connector for everything PraxisMapper can do in its database.
    /// </summary>
    public class PraxisContext : DbContext
    {
        public DbSet<PlayerData> PlayerData { get; set; }
        public DbSet<PerformanceInfo> PerformanceInfo { get; set; }
        public DbSet<MapTile> MapTiles { get; set; }
        public DbSet<SlippyMapTile> SlippyMapTiles { get; set; }
        public DbSet<ErrorLog> ErrorLogs { get; set; }
        public DbSet<ServerSetting> ServerSettings { get; set; }
        public DbSet<DbTables.Place> Places { get; set; }
        public DbSet<StyleEntry> StyleEntries { get; set; }
        public DbSet<StyleMatchRule> StyleMatchRules { get; set; }
        public DbSet<StylePaint> StylePaints { get; set; }
        public DbSet<PlaceTags> PlaceTags { get; set; } 
        public DbSet<PlaceGameData> PlaceGameData { get; set; }
        public DbSet<AreaGameData> AreaGameData { get; set; }
        public DbSet<GlobalDataEntries> GlobalDataEntries { get; set; }
        public DbSet<StyleBitmap> StyleBitmaps { get; set; }
        public DbSet<AntiCheatEntry> AntiCheatEntries { get; set; }
        public DbSet<AuthenticationData> AuthenticationData { get; set; }

        public static string connectionString = "Data Source=localhost\\SQLDEV;UID=PraxisService;PWD=lamepassword;Initial Catalog=Praxis;"; //Needs a default value.
        public static string serverMode = "SQLServer";

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (serverMode == "SQLServer")
                optionsBuilder.UseSqlServer(connectionString, x => x.UseNetTopologySuite());
            else if (serverMode == "MariaDB")
            {
                optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), x => x.UseNetTopologySuite().EnableRetryOnFailure());
            }
            else if (serverMode == "PostgreSQL") //A lot of mapping stuff defaults to PostgreSQL, so I should evaluate it here. It does seem to take some specific setup steps, versus MariaDB
            {
                optionsBuilder.UseNpgsql("Host=localhost;Database=praxis;Username=postgres;Password=asdf", o => o.UseNetTopologySuite());
            }

            //optionsBuilder.UseMemoryCache(mc);//I think this improves performance at the cost of RAM usage. Needs additional testing.
        }

            protected override void OnModelCreating(ModelBuilder model)
            {
            //Create indexes here.
            model.Entity<PlayerData>().HasIndex(p => p.DeviceID);
            model.Entity<PlayerData>().HasIndex(p => p.DataKey);
            model.Entity<PlayerData>().HasIndex(p => p.Expiration);

            model.Entity<DbTables.Place>().HasIndex(m => m.DrawSizeHint); //Enables server-side sorting on biggest-to-smallest draw order.
            model.Entity<DbTables.Place>().HasIndex(m => m.SourceItemID);
            model.Entity<DbTables.Place>().HasIndex(m => m.SourceItemType);
            model.Entity<DbTables.Place>().HasIndex(m => m.PrivacyId);
            model.Entity<DbTables.Place>().HasMany(m => m.Tags).WithOne(m => m.Place).HasForeignKey(m => (new { m.SourceItemId, m.SourceItemType })).HasPrincipalKey(m => (new { m.SourceItemID, m.SourceItemType }));

            model.Entity<MapTile>().HasIndex(m => m.PlusCode);
            model.Entity<MapTile>().Property(m => m.PlusCode).HasMaxLength(12);
            model.Entity<MapTile>().HasIndex(m => m.StyleSet);

            model.Entity<SlippyMapTile>().HasIndex(m => m.Values);
            model.Entity<SlippyMapTile>().HasIndex(m => m.StyleSet);

            model.Entity<PlaceTags>().HasIndex(m => m.Key);
            model.Entity<PlaceTags>().HasOne(m => m.Place).WithMany(m => m.Tags).HasForeignKey(m => new { m.SourceItemId, m.SourceItemType }).HasPrincipalKey(m => new { m.SourceItemID, m.SourceItemType });


            model.Entity<PlaceGameData>().HasIndex(m => m.DataKey);
            model.Entity<PlaceGameData>().HasIndex(m => m.PlaceId);
            model.Entity<PlaceGameData>().HasIndex(m => m.Expiration);

            model.Entity<AreaGameData>().HasIndex(m => m.DataKey);
            model.Entity<AreaGameData>().HasIndex(m => m.PlusCode);
            model.Entity<AreaGameData>().HasIndex(m => m.GeoAreaIndex);
            model.Entity<AreaGameData>().HasIndex(m => m.Expiration);

            model.Entity<AntiCheatEntry>().HasIndex(m => m.filename);

            model.Entity<AuthenticationData>().HasIndex(m => m.accountId);

            if (serverMode == "PostgreSQL")
            {
                model.HasPostgresExtension("postgis");
            }
        }

        //A trigger to ensure all data inserted is valid by MSSQL Server rules.
        public static string MapDataValidTriggerMSSQL = "CREATE TRIGGER dbo.MakeValid ON dbo.MapData AFTER INSERT AS BEGIN UPDATE dbo.MapData SET place = place.MakeValid() WHERE MapDataId in (SELECT MapDataId from inserted) END";
        public static string GeneratedMapDataValidTriggerMSSQL = "CREATE TRIGGER dbo.GenereatedMapDataMakeValid ON dbo.GeneratedMapData AFTER INSERT AS BEGIN UPDATE dbo.GeneratedMapData SET place = place.MakeValid() WHERE GeneratedMapDataId in (SELECT GeneratedMapDataId from inserted) END";

        //An index that I don't think EFCore can create correctly automatically.
        public static string MapTileIndex = "CREATE SPATIAL INDEX MapTileSpatialIndex ON MapTiles(areaCovered)";
        public static string SlippyMapTileIndex = "CREATE SPATIAL INDEX SlippyMapTileSpatialIndex ON SlippyMapTiles(areaCovered)";
        public static string StoredElementsIndex = "CREATE SPATIAL INDEX PlacesIndex ON Places(elementGeometry)";
        public static string customDataPlusCodesIndex = "CREATE SPATIAL INDEX areaGameDataSpatialIndex ON AreaGameData(geoAreaIndex)";
        public static string drawSizeHintIndex = "CREATE OR REPLACE INDEX IX_Places_DrawSizeHint on Places (DrawSizeHint)"; 
        public static string privacyIdIndex = "CREATE OR REPLACE INDEX IX_Places_privacyId on Places (privacyId)";
        public static string sourceItemIdIndex = "CREATE OR REPLACE INDEX IX_Places_sourceItemID on Places (sourceItemID)";
        public static string sourceItemTypeIndex = "CREATE OR REPLACE INDEX IX_Places_sourceItemType on Places (sourceItemType)";
        public static string tagKeyIndex = "CREATE OR REPLACE INDEX IX_PlaceTags_Key on PlaceTags (`Key`)";

        //PostgreSQL uses its own CREATE INDEX syntax
        public static string MapTileIndexPG = "CREATE INDEX maptiles_geom_idx ON public.\"MapTiles\" USING GIST(\"areaCovered\")";
        public static string SlippyMapTileIndexPG = "CREATE INDEX slippmayptiles_geom_idx ON public.\"SlippyMapTiles\" USING GIST(\"areaCovered\")";
        public static string StoredElementsIndexPG = "CREATE INDEX place_geom_idx ON public.\"Places\" USING GIST(\"elementGeometry\")";

        //Adding these as helper values for large use cases. When inserting large amounts of data, it's probably worth removing indexes for the insert and re-adding them later.
        //(On a North-America file insert, this keeps insert speeds at about 2-3 seconds per block, whereas it creeps up consistently while indexes are updated per block.
        //Though, I also see better results there droping the single-column indexes as well, which would need re-created manually since those one are automatic.
        public static string DropMapTileIndex = "DROP INDEX IF EXISTS MapTileSpatialIndex on MapTiles";
        public static string DropSlippyMapTileIndex = "DROP INDEX IF EXISTS SlippyMapTileSpatialIndex on SlippyMapTiles";
        public static string DropStoredElementsIndex = "DROP INDEX IF EXISTS PlacesIndex on Places";
        public static string DropcustomDataPlusCodesIndex = "DROP INDEX IF EXISTS areaGameDataSpatialIndex on AreaGameData";
        public static string DropStoredElementsHintSizeIndex = "DROP INDEX IF EXISTS IX_Places_DrawSizeHint on Places";
        public static string DropStoredElementsPrivacyIdIndex = "DROP INDEX IF EXISTS IX_Places_privacyId on Places";
        public static string DropStoredElementsSourceItemIdIndex = "DROP INDEX IF EXISTS IX_Places_sourceItemID on Places";
        public static string DropStoredElementsSourceItemTypeIndex = "DROP INDEX IF EXISTS IX_Places_sourceItemType on Places";
        public static string DropTagKeyIndex = "DROP INDEX IF EXISTS IX_PlaceTags_Key on PlaceTags";

        //This doesn't appear to be any faster. The query isn't the slow part. Keeping this code as a reference for how to precompile queries.
        //public static Func<PraxisContext, Geometry, IEnumerable<MapData>> compiledIntersectQuery =
        //  EF.CompileQuery((PraxisContext context, Geometry place) => context.MapData.Where(md => md.place.Intersects(place)));

        public void MakePraxisDB()
        {
            if (!Database.EnsureCreated()) //all the automatic stuff EF does for us
                return;

            //Not automatic entries executed below:
            //PostgreSQL will make automatic spatial indexes
            if (serverMode == "PostgreSQL")
            {
                //db.Database.ExecuteSqlRaw(GeneratedMapDataIndexPG);
                Database.ExecuteSqlRaw(MapTileIndexPG);
                Database.ExecuteSqlRaw(SlippyMapTileIndexPG);
                Database.ExecuteSqlRaw(StoredElementsIndexPG);
            }
            else //SQL Server and MariaDB share the same syntax here
            {
                //db.Database.ExecuteSqlRaw(GeneratedMapDataIndex);
                Database.ExecuteSqlRaw(MapTileIndex);
                Database.ExecuteSqlRaw(SlippyMapTileIndex);
                Database.ExecuteSqlRaw(StoredElementsIndex);
            }

            if (serverMode == "SQLServer")
            {
                Database.ExecuteSqlRaw(GeneratedMapDataValidTriggerMSSQL);
            }
            if (serverMode == "MariaDB")
            {
                Database.ExecuteSqlRaw("SET collation_server = 'utf8mb4_unicode_ci'; SET character_set_server = 'utf8mb4'"); //MariaDB defaults to latin2_swedish, we need Unicode.
            }

            InsertDefaultServerConfig();
            InsertDefaultStyle();
        }

        public void InsertDefaultServerConfig()
        {
            //var db = new PraxisContext();
            ServerSettings.Add(new ServerSetting() { Id = 1, NorthBound = 90, SouthBound = -90, EastBound = 180, WestBound = -180 });
            SaveChanges();
        }

        public void InsertDefaultStyle()
        {
            //var db = new PraxisContext();
            //Remove any existing entries, in case I'm refreshing the rules on an existing entry.
            if (serverMode != "PostgreSQL") //PostgreSQL has stricter requirements on its syntax.
            {
                //db.Database.ExecuteSqlRaw("DELETE FROM TagParserEntriesTagParserMatchRules");
                //db.Database.ExecuteSqlRaw("DELETE FROM TagParserEntries");
                //db.Database.ExecuteSqlRaw("DELETE FROM TagParserMatchRules");
            }

            if (serverMode == "SQLServer")
            {
                Database.BeginTransaction();
                Database.ExecuteSqlRaw("SET IDENTITY_INSERT StyleEntries ON;");
            }
            StyleEntries.AddRange(Singletons.defaultStyleEntries);
            SaveChanges();
            if (serverMode == "SQLServer")
            {
                Database.ExecuteSqlRaw("SET IDENTITY_INSERT StyleEntries OFF;");
                Database.CommitTransaction();
            }

            foreach (var file in System.IO.Directory.EnumerateFiles("MapPatterns"))
            {
                StyleBitmaps.Add(new StyleBitmap()
                {
                    Filename = System.IO.Path.GetFileName(file),
                    Data = System.IO.File.ReadAllBytes(file)
                });
            }
            SaveChanges();
        }

        public void RecreateIndexes()
        {
            //Only run this after running DropIndexes, since these should all exist on DB creation.
            Database.SetCommandTimeout(60000);

            //PostgreSQL will make automatic spatial indexes
            if (serverMode == "PostgreSQL")
            {
                //db.Database.ExecuteSqlRaw(GeneratedMapDataIndexPG);
                Database.ExecuteSqlRaw(MapTileIndexPG);
                Database.ExecuteSqlRaw(SlippyMapTileIndexPG);
                Database.ExecuteSqlRaw(StoredElementsIndexPG);
            }
            else //SQL Server and MariaDB share the same syntax here
            {
                //db.Database.ExecuteSqlRaw(GeneratedMapDataIndex);
                Database.ExecuteSqlRaw(MapTileIndex);
                Database.ExecuteSqlRaw(SlippyMapTileIndex);
                Database.ExecuteSqlRaw(StoredElementsIndex);

                //now also add the automatic ones we took out in DropIndexes.
                Database.ExecuteSqlRaw(drawSizeHintIndex);
                Database.ExecuteSqlRaw(sourceItemIdIndex);
                Database.ExecuteSqlRaw(sourceItemTypeIndex);
                Database.ExecuteSqlRaw(tagKeyIndex);
                Database.ExecuteSqlRaw(privacyIdIndex);
                Database.ExecuteSqlRaw(customDataPlusCodesIndex);
            }
        }

        public void DropIndexes()
        {
            Database.ExecuteSqlRaw(DropMapTileIndex);
            Database.ExecuteSqlRaw(DropStoredElementsIndex);
            Database.ExecuteSqlRaw(DropSlippyMapTileIndex);
            Database.ExecuteSqlRaw(DropcustomDataPlusCodesIndex);
            Database.ExecuteSqlRaw(DropStoredElementsHintSizeIndex);
            Database.ExecuteSqlRaw(DropStoredElementsPrivacyIdIndex);
            Database.ExecuteSqlRaw(DropStoredElementsSourceItemTypeIndex);
            Database.ExecuteSqlRaw(DropStoredElementsSourceItemIdIndex);
            Database.ExecuteSqlRaw(DropTagKeyIndex);
        }

        /// <summary>
        /// Force gameplay maptiles to expire and be redrawn on next access. Can be limited to a specific style set or run on all tiles.
        /// </summary>
        /// <param name="g">the area to expire intersecting maptiles with</param>
        /// <param name="styleSet">which set of maptiles to expire. All tiles if this is an empty string</param>
        public int ExpireMapTiles(Geometry g, string styleSet = "")
        {
            string SQL = "UPDATE MapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet= '" + styleSet + "' OR '" + styleSet + "' = '') AND ST_INTERSECTS(areaCovered, ST_GEOMFROMTEXT('" + g.AsText() + "'))";
            return Database.ExecuteSqlRaw(SQL);
        }

        public int ExpireAllMapTiles()
        {
            string SQL = "UPDATE MapTiles SET ExpireOn = CURRENT_TIMESTAMP";
            return Database.ExecuteSqlRaw(SQL);
        }

        /// <summary>
        /// Force gameplay maptiles to expire and be redrawn on next access. Can be limited to a specific style set or run on all tiles.
        /// </summary>
        /// <param name="elementId">the privacyID of a Place to expire intersecting tiles for.</param>
        /// <param name="styleSet">which set of maptiles to expire. All tiles if this is an empty string</param>
        public int ExpireMapTiles(Guid elementId, string styleSet = "")
        {
            string SQL = "UPDATE MapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet= '" + styleSet + "' OR '" + styleSet + "' = '') AND ST_INTERSECTS(areaCovered, (SELECT elementGeometry FROM Places WHERE privacyId = '" + elementId + "'))";
            return Database.ExecuteSqlRaw(SQL);
        }

        /// <summary>
        /// Force SlippyMap tiles to expire and be redrawn on next access. Can be limited to a specific style set or run on all tiles.
        /// </summary>
        /// <param name="g">the area to expire intersecting maptiles with</param>
        /// <param name="styleSet">which set of SlippyMap tiles to expire. All tiles if this is an empty string</param>
        public void ExpireSlippyMapTiles(Geometry g, string styleSet = "")
        {
            string SQL = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet= '" + styleSet + "' OR '" + styleSet + "' = '') AND ST_INTERSECTS(areaCovered, ST_GEOMFROMTEXT('" + g.AsText() + "'))";
            Database.ExecuteSqlRaw(SQL);
        }

        /// <summary>
        /// Force SlippyMap tiles to expire and be redrawn on next access. Can be limited to a specific style set or run on all tiles.
        /// </summary>
        /// <param name="elementId">the privacyID of a Place to expire intersecting tiles for.</param>
        /// <param name="styleSet">which set of maptiles to expire. All tiles if this is an empty string</param>
        public void ExpireSlippyMapTiles(Guid elementId, string styleSet = "")
        {
            //Might this be better off as raw SQL? If I expire, say, an entire state, that could be a lot of map tiles to pull into RAM just for a date to change.
            //var raw = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE ST_INTERSECTS(areaCovered, ST_GeomFromText(" + g.AsText() + "))";
            //var db = new PraxisContext();
            string SQL = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet = '" + styleSet + "' OR '" + styleSet + "' = '') AND ST_INTERSECTS(areaCovered, (SELECT elementGeometry FROM Places WHERE privacyId = '" + elementId + "'))";
            Database.ExecuteSqlRaw(SQL);
        }
        public void ExpireAllSlippyMapTiles()
        {
            string SQL = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP";
            Database.ExecuteSqlRaw(SQL);
        }

        public GeoArea SetServerBounds(long singleArea)
        {
            //This is an important command if you don't want to track data outside of your initial area.
            GeoArea results = null;
            if (singleArea != 0)
            {
                var area = Places.First(e => e.SourceItemID == singleArea);
                var envelop = area.ElementGeometry.EnvelopeInternal;
                results = new GeoArea(envelop.MinY, envelop.MinX, envelop.MaxY, envelop.MaxX);
            }
            else
                results = Place.DetectServerBounds(ConstantValues.resolutionCell8);

            var settings = ServerSettings.FirstOrDefault();
            if (settings == null) //Shouldn't happen, but an error in the right spot and re-running the process can cause this.
            {
                settings = new ServerSetting();
                ServerSettings.Add(settings);
            }
            settings.NorthBound = results.NorthLatitude;
            settings.SouthBound = results.SouthLatitude;
            settings.EastBound = results.EastLongitude;
            settings.WestBound = results.WestLongitude;
            SaveChanges();
            return results;
        }

        public int UpdateExistingEntries(List<DbTables.Place> entries)
        {
            //NOTE: this is not the fastest way to handle this process, but this setup does allow the DB to be updated while the game is running.
            //It would be faster to update all objects, then expire all map tiles instead of expiring map tiles on every entry, but that requires a server shutdown.
            //Do 1 DB load to start, then process the whole block.
            int updateCount = 0;
            
            var firstPlaceId = entries.Min(e => e.SourceItemID); //these should be ordered, can just use First and Last instead of min/max.
            var lastPlaceId = entries.Max(e => e.SourceItemID);
            var itemType = entries.First().SourceItemType;
            var placesThisBlock = Places.Include(p => p.Tags).Where(p => p.SourceItemType == itemType && (p.SourceItemID <= lastPlaceId && p.SourceItemID >= firstPlaceId)).ToList();

            var placeIds = entries.Select(e => e.SourceItemID).ToList();
            var placesToRemove = placesThisBlock.Where(p => !placeIds.Contains(p.SourceItemID)).ToList();

            if (placesToRemove.Count > 0)
            {
                Log.WriteLog("Removing " + placesToRemove.Count + " entries");
                foreach (var ptr in placesToRemove)
                {
                    Places.Remove(ptr);
                    ExpireMapTiles(ptr.ElementGeometry);
                    ExpireSlippyMapTiles(ptr.ElementGeometry);
                }
                SaveChanges();
                Log.WriteLog("Entries Removed");
            }
            //Appears to take 3-4 minutes per Way block? Might be faster to just make a new DB at that rate and expire all map tiles, but this one works live no problem.
            Log.WriteLog("Places loaded, checking entries");
            foreach (var entry in entries)
            {
                try {
                    //Log.WriteLog("Entry " + entry.SourceItemID);
                    //check existing entry, see if it requires being updated
                    var existingData = placesThisBlock.FirstOrDefault(md => md.SourceItemID == entry.SourceItemID);
                    if (existingData != null)
                    {
                        //These are redundant. If geometry changes, areaSize will too. If name changes, tags will too.
                        //if (existingData.AreaSize != entry.AreaSize) existingData.AreaSize = entry.AreaSize;
                        //if (existingData.GameElementName != entry.GameElementName) existingData.GameElementName = entry.GameElementName;

                        bool expireTiles = false;
                        //NOTE: sometimes EqualsTopologically fails. But anything that's an invalid geometry should not have been written to the file in the first place.
                        if (!existingData.ElementGeometry.EqualsTopologically(entry.ElementGeometry)) //TODO: this might need to be EqualsExact?
                        {
                            //update the geometry for this object.
                            existingData.ElementGeometry = entry.ElementGeometry;
                            existingData.DrawSizeHint = entry.DrawSizeHint;
                            expireTiles = true;
                        }
                        //This doesn't work. SequenceEquals returns false on identical sets of tags and values.
                        //if (!existingData.Tags.OrderBy(t => t.Key).SequenceEqual(entry.Tags.OrderBy(t => t.Key)))
                        if (!(existingData.Tags.Count == entry.Tags.Count && existingData.Tags.All(t => entry.Tags.Any(tt => tt.Equals(t)))))
                        {
                            existingData.GameElementName = entry.GameElementName;
                            existingData.Tags = entry.Tags;
                            var styleA = TagParser.GetStyleForOsmWay(existingData.Tags);
                            var styleB = TagParser.GetStyleForOsmWay(entry.Tags);
                            if (styleA != styleB)
                                expireTiles = true; //don't force a redraw on tags unless we change our drawing rules.
                        }

                        if (expireTiles) //geometry or style has to change, otherwise we can skip expiring values.
                        {
                            //Log.WriteLog("Saving Updated and expiring tiles");
                            updateCount += SaveChanges(); //save before expiring, so the next redraw absolutely has the latest data. Can't catch it mid-command if we do this first.
                            ExpireMapTiles(entry.ElementGeometry, "mapTiles");
                            ExpireSlippyMapTiles(entry.ElementGeometry, "mapTiles");
                        }
                        else
                        {
                            //Log.WriteLog("No detected changes");
                        }
                    }
                    else
                    {
                        //We don't have this item, add it.
                        //Log.WriteLog("Saving New " + entry.SourceItemID);
                        Places.Add(entry);
                        updateCount += SaveChanges(); //again, necessary here to get tiles to draw correctly after expiring.
                        ExpireMapTiles(entry.ElementGeometry, "mapTiles");
                        ExpireSlippyMapTiles(entry.ElementGeometry, "mapTiles");
                    }
                }
                catch(Exception ex)
                {
                    Log.WriteLog("Error on  " + entry.SourceItemID + ": " + ex.Message + " | " + ex.StackTrace);
                }
            }
            //Log.WriteLog("Final saving");
            updateCount += SaveChanges(); //final one for anything not yet persisted.
            return updateCount;
        }

        public int UpdateExistingEntriesFast(List<DbTables.Place> entries)
        {
            //This version assumes the game is down, and should be somewhat faster by not needing to do as much work to ensure live play goes uninterrupted.
            int updateCount = 0;

            var firstPlaceId = entries.Min(e => e.SourceItemID); //these should be ordered, can just use First and Last instead of min/max.
            var lastPlaceId = entries.Max(e => e.SourceItemID);

            var itemType = entries.First().SourceItemType;
            var placesThisBlock = Places.Include(p => p.Tags).Where(p => p.SourceItemType == itemType && (p.SourceItemID <= lastPlaceId && p.SourceItemID >= firstPlaceId)).ToList();

            var placeIds = entries.Select(e => e.SourceItemID).ToList();
            var placesToRemove = placesThisBlock.Where(p => !placeIds.Contains(p.SourceItemID)).ToList();

            if (placesToRemove.Count > 0)
            {
                Log.WriteLog("Removing " + placesToRemove.Count + " entries");
                foreach (var ptr in placesToRemove)
                {
                    Places.Remove(ptr);
                }
                Log.WriteLog("Entries Removed");
            }

            foreach (var entry in entries)
            {
                try
                {
                    var existingData = placesThisBlock.FirstOrDefault(md => md.SourceItemID == entry.SourceItemID);
                    if (existingData != null)
                    {
                        //NOTE: sometimes EqualsTopologically fails. But anything that's an invalid geometry should not have been written to the file in the first place.
                        if (!existingData.ElementGeometry.EqualsTopologically(entry.ElementGeometry)) //TODO: this might need to be EqualsExact?
                        {
                            //update the geometry for this object.
                            existingData.ElementGeometry = entry.ElementGeometry;
                            existingData.DrawSizeHint = entry.DrawSizeHint;
                        }
                        if (!(existingData.Tags.Count == entry.Tags.Count && existingData.Tags.All(t => entry.Tags.Any(tt => tt.Equals(t)))))
                        {
                            existingData.GameElementName = entry.GameElementName;
                            existingData.Tags = entry.Tags;
                        }
                    }
                    else
                    {
                        Places.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLog("Error on  " + entry.SourceItemID + ": " + ex.Message + " | " + ex.StackTrace);
                }
            }
            //Log.WriteLog("Final saving");
            updateCount += SaveChanges(); //final one for anything not yet persisted.
            return updateCount;
        }

        public void ResetStyles()
        {
            Log.WriteLog("Replacing current styles with default ones");
            var styles = Singletons.defaultStyleEntries.Select(t => t.StyleSet).Distinct().ToList();

            var toRemove = StyleEntries.Include(t => t.PaintOperations).Include(t => t.StyleMatchRules).Where(t => styles.Contains(t.StyleSet)).ToList();
            var toRemovePaints = toRemove.SelectMany(t => t.PaintOperations).ToList();
            var toRemoveImages = StyleBitmaps.ToList();
            var toRemoveRules = toRemove.SelectMany(t => t.StyleMatchRules).ToList();
            StylePaints.RemoveRange(toRemovePaints);
            SaveChanges();
            StyleMatchRules.RemoveRange(toRemoveRules);
            SaveChanges();
            StyleEntries.RemoveRange(toRemove);
            SaveChanges();
            StyleBitmaps.RemoveRange(toRemoveImages);
            SaveChanges();

            InsertDefaultStyle();
            Log.WriteLog("Styles restored to PraxisMapper defaults");
        }
    }
}