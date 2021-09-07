﻿//using CoreComponents;
//using CoreComponents.Support;
//using Google.OpenLocationCode;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.Extensions.Caching.Memory;
//using Microsoft.Extensions.Configuration;
//using NetTopologySuite.Geometries;
//using PraxisMapper.Classes;
//using SkiaSharp;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using static CoreComponents.DbTables;

////This is all commented out to prove I don't need this dedicated endpoint for Paint The Town mode on a mobile app
///but I don want to keep the simple endpoints here as a reference for potential future examples that do need one.

//namespace PraxisMapper.Controllers
//{
//    [Route("[controller]")]
//    [ApiController]
//    public class PaintTownController : Controller
//    {
//        private readonly IConfiguration Configuration;
//        private IMemoryCache cache; //Using this cache to pass around some values instead of making them DB lookups each time.
//        //Re-purposing this mode
//        //This is no longer a competitive game mode. This is a simpler demo of PraxisMapper's capabilities.
//        //New logic: When a player walks into a cell for the first time that day, set that cell's color to a random value.(enforced client side)
//        //No resets or expirations, no teams.
//        public PaintTownController(IConfiguration configuration, IMemoryCache cacheSingleton)
//        {
//            try
//            {
//                Classes.PerformanceTracker pt = new Classes.PerformanceTracker("PaintTownConstructor");
//                var db = new PraxisContext();
//                Configuration = configuration;
//                cache = cacheSingleton; //Fallback code on actual functions if this is null for some reason.
//            }
//            catch (Exception ex)
//            {
//                Classes.ErrorLogger.LogError(ex);
//            }
//        }

//        [HttpGet]
//        [Route("/[controller]/LearnCell8/{Cell8}")]
//        public string LearnCell8(string Cell8)
//        {
//            //NOTE: this is a slightly optimized version of calling Data/GetAllDataInPlusCode, where it only returns the relevant
//            //data to the client.
//            try
//            {
//                Classes.PerformanceTracker pt = new Classes.PerformanceTracker("LearnCell8PaintTown");
//                var cellData = GenericData.GetAllDataInPlusCode(Cell8);
//                string results = "";
//                foreach (var cell in cellData.Where(c => c.key == "color"))
//                    results += cell.plusCode + "=" + cell.value + "|";
//                pt.Stop(Cell8);
//                return results;
//            }
//            catch (Exception ex)
//            {
//                Classes.ErrorLogger.LogError(ex);
//                return "";
//            }
//        }

//        [HttpGet]
//        [Route("/[controller]/ClaimCell10/{Cell10}")]
//        public void ClaimCell10(string Cell10)
//        {
//            //NOTE: This is a more robust version of calling Data/SetPlusCode data that sets the random color for the client and expires maptiles if any exist
//            try
//            {
//                Classes.PerformanceTracker pt = new Classes.PerformanceTracker("ClaimCell10PaintTown");
//                var db = new PraxisContext();

//                ServerSetting settings = null;
//                if (cache != null)
//                {
//                    settings = (ServerSetting)cache.Get("ServerSettings");
//                }
//                else
//                {
//                    settings = db.ServerSettings.FirstOrDefault();
//                }

//                if (!Place.IsInBounds(Cell10, settings))
//                {
//                    pt.Stop("OOB:" + Cell10);
//                }
//                Random r = new Random();
//                SKColor color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255), 66);
//                GenericData.SetPlusCodeData(Cell10, "color", color.ToString());
//                Geometry g= Converters.GeoAreaToPolygon(OpenLocationCode.DecodeValid(Cell10));
//                MapTiles.ExpireMapTiles(g, "paintTown");
//                MapTiles.ExpireSlippyMapTiles(g, "paintTown");

//                db.SaveChanges();
//                pt.Stop(Cell10);
//            }
//            catch (Exception ex)
//            {
//                Classes.ErrorLogger.LogError(ex);
//            }
//        }

//        //[HttpGet]
//        ////[Route("/[controller]/DrawSlippyTile/{x}/{y}/{zoom}/{layer}")] //old, not slippy map conventions
//        //[Route("/[controller]/DrawSlippyTile/{zoom}/{x}/{y}.png")] //slippy map conventions.
//        //public FileContentResult DrawPaintTownSlippyTile(int x, int y, int zoom, string styleSet = "paintTown")
//        //{
//        //    try
//        //    {
//        //        PerformanceTracker pt = new PerformanceTracker("DrawPaintTownSlippyTile");
//        //        string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString();
//        //        var db = new PraxisContext();
//        //        var existingResults = db.SlippyMapTiles.Where(mt => mt.Values == tileKey && mt.styleSet == "paintTown").FirstOrDefault();
//        //        bool useCache = true;
//        //        cache.TryGetValue("caching", out useCache);
//        //        if (!useCache || existingResults == null || existingResults.SlippyMapTileId == null || existingResults.ExpireOn < DateTime.Now)
//        //        {
//        //            //Create this entry
//        //            var info = new ImageStats(zoom, x, y, MapTiles.MapTileSizeSquare, MapTiles.MapTileSizeSquare);
//        //            //info.filterSize = info.degreesPerPixelX * 2; //I want this to apply to areas, and allow lines to be drawn regardless of length.
//        //            //if (zoom >= 15) //Gameplay areas are ~15.
//        //                //info.filterSize = 0;

//        //            var dataLoadArea = new GeoArea(info.area.SouthLatitude - ConstantValues.resolutionCell10, info.area.WestLongitude - ConstantValues.resolutionCell10, info.area.NorthLatitude + ConstantValues.resolutionCell10, info.area.EastLongitude + ConstantValues.resolutionCell10);
//        //            DateTime expires = DateTime.Now;
                    
//        //            var paintOps = MapTiles.GetPaintOpsForCustomDataPlusCodesFromTagValue(Converters.GeoAreaToPolygon(info.area), "color", "paintTown", info);
//        //            byte[] results = MapTiles.DrawAreaAtSize(info, paintOps, TagParser.GetStyleBgColor(styleSet));
//        //            expires = DateTime.Now.AddYears(10);
//        //            if (existingResults == null)
//        //                db.SlippyMapTiles.Add(new SlippyMapTile() { Values = tileKey, CreatedOn = DateTime.Now, styleSet = styleSet, tileData = results, ExpireOn = expires, areaCovered = Converters.GeoAreaToPolygon(dataLoadArea) });
//        //            else
//        //            {
//        //                existingResults.CreatedOn = DateTime.Now;
//        //                existingResults.ExpireOn = expires;
//        //                existingResults.tileData = results;
//        //            }
//        //            if (useCache)
//        //                db.SaveChanges();
//        //            pt.Stop(tileKey + "|" + styleSet);
//        //            return File(results, "image/png");
//        //        }

//        //        pt.Stop(tileKey + "|" + styleSet);
//        //        return File(existingResults.tileData, "image/png");
//        //    }
//        //    catch (Exception ex)
//        //    {
//        //        ErrorLogger.LogError(ex);
//        //        return null;
//        //    }
//        //}

//        [HttpGet]
//        [Route("/[controller]/FillTestAreas/{percent}")]
//        public void FillTestAreas(int percent)
//        {
//            //NOTE: This isn't called by the client, and is here for admin test purposes, so should probably be replaced 
//            //with a function on AdminController with more flexibility.
//            var db = new PraxisContext();
//            var settings = db.ServerSettings.First();
//            Random r = new Random();

//            double currentY = settings.SouthBound;
//            double currentX = settings.WestBound;

//            //Loop through all Cell10s in range, make (percent) of them filled in with a random color.
//            while(currentY < settings.NorthBound)
//            {
//                while(currentX < settings.EastBound)
//                {
//                    currentX += ConstantValues.resolutionCell10;
                    
//                    if (r.Next(0, 100) > percent)
//                        continue;

//                    var Cell10 = new OpenLocationCode(currentY, currentX, 10).CodeDigits;
//                    SKColor color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255), 66);
//                    GenericData.SetPlusCodeData(Cell10, "color", color.ToString());
//                }
//                currentY += ConstantValues.resolutionCell10;
//                currentX = settings.WestBound;
//            }
//        }
//    }
//}
