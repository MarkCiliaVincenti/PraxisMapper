﻿using CoreComponents;
using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PraxisMapper.Classes;
using System;
using System.Linq;
using static CoreComponents.DbTables;
using static CoreComponents.Place;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class MapTileController : Controller
    {
        private readonly IConfiguration Configuration;
        private static MemoryCache cache;

        //TODO: consider playing with the SKSVGCanvas to see if SVG maptiles are faster/smaller/better in any ways
        //though the real delay is loading data from DB/disk, not drawing the loaded shapes.

        public MapTileController(IConfiguration configuration)
        {
            Configuration = configuration;

            if (cache == null && Configuration.GetValue<bool>("enableCaching") == true)
            {
                var options = new MemoryCacheOptions();
                options.SizeLimit = 1024;
                cache = new MemoryCache(options);
            }
        }

        [HttpGet]
        [Route("/[controller]/DrawSlippyTile/{x}/{y}/{zoom}/{layer}")]
        public FileContentResult DrawSlippyTile(int x, int y, int zoom, int layer)
        {
            //slippymaps don't use coords. They use a grid from -180W to 180E, 85.0511N to -85.0511S (they might also use radians, not degrees, for an additional conversion step)
            //with 2^zoom level tiles in place. so, i need to do some math to get a coordinate
            //X: -180 + ((360 / 2^zoom) * X)
            //Y: 8
            //Remember to invert Y to match PlusCodes going south to north.
            //BUT Also, PlusCodes have 20^(zoom/2) tiles, and Slippy maps have 2^zoom tiles, this doesn't even line up nicely.
            //Slippy Map tiles might just have to be their own thing.
            //I will also say these are 512x512 images.
            //TODO: should I set a longer timeout for these webtiles, and expire them when something in them gets updated?
            //This is much harder to detect for slippy maps, since I have to re-caculate the X and Y on a bunch of zoom levels for it.

            try
            {
                PerformanceTracker pt = new PerformanceTracker("DrawSlippyTile");
                string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString();
                var db = new PraxisContext();
                var existingResults = db.SlippyMapTiles.Where(mt => mt.Values == tileKey && mt.mode == layer).FirstOrDefault(); 
                if (existingResults == null || existingResults.SlippyMapTileId == null || existingResults.ExpireOn < DateTime.Now)
                {
                    //Create this entry
                    //requires a list of colors to use, which might vary per app

                    //MapTiles.GetSlippyResolutions(x, y, zoom, ou)
                    var n = Math.Pow(2, zoom);

                    var lon_degree_w = x / n * 360 - 180;
                    var lon_degree_e = (x + 1) / n * 360 - 180;
                    var lat_rads_n = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / n)));
                    var lat_degree_n = lat_rads_n * 180 / Math.PI;
                    var lat_rads_s = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * (y + 1) / n)));
                    var lat_degree_s = lat_rads_s * 180 / Math.PI;

                    var relevantArea = new GeoArea(lat_degree_s, lon_degree_w, lat_degree_n, lon_degree_e);
                    var areaHeightDegrees = lat_degree_n - lat_degree_s;
                    var areaWidthDegrees = 360 / n;

                    var filterSize = areaHeightDegrees / 128; //Height is always <= width, so use that divided by vertical resolution to get 1 pixel's size in degrees. Don't load stuff smaller than that.
                                                              //Test: set to 128 instead of 512: don't load stuff that's not 4 pixels ~.008 degrees at zoom 8.

                    var dataLoadArea = new GeoArea(relevantArea.SouthLatitude - ConstantValues.resolutionCell10, relevantArea.WestLongitude - ConstantValues.resolutionCell10, relevantArea.NorthLatitude + ConstantValues.resolutionCell10, relevantArea.EastLongitude + ConstantValues.resolutionCell10);
                    DateTime expires = DateTime.Now;
                    byte[] results = null;
                    switch (layer)
                    {
                        case 1: //Base map tile
                            //add some padding so we don't clip off points at the edge of a tile
                            var places = GetPlaces(dataLoadArea, includeGenerated: false, filterSize: filterSize); //NOTE: in this case, we want generated areas to be their own slippy layer, so the config setting is ignored here.
                            results = MapTiles.DrawAreaMapTileSlippySkia(ref places, relevantArea, areaHeightDegrees, areaWidthDegrees);
                            expires = DateTime.Now.AddYears(10); //Assuming you are going to manually update/clear tiles when you reload base data
                            break;
                        case 2: //PaintTheTown overlay. 
                            results = MapTiles.DrawPaintTownSlippyTileSkia(relevantArea, 2);
                            expires = DateTime.Now.AddMinutes(1); //We want this to be live-ish, but not overwhelming, so we cache this for 60 seconds.
                            break;
                        case 3: //MultiplayerAreaControl overlay.
                            results = MapTiles.DrawMPAreaMapTileSlippySkia(relevantArea, areaHeightDegrees, areaWidthDegrees);
                            expires = DateTime.Now.AddYears(10); //These expire when an area inside gets claimed now, so we can let this be permanent.
                            break;
                        case 4: //GeneratedMapData areas.
                            var places2 = GetGeneratedPlaces(dataLoadArea); //NOTE: this overlay doesn't need to check the config, since it doesn't create them, just displays them as their own layer.
                            results = MapTiles.DrawAreaMapTileSlippySkia(ref places2, relevantArea, areaHeightDegrees, areaWidthDegrees, true);
                            expires = DateTime.Now.AddYears(10); //again, assuming these don't change unless you manually updated entries.
                            break;
                        case 5: //Custom objects (scavenger hunt). Should be points loaded up, not an overlay?
                            //this isnt supported yet as a game mode.
                            break;
                        case 6: //Admin boundaries. Will need to work out rules on how to color/layer these. Possibly multiple layers, 1 per level? Probably not helpful for game stuff.
                            var placesAdmin = GetAdminBoundaries(dataLoadArea);
                            results = MapTiles.DrawAdminBoundsMapTileSlippy(ref placesAdmin, relevantArea, areaHeightDegrees, areaWidthDegrees);
                            expires = DateTime.Now.AddYears(10); //Assuming you are going to manually update/clear tiles when you reload base data
                            break;
                        case 7: //This might be the layer that shows game areas on the map. Draw outlines of them. Means games will also have a Geometry object attached to them for indexing.
                            break;
                        case 8: //This might be what gets called to load an actual game. The ID will be the game in question, so X and Y values could be ignored?
                            break;
                        case 9: //Draw Cell8 boundaries as lines. I thought about not saving these to the DB, but i can get single-ms time on reading an existing file instead of double-digit ms recalculating them.
                            results = MapTiles.DrawCell8GridLines(relevantArea);
                            break;
                        case 10: //Draw Cell10 boundaries as lines. I thought about not saving these to the DB, but i can get single-ms time on reading an existing file instead of double-digit ms recalculating them.
                            results = MapTiles.DrawCell10GridLines(relevantArea);
                            break;
                        case 11: //Admin bounds as a base layer. Countries only. Or states?
                            var placesAdminStates = GetAdminBoundaries(dataLoadArea);
                            placesAdminStates = placesAdminStates.Where(p => p.type == "admin4").ToList();
                            results = MapTiles.DrawAdminBoundsMapTileSlippy(ref placesAdminStates, relevantArea, areaHeightDegrees, areaWidthDegrees);
                            expires = DateTime.Now.AddYears(10); //Assuming you are going to manually update/clear tiles when you reload base data
                            break;
                    }
                    if (existingResults == null)
                        db.SlippyMapTiles.Add(new SlippyMapTile() { Values = tileKey, CreatedOn = DateTime.Now, mode = layer, tileData = results, ExpireOn = expires, areaCovered = Converters.GeoAreaToPolygon(dataLoadArea) });
                    else
                    {
                        existingResults.CreatedOn = DateTime.Now;
                        existingResults.ExpireOn = expires;
                        existingResults.tileData = results;
                    }
                    db.SaveChanges();
                    pt.Stop(tileKey + "|" + layer);
                    return File(results, "image/png");
                }

                pt.Stop(tileKey + "|" + layer);
                return File(existingResults.tileData, "image/png");
            }
            catch(Exception ex)
            {
                ErrorLogger.LogError(ex);
                return null;
            }
        }

        [HttpGet]
        [Route("/[controller]/CheckTileExpiration/{PlusCode}/{mode}")]
        public string CheckTileExpiration(string PlusCode, int mode) //For simplicity, maptiles expire after the Date part of a DateTime. Intended for base tiles.
        {
            //I pondered making this a boolean, but the client needs the expiration date to know if it's newer or older than it's version. Not if the server needs to redraw the tile. That happens on load.
            //I think, what I actually need, is the CreatedOn, and if it's newer than the client's tile, replace it.
            PerformanceTracker pt = new PerformanceTracker("CheckTileExpiration");
            var db = new PraxisContext();
            var mapTileExp = db.MapTiles.Where(m => m.PlusCode == PlusCode && m.mode == mode).Select(m => m.ExpireOn).FirstOrDefault();
            pt.Stop();
            return mapTileExp.ToShortDateString();
        }

        [HttpGet]
        [Route("/[controller]/DrawPath")]
        public byte[] DrawPath()
        {
            //NOTE: URL limitations block this from being a usable REST style path, so this one may require reading data bindings from the body instead
            string path = new System.IO.StreamReader(Request.Body).ReadToEnd();
            return MapTiles.DrawUserPath(path);
        }
    }
}
