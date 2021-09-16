﻿//using PraxisCore;
//using PraxisCore.Support;
//using Google.OpenLocationCode;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.Caching.Memory;
//using PraxisMapper.Classes;
//using SkiaSharp;
//using static PraxisCore.ConstantValues;
//using static PraxisCore.DbTables;
//using static PraxisCore.Place;
//using static PraxisCore.ScoreData;

//namespace PraxisMapper.Controllers
//{
//    [Route("[controller]")]
//    [Route("Gameplay")] //backward compatibility with deployed version of apps.
//    [ApiController]
//    public class AreaControlController : Controller
//    {
//        //AreaControl is meant to be a more typical baseline for a game. You have a core action (walk places to get point), 
//        //which feeds a second action (spend points to claim an area for your team), which feeds a third (have more points than the other team).
//        //It's a very simple framework, but it can be built upon.

//        private static MemoryCache cache;
//        private readonly IConfiguration Configuration;
//        private readonly static SKColor bgColor = TagParser.GetStyleBgColor("teamColor");
//        public AreaControlController(IConfiguration configuration)
//        {
//            Configuration = configuration;

//            if (cache == null && Configuration.GetValue<bool>("enableCaching") == true)
//            {
//                var options = new MemoryCacheOptions();
//                options.SizeLimit = 1024; //1k entries. that's 2.5 4-digit plus code blocks without roads/buildings. If an entry is 300kb on average, this is 300MB of RAM for caching. T3.small has 2GB total, t3.medium has 4GB.
//                cache = new MemoryCache(options);
//            }
//        }

//        //AreaControlController handles the basic gameplay interactions included with the server
//        //this is AreaControl commands for players to take an area, get resources through an action, etc.
//        //Note: to stick to our policy of not storing player data on the server side, Area-Control records stored here must be by faction, not individual.
//        //(individual area control gameplay can store that info on the device)

//        [HttpGet]
//        [Route("/[controller]/test")]
//        [Route("/Gameplay/test")]
//        public string controllerterst()
//        {
//            return "Gameplay Endpoint up";
//        }

//        [HttpGet] //TODO technically this is a post.
//        [Route("/[controller]/claimArea/{storedOsmElementId}/{factionId}")]
//        [Route("/Gameplay/claimArea/{storedOsmElementId}/{factionId}")]
//        public bool ClaimArea(long storedOsmElementId, long factionId)
//        {
//            PerformanceTracker pt = new PerformanceTracker("ClaimArea");
//            PraxisContext db = new PraxisContext();
//            //Tuple<long, int> shortcut = new Tuple<long, int>(MapDataId, factionId); //tell the application later not to hit the DB on every tile for this entry.
//            var element = db.StoredOsmElements.Where(s => s.id == storedOsmElementId).First();
//            ///StoredOmeElementId is the primary key on the table, not the OSM ID
//            GenericData.SetStoredElementData(storedOsmElementId, "teamColor", factionId.ToString());
//            var score = GetScoreForSinglePlace(element.elementGeometry); 
//            if (score < 1)
//                score = 1;
//            GenericData.SetStoredElementData(storedOsmElementId, "points", ((int)score).ToString());
//            MapTiles.ExpireMapTiles(storedOsmElementId, "teamColor");
//            MapTiles.ExpireSlippyMapTiles(storedOsmElementId, "teamColor"); //These 2 previous had both element.elementGeometry and the elementID, should only need 1.
//            pt.Stop();
//            return true;
//        }

//        [HttpGet]
//        [Route("/[controller]/GetFactions")]
//        [Route("/Gameplay/GetFactions")]
//        public string GetFactionInfo()
//        {
//            //TODO: move this to a general controller, since factions apply to multiple game modes?
//            PraxisContext db = new PraxisContext();
//            var factions = db.Factions.ToList();
//            string results = "";
//            foreach (var f in factions)
//                results += f.FactionId + "|" + f.Name + Environment.NewLine;

//            return results;
//        }

//        //This code technically works on any Cell size, I haven't yet functionalized it correctly yet.
//        [HttpGet]
//        [Route("/[controller]/DrawFactionModeCell8HighRes/{Cell8}/")]
//        [Route("/[controller]/DrawFactionModeCell8/{Cell8}")]
//        public FileContentResult DrawFactionModeMergedCell8(string Cell8, string styleSet = "mapTiles")
//        {
//            //TODO: I think it's better to have the background tiles be shared across modes, and to
//            //regenerate the smaller, overlay tiles more often. This means that I need one endpoint
//            //for the background normal map tiles and one for the overlay, and the app needs to draw both in order.
//            PerformanceTracker pt = new PerformanceTracker("DrawFactionModeCell8");
//            //We will try to minimize rework done.
//            var db = new PraxisContext();
//            var baseMapTile = db.MapTiles.Where(mt => mt.PlusCode == Cell8 && mt.resolutionScale == 11 && mt.styleSet == styleSet).FirstOrDefault();
//            System.Collections.Generic.List<StoredOsmElement> places = null;
//            GeoArea pluscode = OpenLocationCode.DecodeValid(Cell8);
//            if (baseMapTile == null || baseMapTile.ExpireOn < DateTime.Now) //Expiration is how we know we have to redraw this tile.
//            {
//                //Create this map tile.
//                //places = GetPlaces(pluscode); //, includeGenerated: Configuration.GetValue<bool>("generateAreas") //TODO restore generated area logic.
//                var tile = MapTiles.DrawPlusCode(Cell8, "mapTiles", true);
//                baseMapTile = new MapTile() { CreatedOn = DateTime.Now, styleSet = styleSet, PlusCode = Cell8, resolutionScale = 11, tileData = tile, areaCovered = Converters.GeoAreaToPolygon(pluscode) };
//                db.MapTiles.Add(baseMapTile);
//                db.SaveChanges();
//            }

//            var factionColorTile = db.MapTiles.Where(mt => mt.PlusCode == Cell8 && mt.resolutionScale == 11 && mt.styleSet == "teamColor").FirstOrDefault();
//            if (factionColorTile == null || factionColorTile.MapTileId == null || factionColorTile.ExpireOn < DateTime.Now)
//            {
//                //Draw this entry
//                //requires a list of colors to use, which might vary per app
//                GeoArea CellEightArea = OpenLocationCode.DecodeValid(Cell8);
//                var cellPoly = Converters.GeoAreaToPolygon(pluscode);
//                //if (places == null) //Don't download the data twice if we already have it, just reset the tags.
//                //places = GetPlaces(CellEightArea, skipTags: true); // , includeGenerated: Configuration.GetValue<bool>("generateAreas") TODO restore generated area logic.

//                ImageStats info = new ImageStats(pluscode, 160, 200); //Cell8 size TODO use DrawPlusCode here, add parameteres as needed
//                var ops = MapTiles.GetPaintOpsForCustomDataElements(Converters.GeoAreaToPolygon(CellEightArea), "teamColor", "teamColor", info);
                
//                var results = MapTiles.DrawAreaAtSize(info, ops, bgColor); //MapTiles.DrawMPAreaControlMapTile(info, places);
//                if (factionColorTile == null) //create a new entry
//                {
//                    factionColorTile = new MapTile() { PlusCode = Cell8, CreatedOn = DateTime.Now, styleSet = "teamColor", resolutionScale = 11, tileData = results, areaCovered = Converters.GeoAreaToPolygon(CellEightArea) };
//                    db.MapTiles.Add(factionColorTile);
//                }
//                else //update the existing entry.
//                {
//                    factionColorTile.tileData = results;
//                    factionColorTile.ExpireOn = DateTime.Now.AddYears(10);
//                    factionColorTile.CreatedOn = DateTime.Now;
//                }
//                db.SaveChanges();
//            }

//            ImageStats info2 = new ImageStats(pluscode, 160, 200);
//            var output = MapTiles.LayerTiles(info2, baseMapTile.tileData, factionColorTile.tileData);
//            pt.Stop(Cell8);
//            return File(output, "image/png");
//        }

//        //[Route("/[controller]/DrawAreaControlOverlay/{Cell8}")]
//        //public FileContentResult DrawAreaControlOverlay(string Cell8)
//        //{
//        //    //TODO: I think it's better to have the background tiles be shared across modes, and to
//        //    //regenerate the smaller, overlay tiles more often. This means that I need one endpoint
//        //    //for the background normal map tiles and one for the overlay, and the app needs to draw both in order.
//        //    PerformanceTracker pt = new PerformanceTracker("DrawAreaControlOverlay");
//        //    //We will try to minimize rework done.
//        //    var db = new PraxisContext();
//        //    System.Collections.Generic.List<StoredOsmElement> places = null;
//        //    GeoArea pluscode = OpenLocationCode.DecodeValid(Cell8);
//        //    var factionColorTile = db.MapTiles.Where(mt => mt.PlusCode == Cell8 && mt.resolutionScale == 11 && mt.styleSet == "teamColor").FirstOrDefault();
//        //    if (factionColorTile == null || factionColorTile.MapTileId == null || factionColorTile.ExpireOn < DateTime.Now)
//        //    {
//        //        //Draw this entry
//        //        //requires a list of colors to use, which might vary per app
//        //        GeoArea CellEightArea = OpenLocationCode.DecodeValid(Cell8);
//        //        var cellPoly = Converters.GeoAreaToPolygon(pluscode);
//        //        //if (places == null) //Don't download the data twice if we already have it, just reset the tags.
//        //        //places = GetPlaces(CellEightArea, skipTags: true); // , includeGenerated: Configuration.GetValue<bool>("generateAreas") TODO restore generated area logic.

//        //        ImageStats info = new ImageStats(pluscode, 160, 200); //Cell8 size TODO use DrawPlusCode here, add parameteres as needed
//        //        var ops = MapTiles.GetPaintOpsForCustomDataElements(Converters.GeoAreaToPolygon(CellEightArea), "teamColor", "teamColor", info);

//        //        var results = MapTiles.DrawAreaAtSize(info, ops, bgColor); //MapTiles.DrawMPAreaControlMapTile(info, places);
//        //        if (factionColorTile == null) //create a new entry
//        //        {
//        //            factionColorTile = new MapTile() { PlusCode = Cell8, CreatedOn = DateTime.Now, styleSet = "teamColor", resolutionScale = 11, tileData = results, areaCovered = Converters.GeoAreaToPolygon(CellEightArea) };
//        //            db.MapTiles.Add(factionColorTile);
//        //        }
//        //        else //update the existing entry.
//        //        {
//        //            factionColorTile.tileData = results;
//        //            factionColorTile.ExpireOn = DateTime.Now.AddYears(10);
//        //            factionColorTile.CreatedOn = DateTime.Now;
//        //        }
//        //        db.SaveChanges();
//        //    }
//        //    pt.Stop(Cell8);
//        //    return File(factionColorTile.tileData, "image/png");
//        //}

//        [HttpGet]
//        [Route("/[controller]/DrawFactionModeSlippyTile/{zoom}/{x}/{y}.png")]
//        public FileContentResult DrawFactionModeSlippyTile(int x, int y, int zoom, string styleSet = "teamColor")
//        {
//            try
//            {
//                PerformanceTracker pt = new PerformanceTracker("DrawFactionModeSlippyTile");
//                string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString();
//                var db = new PraxisContext();
//                var existingResults = db.SlippyMapTiles.Where(mt => mt.Values == tileKey && mt.styleSet == "paintTown").FirstOrDefault();
//                bool useCache = true;
//                cache.TryGetValue("caching", out useCache);
//                if (!useCache || existingResults == null || existingResults.SlippyMapTileId == null || existingResults.ExpireOn < DateTime.Now)
//                {
//                    //Create this entry
//                    var info = new ImageStats(zoom, x, y, MapTiles.MapTileSizeSquare, MapTiles.MapTileSizeSquare);
//                    //info.filterSize = info.degreesPerPixelX * 2; //I want this to apply to areas, and allow lines to be drawn regardless of length.
//                    //if (zoom >= 15) //Gameplay areas are ~15.
//                    //info.filterSize = 0;

//                    var dataLoadArea = new GeoArea(info.area.SouthLatitude - ConstantValues.resolutionCell10, info.area.WestLongitude - ConstantValues.resolutionCell10, info.area.NorthLatitude + ConstantValues.resolutionCell10, info.area.EastLongitude + ConstantValues.resolutionCell10);
//                    DateTime expires = DateTime.Now;

//                    var paintOps = MapTiles.GetPaintOpsForCustomDataElements(Converters.GeoAreaToPolygon(info.area), "teamColor", "teamColor", info);
//                    byte[] results = MapTiles.DrawAreaAtSize(info, paintOps, TagParser.GetStyleBgColor(styleSet));
//                    expires = DateTime.Now.AddYears(10);
//                    if (existingResults == null)
//                        db.SlippyMapTiles.Add(new SlippyMapTile() { Values = tileKey, CreatedOn = DateTime.Now, styleSet = styleSet, tileData = results, ExpireOn = expires, areaCovered = Converters.GeoAreaToPolygon(dataLoadArea) });
//                    else
//                    {
//                        existingResults.CreatedOn = DateTime.Now;
//                        existingResults.ExpireOn = expires;
//                        existingResults.tileData = results;
//                    }
//                    if (useCache)
//                        db.SaveChanges();
//                    pt.Stop(tileKey + "|" + styleSet);
//                    return File(results, "image/png");
//                }

//                pt.Stop(tileKey + "|" + styleSet);
//                return File(existingResults.tileData, "image/png");
//            }
//            catch (Exception ex)
//            {
//                ErrorLogger.LogError(ex);
//                return null;
//            }
//        }

//        //Might need to restore this for clients
//        [HttpGet]
//        [Route("/[controller]/FindChangedMapTiles/{mapDataId}")]
//        [Route("/Gameplay/FindChangedMapTiles/{mapDataId}")]
//        public string FindChangedMapTiles(long mapDataId)
//        {
//            PerformanceTracker pt = new PerformanceTracker("FindChangedMapTiles");
//            string results = "";
//            //return a separated list of maptiles that need updated.
//            var db = new PraxisContext();
//            var md = db.StoredOsmElements.Where(m => m.id == mapDataId).FirstOrDefault();
//            var space = md.elementGeometry.Envelope;
//            var geoAreaToRefresh = new GeoArea(new GeoPoint(space.Coordinates.Min().Y, space.Coordinates.Min().X), new GeoPoint(space.Coordinates.Max().Y, space.Coordinates.Max().X));
//            var Cell8XTiles = geoAreaToRefresh.LongitudeWidth / resolutionCell8;
//            var Cell8YTiles = geoAreaToRefresh.LatitudeHeight / resolutionCell8;
//            for (var x = 0; x < Cell8XTiles; x++)
//            {
//                for (var y = 0; y < Cell8YTiles; y++)
//                {
//                    var olc = new OpenLocationCode(new GeoPoint(geoAreaToRefresh.SouthLatitude + (resolutionCell8 * y), geoAreaToRefresh.WestLongitude + (resolutionCell8 * x)));
//                    var olcPoly = Converters.GeoAreaToPolygon(olc.Decode());
//                    if (md.elementGeometry.Intersects(olcPoly)) //If this intersects the original way, redraw that tile. Lets us minimize work for oddly-shaped areas.
//                    {
//                        results += olc.CodeDigits + "|";
//                    }
//                }
//            }
//            pt.Stop();
//            return results;
//        }

//        [HttpGet]
//        [Route("/[controller]/AreaOwners/{mapDataId}")]
//        [Route("/Gameplay/AreaOwners/{mapDataId}")]
//        public string AreaOwners(long mapDataId)
//        {
//            //App asks to see which team owns this area.
//            //Return the area name (or type if unnamed), the team that owns it, and its point cost (pipe-separated)
//            PerformanceTracker pt = new PerformanceTracker("AreaOwners");
//            var db = new PraxisContext();
//            var owner = GenericData.GetElementData(mapDataId, "teamColor");
//            var mapData = db.StoredOsmElements.Where(m => m.id == mapDataId).FirstOrDefault();
//            if (mapData == null)
//                //This is an error, probably from not clearing data between server instances.
//                return "MissingArea|Nobody|0";

//            //if (string.IsNullOrWhiteSpace(mapData.name))
//                //mapData.name = areaIdReference[mapData.AreaTypeId].FirstOrDefault();

//            if (owner != "")
//            {
//                var factionName = db.Factions.Where(f => f.FactionId.ToString() == owner).FirstOrDefault().Name;
//                pt.Stop();
//                return mapData.name + "|" + factionName + "|" + GetScoreForSinglePlace(mapData.elementGeometry);
//            }
//            else
//            {
//                pt.Stop();
//                return mapData.name + "|Nobody|" + GetScoreForSinglePlace(mapData.elementGeometry);
//            }
//        }

//        [HttpGet]
//        [Route("/[controller]/FactionScores")]
//        [Route("/Gameplay/FactionScores")]
//        public string FactionScores()
//        {
//            PerformanceTracker pt = new PerformanceTracker("FactionScores");
//            var db = new PraxisContext();
//            var teamNames = db.Factions.ToLookup(k => k.TeamColorTag, v => v.Name);
//            var scores = db.CustomDataOsmElements.Where(d => d.dataKey == "teamColor").GroupBy(d => d.dataValue).Select(d => new {team = d.Key, score = d.Sum(dd => dd.storedOsmElement.elementGeometry.Length), teamName = teamNames[d.Key] }).ToList();
//            //var scores = db.TeamClaims.GroupBy(a => a.FactionId).Select(a => new { team = a.Key, score = a.Sum(aa => aa.points), teamName = teamNames[a.Key] }).ToList();

//            var results = string.Join(Environment.NewLine, scores.Select(s => s.team + "|" + s.teamName + "|" + s.score));
//            pt.Stop();
//            return results;
//        }

//        [HttpGet]
//        [Route("/[controller]/FillTestAreas/{percent}")]
//        public void FillTestAreas(int percent)
//        {
//            var db = new PraxisContext();
//            var settings = db.ServerSettings.First();
//            var factions = db.Factions.ToList();
//            Random r = new Random();

//            var assignPlaces = db.StoredOsmElements.Select(a => a.id).ToList().Where(s => r.Next(0, 100) < percent).ToList(); //first ToList allows r.Next() to work.
//            foreach(var a in assignPlaces)
//            {
//                GenericData.SetStoredElementData(a, "teamColor", factions[r.Next(0, 3)].FactionId.ToString());
//            }
//        }
//    }
//}
