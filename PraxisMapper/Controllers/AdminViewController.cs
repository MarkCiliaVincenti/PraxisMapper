﻿using PraxisCore;
using PraxisCore.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PraxisMapper.Classes;
using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Configuration;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    public class AdminViewController : Controller
    {
        private readonly IConfiguration Configuration;
        private IMapTiles MapTiles;
        public AdminViewController(IConfiguration config, IMapTiles mapTiles)
        {
            Configuration = config;
            MapTiles = mapTiles;
        }

        [HttpGet]
        [Route("/[controller]")]
        [Route("/[controller]/Index")]
        public IActionResult Index()
        {
            return View();
        }

        public void ExpireAllMapTiles()
        {
            PerformanceTracker pt = new PerformanceTracker("ExpireMapTilesAdmin");
            var db = new PraxisContext();
            string sql = "UPDATE MapTiles SET ExpireOn = CURRENT_TIMESTAMP"; 
            db.Database.ExecuteSqlRaw(sql);
            sql = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP";
            db.Database.ExecuteSqlRaw(sql);
            pt.Stop();
        }

        [Route("/[controller]/GetMapTileInfo/{zoom}/{x}/{y}")]
        public ActionResult GetMapTileInfo(int x, int y, int zoom) //This should have a view.
        {
            //Draw the map tile, with extra info to send over.
            ImageStats istats = new ImageStats(zoom, x, y, IMapTiles.MapTileSizeSquare);

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            var tile = MapTiles.DrawAreaAtSize(istats);
            sw.Stop();

            ViewBag.placeCount = PraxisCore.Place.GetPlaces(istats.area).Count();
            ViewBag.timeToDraw = sw.Elapsed.ToString();
            ViewBag.imageString = "data:image/png;base64," + Convert.ToBase64String(tile);

            return View();
        }

        [Route("/[controller]/GetPlaceInfo/{sourceElementId}/{sourceElementType}")]
        public ActionResult GetPlaceInfo(long sourceElementId, int sourceElementType)
        {
            var db = new PraxisContext();
            var area = db.StoredOsmElements.Include(e => e.Tags).FirstOrDefault(e => e.sourceItemID == sourceElementId && e.sourceItemType == sourceElementType);
            if (area == null)
                return View();

            TagParser.ApplyTags(new System.Collections.Generic.List<DbTables.StoredOsmElement>() { area }, "mapTiles");
            ViewBag.areaname = TagParser.GetPlaceName(area.Tags);
            ViewBag.type = area.GameElementName;

            var geoarea = Converters.GeometryToGeoArea(area.elementGeometry.Envelope);
            geoarea = new Google.OpenLocationCode.GeoArea(geoarea.SouthLatitude - ConstantValues.resolutionCell10,
                geoarea.WestLongitude - ConstantValues.resolutionCell10,
                geoarea.NorthLatitude + ConstantValues.resolutionCell10,
                geoarea.EastLongitude + ConstantValues.resolutionCell10); //add some padding to the edges.
            ImageStats istats = new ImageStats(geoarea, (int)(geoarea.LongitudeWidth / ConstantValues.resolutionCell11Lon), (int)(geoarea.LatitudeHeight / ConstantValues.resolutionCell11Lat));

            //sanity check: we don't want to draw stuff that won't fit in memory, so check for size and cap it if needed
            //Remember that if we have any multipolygon elements with holes, we need an additional bitmap of the same size to be in RAM, and that gets real expensive fast.
            if (istats.imageSizeX * istats.imageSizeY > 8000000)
            {
                var ratio = geoarea.LongitudeWidth / geoarea.LatitudeHeight; //W:H,
                var newSize = (istats.imageSizeY > 2000 ? 2000 : istats.imageSizeY);
                istats = new ImageStats(geoarea, (int)(newSize * ratio), newSize);
            }
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            //var tileSvg = MapTiles.DrawAreaAtSizeSVG(istats); ViewBag.UseSvg = true;
            var tile = MapTiles.DrawAreaAtSize(istats); ViewBag.UseSvg = false;
            sw.Stop();

            ViewBag.imageString = "data:image/png;base64," + Convert.ToBase64String(tile);
            //ViewBag.imageString = tileSvg.Substring(39); //skip the <xml> tag
            ViewBag.timeToDraw = sw.Elapsed;
            ViewBag.placeCount = 0;
            ViewBag.areasByType = "";
            var places = PraxisCore.Place.GetPlaces(istats.area);
            ViewBag.placeCount = places.Count();
            var grouped = places.GroupBy(p => p.GameElementName);
            string areasByType = "";
            foreach (var g in grouped)
                areasByType += g.Key + g.Count() + "<br />";

            ViewBag.areasByType = areasByType;

            return View();
        }

        [Route("/[controller]/GetPlaceInfo/{privacyId}/")]
        public ActionResult GetPlaceInfo(Guid privacyId)
        {
            var db = new PraxisContext();
            var area = db.StoredOsmElements.Include(e => e.Tags).FirstOrDefault(e => e.privacyId == privacyId);
            if (area != null)
                return GetPlaceInfo(area.sourceItemID, area.sourceItemType);

            return null;
        }

        [Route("/[controller]/EditData")]
        public ActionResult EditData()
        {
            //TODO: break these out into separate views when ready.
            Models.EditData model = new Models.EditData();
            var db = new PraxisContext();
            model.accessKey = "?PraxisAuthKey=" + Configuration["serverAuthKey"];
            model.globalDataKeys = db.GlobalDataEntries.Select(g => new SelectListItem(g.dataKey, g.dataValue)).ToList();
            model.playerKeys = db.PlayerData.Select(p => p.deviceID).Distinct().Select(g => new SelectListItem(g, g)).ToList();

            model.stylesetKeys = db.TagParserEntries.Select(t => t.styleSet).Distinct().Select(t => new SelectListItem(t, t)).ToList();
            model.stylesetKeys.Insert(0, new SelectListItem("", ""));

            return View(model);
        }

        [Route("/[controller]/EditGeography")]
        public ActionResult EditGeography()
        {
            return View();
        }
    }
}
