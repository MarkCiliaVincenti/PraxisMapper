﻿using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static CoreComponents.ConstantValues;
using static CoreComponents.DbTables;
using static CoreComponents.Place;
using static CoreComponents.Singletons;
using SkiaSharp;
using Microsoft.EntityFrameworkCore;
using CoreComponents.Support;

namespace CoreComponents
{
    public static class MapTiles
    {
        public const int MapTileSizeSquare = 512;
        static SKPaint eraser = new SKPaint() { Color = SKColors.Transparent, BlendMode = SKBlendMode.Src, Style = SKPaintStyle.StrokeAndFill }; //BlendMode is the important part for an Eraser.
        
        public static void GetResolutionValues(int CellSize, out double resX, out double resY) //This is degrees per pixel in a maptile.
        {
            switch (CellSize)
            {
                case 2: //not real useful but lets allow it
                    resX = resolutionCell2;
                    resY = resolutionCell2;
                    break;
                case 4:
                    resX = resolutionCell4;
                    resY = resolutionCell4;
                    break;
                case 6:
                    resX = resolutionCell6;
                    resY = resolutionCell6;
                    break;
                case 8:
                    resX = resolutionCell8;
                    resY = resolutionCell8;
                    break;
                case 10:
                    resX = resolutionCell10;
                    resY = resolutionCell10;
                    break;
                case 11:
                    resX = resolutionCell11Lon;
                    resY = resolutionCell11Lat;
                    break;
                default: //Not a supported resolution
                    resX = 0;
                    resY = 0;
                    break;
            }
        }

        public static void GetSlippyResolutions(int xTile, int yTile, int zoomLevel, out double resX, out double resY) //This is degrees per pixel in a maptile.
        {
            //NOTE: currently, this calculation is done in 2 steps, with the last one to get resX and resY at the end done in an inner function and earlier code using a GeoArea based on the coordinates.
            //These are harder to cache, because they change based on latitude. X tiles are always the same, Y tiles scale with latitude.

            //var stats = new ImageStats();

            var n = Math.Pow(2, zoomLevel);

            var lon_degree_w = xTile / n * 360 - 180;
            var lon_degree_e = (xTile + 1) / n * 360 - 180;

            var lat_rads_n = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * yTile / n)));
            var lat_degree_n = lat_rads_n * 180 / Math.PI;

            var lat_rads_s = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * (yTile + 1) / n)));
            var lat_degree_s = lat_rads_s * 180 / Math.PI;

            var areaHeightDegrees = lat_degree_n - lat_degree_s;
            var areaWidthDegrees = 360 / n;

            resX = areaWidthDegrees / MapTileSizeSquare;
            resY = areaHeightDegrees / MapTileSizeSquare;
        }

        public static byte[] DrawMPControlAreaMapTileSkia(GeoArea totalArea, int pixelSizeCells, Tuple<long, int> shortcut = null)
        {
            //These are Mode=2 tiles in the database, used as an overlay that's merged into the baseline map tile. Should be faster than re-drawing a full tile.
            //Initial suggestion for these is to use a pixelSizeCell value 2 steps down from the areas size
            //EX: for Cell8 tiles, use 11 for the pixel cell size (this is the default I use, smallest location in a pixel sets the color)
            //or for Cell4 tiles, use Cell8 pixel size. (alternative sort for pixel color: largest area? Exclude points?)
            //But this is gaining flexibilty, and adjusting pixelSizeCells to a double to be degreesPerPixel allows for more freedom.
            double degreesPerPixelX, degreesPerPixelY;
            double filterSize = 0;
            GetResolutionValues(pixelSizeCells, out degreesPerPixelX, out degreesPerPixelY);
            if (pixelSizeCells < 10) // Roads and buildings are good at Cell10+. Not helpful at Cell8-;
                filterSize = degreesPerPixelX / 2; //things smaller than half a pixel will not be considered for the map tile. Might just want to toggle the alternate sort rules for pixels (most area, not smallest item)
            //Or should this filter to 'smallest area over filter size'?

            //To make sure we don't get any seams on our maptiles (or points that don't show a full circle, we add a little extra area to the image before drawing (Skia just doesn't draw things outside the canvas)
            var dataLoadArea = new GeoArea(new GeoPoint(totalArea.Min.Latitude - resolutionCell10, totalArea.Min.Longitude - resolutionCell10), new GeoPoint(totalArea.Max.Latitude + resolutionCell10, totalArea.Max.Longitude + resolutionCell10));

            List<MapData> rowPlaces;
            //create a new bitmap.
            
            //MemoryStream ms = new MemoryStream();
            //int imagesizeX = (int)Math.Ceiling(totalArea.LongitudeWidth / degreesPerPixelX);
            //int imagesizeY = (int)Math.Ceiling(totalArea.LatitudeHeight / degreesPerPixelY);

            var db = new PraxisContext();
            //Replacing this one requires multiple style list support first.
            List<StoredWay> allPlaces = GetPlaces(dataLoadArea);  //, null, false, true, filterSize //Includes generated here with the final True parameter.
            List<long> placeIDs = allPlaces.Select(a => a.sourceItemID).ToList();
            Dictionary<long, int> teamClaims = db.AreaControlTeams.Where(act => placeIDs.Contains(act.MapDataId)).ToDictionary(k => k.MapDataId, v => v.FactionId);

            //crop all places to the current area. This removes a ton of work from the process by simplifying geometry to only what's relevant, instead of drawing all of a great lake or state-wide park.
            var cropArea = Converters.GeoAreaToPolygon(dataLoadArea);
            ImageStats info = new ImageStats(dataLoadArea, 80, 100);
            //A quick fix to drawing order when multiple areas take up the entire cell: sort before the crop (otherwise, the areas are drawn in a random order, which makes some disappear)
            //Affects small map tiles more often than larger ones, but it can affect both.
            allPlaces = allPlaces.Where(ap => teamClaims.ContainsKey(ap.sourceItemID)).OrderByDescending(a => a.AreaSize).ToList();
            foreach (var ap in allPlaces)
                ap.wayGeometry = ap.wayGeometry.Intersection(cropArea); //This is a ref list, so this crop will apply if another call is made to this function with the same list.

            var image = DrawAreaAtSizeV4(info, allPlaces); //InnerDrawSkia(ref allPlaces, totalArea, degreesPerPixelX, degreesPerPixelY, imagesizeX, imagesizeY, transparent: true);

            return image;
        }

        public static byte[] DrawMPAreaMapTileSlippySkia(ImageStats info)
        {
            return DrawMPAreaMapTileSlippySkia(info.area, info.area.LatitudeHeight, info.area.LongitudeWidth);
        }
        public static byte[] DrawMPAreaMapTileSlippySkia(GeoArea totalArea, double areaHeight, double areaWidth)
        {
            //Resolution scaling here is flexible, since we're always drawing a 512x512 tile.
            double degreesPerPixelX, degreesPerPixelY;
            degreesPerPixelX = areaWidth / MapTileSizeSquare;
            degreesPerPixelY = areaHeight / MapTileSizeSquare;
            bool drawEverything = false; //for debugging/testing
            var smallestFeature = (drawEverything ? 0 : degreesPerPixelX < degreesPerPixelY ? degreesPerPixelX : degreesPerPixelY);

            List<MapData> rowPlaces;
            MemoryStream ms = new MemoryStream();
            int imagesizeX = MapTileSizeSquare;
            int imagesizeY = MapTileSizeSquare;

            //To make sure we don't get any seams on our maptiles (or points that don't show a full circle, we add a little extra area to the image before drawing, then crop it out at the end.
            //Do this after determining image size, since Skia will ignore parts off-canvas.
            var loadDataArea = new GeoArea(new GeoPoint(totalArea.Min.Latitude - resolutionCell10, totalArea.Min.Longitude - resolutionCell10), new GeoPoint(totalArea.Max.Latitude + resolutionCell10, totalArea.Max.Longitude + resolutionCell10));

            var db = new PraxisContext();
            //Replacing this one requires multiple style list support first.
            List<StoredWay> allPlaces = GetPlaces(loadDataArea); //, null, false, true, smallestFeature //Includes generated here with the final True parameter.
            List<long> placeIDs = allPlaces.Select(a => a.sourceItemID).ToList();
            Dictionary<long, int> teamClaims = db.AreaControlTeams.Where(act => placeIDs.Contains(act.MapDataId)).ToDictionary(k => k.MapDataId, v => v.FactionId);
            allPlaces = allPlaces.Where(a => teamClaims.ContainsKey(a.sourceItemID)).ToList();

            //crop all places to the current area. This removes a ton of work from the process by simplifying geometry to only what's relevant, instead of drawing all of a great lake or state-wide park.
            var cropArea = Converters.GeoAreaToPolygon(loadDataArea);

            //A quick fix to drawing order when multiple areas take up the entire cell: sort before the crop (otherwise, the areas are drawn in a random order, which makes some disappear)
            //Affects small map tiles more often than larger ones, but it can affect both.
            //This where clause means things smaller than 1 pixel won't get drawn. It's a C# filter here, but it would be faster to do DB-side on a SizeColumn on Mapdata to save more time, in the function above this one.
            allPlaces = allPlaces.Where(a => a.AreaSize >= smallestFeature).OrderByDescending(a => a.AreaSize).ToList();
            foreach (var ap in allPlaces)
                ap.wayGeometry = ap.wayGeometry.Intersection(cropArea); //This is a ref list, so this crop will apply if another call is made to this function with the same list.

            ImageStats info = new ImageStats(loadDataArea, MapTileSizeSquare, MapTileSizeSquare);
            return DrawAreaAtSizeV4(info, allPlaces, TagParser.teams); //TODO: force transparent background on this one.
        }

        public static byte[] DrawPaintTownSlippyTileSkia(GeoArea relevantArea, int instanceID)
        {
            //It might be fun on rare occasion to try and draw this all at once, but zoomed out too far and we won't see anything and will be very slow.
            //Find all Cell8s in the relevant area.
            MemoryStream ms = new MemoryStream();
            var imagesizeX = MapTileSizeSquare;
            var imagesizeY = MapTileSizeSquare;
            var Cell8Wide = relevantArea.LongitudeWidth / resolutionCell8;
            var Cell8High = relevantArea.LatitudeHeight / resolutionCell8;
            var Cell10PixelSize = resolutionCell10 / relevantArea.LongitudeWidth; //Making this square for now.
            var resolutionX = relevantArea.LongitudeWidth / imagesizeX;
            var resolutionY = relevantArea.LatitudeHeight / imagesizeY;

            //These may or may not be the same, even if the map tile is smaller than 1 Cell8.
            var firstCell8 = new OpenLocationCode(relevantArea.SouthLatitude, relevantArea.WestLongitude).CodeDigits.Substring(0, 8);
            var lastCell8 = new OpenLocationCode(relevantArea.NorthLatitude, relevantArea.EastLongitude).CodeDigits.Substring(0, 8);
            if (firstCell8 != lastCell8)
            {
                //quick hack to make sure we process enough data.
                Cell8High++;
                Cell8Wide++;
            }

            List<PaintTownEntry> allData = new List<PaintTownEntry>();
            for (var x = 0; x < Cell8Wide; x++)
                for (var y = 0; y < Cell8High; y++)
                {
                    var thisCell = new OpenLocationCode(relevantArea.SouthLatitude + (resolutionCell8 * x), relevantArea.WestLongitude + (resolutionCell8 * y)).CodeDigits.Substring(0, 8);
                    var thisData = PaintTown.LearnCell8(instanceID, thisCell);
                    allData.AddRange(thisData);
                }

            //Some image items setup.
            SkiaSharp.SKBitmap bitmap = new SkiaSharp.SKBitmap(MapTileSizeSquare, MapTileSizeSquare, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
            SkiaSharp.SKCanvas canvas = new SkiaSharp.SKCanvas(bitmap);
            var bgColor = new SkiaSharp.SKColor();
            SkiaSharp.SKColor.TryParse("00000000", out bgColor); //this one wants a transparent background.
            canvas.Clear(bgColor);
            canvas.Scale(1, -1, MapTileSizeSquare / 2, MapTileSizeSquare / 2);
            SkiaSharp.SKPaint paint = new SkiaSharp.SKPaint();
            SkiaSharp.SKColor color = new SkiaSharp.SKColor();
            paint.IsAntialias = true;
            foreach (var line in allData)
            {
                var location = OpenLocationCode.DecodeValid(line.Cell10);
                var placeAsPoly = Converters.GeoAreaToPolygon(location);
                var path = new SkiaSharp.SKPath();
                path.AddPoly(Converters.PolygonToSKPoints(placeAsPoly, relevantArea, resolutionX, resolutionY));
                paint.Style = SkiaSharp.SKPaintStyle.Fill;
                SkiaSharp.SKColor.TryParse(teamColorReferenceLookupSkia[line.FactionId].FirstOrDefault(), out color);
                paint.Color = color;
                canvas.DrawPath(path, paint);
            }

            var skms = new SkiaSharp.SKManagedWStream(ms);
            bitmap.Encode(skms, SkiaSharp.SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            return results;
        }

        public static byte[] DrawCell8GridLines(GeoArea totalArea)
        {
            int imageSizeX = MapTileSizeSquare;
            int imageSizeY = MapTileSizeSquare;
            SKBitmap bitmap = new SKBitmap(imageSizeX, imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            var bgColor = new SKColor();
            SKColor.TryParse("00000000", out bgColor);
            canvas.Clear(bgColor);
            canvas.Scale(1, -1, imageSizeX / 2, imageSizeY / 2);
            SKPaint paint = new SKPaint();
            SKColor color = new SKColor();
            SKColor.TryParse("#FF0000", out color);
            paint.Color = color;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 3;
            paint.IsAntialias = true;

            double degreesPerPixelX = totalArea.LongitudeWidth / imageSizeX;
            double degreesPerPixelY = totalArea.LatitudeHeight / imageSizeY;

            //This is hardcoded to Cell 8 spaced gridlines.
            var imageLeft = totalArea.WestLongitude;
            var spaceToFirstLineLeft = (imageLeft % resolutionCell8);

            var imageBottom = totalArea.SouthLatitude;
            var spaceToFirstLineBottom = (imageBottom % resolutionCell8);

            double lonLineTrackerDegrees = imageLeft - spaceToFirstLineLeft; //This is degree coords
            while (lonLineTrackerDegrees <= totalArea.EastLongitude + resolutionCell8) //This means we should always draw at least 2 lines, even if they're off-canvas.
            {
                var geoLine = new LineString(new Coordinate[] { new Coordinate(lonLineTrackerDegrees, 90), new Coordinate(lonLineTrackerDegrees, -90) });
                var points = Converters.PolygonToSKPoints(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
                canvas.DrawLine(points[0], points[1], paint);
                lonLineTrackerDegrees += resolutionCell8;
            }

            double latLineTrackerDegrees = imageBottom - spaceToFirstLineBottom; //This is degree coords
            while (latLineTrackerDegrees <= totalArea.NorthLatitude + resolutionCell8) //This means we should always draw at least 2 lines, even if they're off-canvas.
            {
                var geoLine = new LineString(new Coordinate[] { new Coordinate(180, latLineTrackerDegrees), new Coordinate(-180, latLineTrackerDegrees) });
                var points = Converters.PolygonToSKPoints(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
                canvas.DrawLine(points[0], points[1], paint);
                latLineTrackerDegrees += resolutionCell8;
            }

            var ms = new MemoryStream();
            var skms = new SkiaSharp.SKManagedWStream(ms);
            bitmap.Encode(skms, SkiaSharp.SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            return results;
        }

        public static byte[] DrawCell10GridLines(GeoArea totalArea)
        {
            int imageSizeX = MapTileSizeSquare;
            int imageSizeY = MapTileSizeSquare;
            SKBitmap bitmap = new SKBitmap(imageSizeX, imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            var bgColor = new SKColor();
            SKColor.TryParse("00000000", out bgColor);
            canvas.Clear(bgColor);
            canvas.Scale(1, -1, imageSizeX / 2, imageSizeY / 2);
            SKPaint paint = new SKPaint();
            SKColor color = new SKColor();
            SKColor.TryParse("#00CCFF", out color);
            paint.Color = color;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 1;
            paint.IsAntialias = true;

            double degreesPerPixelX = totalArea.LongitudeWidth / imageSizeX;
            double degreesPerPixelY = totalArea.LatitudeHeight / imageSizeY;

            //This is hardcoded to Cell 8 spaced gridlines.
            var imageLeft = totalArea.WestLongitude;
            var spaceToFirstLineLeft = (imageLeft % resolutionCell10);

            var imageBottom = totalArea.SouthLatitude;
            var spaceToFirstLineBottom = (imageBottom % resolutionCell10);

            double lonLineTrackerDegrees = imageLeft - spaceToFirstLineLeft; //This is degree coords
            while (lonLineTrackerDegrees <= totalArea.EastLongitude + resolutionCell10) //This means we should always draw at least 2 lines, even if they're off-canvas.
            {
                var geoLine = new LineString(new Coordinate[] { new Coordinate(lonLineTrackerDegrees, 90), new Coordinate(lonLineTrackerDegrees, -90) });
                var points = Converters.PolygonToSKPoints(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
                canvas.DrawLine(points[0], points[1], paint);
                lonLineTrackerDegrees += resolutionCell10;
            }

            double latLineTrackerDegrees = imageBottom - spaceToFirstLineBottom; //This is degree coords
            while (latLineTrackerDegrees <= totalArea.NorthLatitude + resolutionCell10) //This means we should always draw at least 2 lines, even if they're off-canvas.
            {
                var geoLine = new LineString(new Coordinate[] { new Coordinate(180, latLineTrackerDegrees), new Coordinate(-180, latLineTrackerDegrees) });
                var points = Converters.PolygonToSKPoints(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
                canvas.DrawLine(points[0], points[1], paint);
                latLineTrackerDegrees += resolutionCell10;
            }

            var ms = new MemoryStream();
            var skms = new SkiaSharp.SKManagedWStream(ms);
            bitmap.Encode(skms, SkiaSharp.SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            return results;
        }

        public static byte[] DrawAdminBoundsMapTileSlippy(ref List<StoredWay> allPlaces, ImageStats info)
        {
            //The correct replacement for this is to just do the normal draw, but only feed in admin bound areas.
            return null; 
            //return DrawAdminBoundsMapTileSlippy(ref allPlaces, info.area, info.area.LatitudeHeight, info.area.LongitudeWidth, false);
        }

        public static byte[] InnerDrawSkia(ref List<MapData> allPlaces, GeoArea totalArea, double degreesPerPixelX, double degreesPerPixelY, int imageSizeX, int imageSizeY, bool transparent = false, bool colorEachPlace = false)
        {
            //Some image items setup.
            SKBitmap bitmap = new SKBitmap(imageSizeX, imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            var bgColor = new SKColor();
            if (transparent)
                SKColor.TryParse("00000000", out bgColor);
            else
                //SKColor.TryParse(areaColorReference[999].FirstOrDefault(), out bgColor);
            canvas.Clear(bgColor);
            canvas.Scale(1, -1, imageSizeX / 2, imageSizeY / 2);
            SKPaint paint = new SKPaint();
            SKColor color = new SKColor();
            paint.IsAntialias = true;
            //I want to draw lines at least 3 pixels wide for Cell8HighRes tiles and similar Slippy tiles. (Zoom 14-20 on Slippy)
            //otherwise they can be 1 as we zoom out. (Zoom 13 and lower)
            if (degreesPerPixelX <= .00003125) //value at Cell8HighRes command parameters
                paint.StrokeWidth = 3; //Makes trails, roads, and some rivers easier to see.
            else
                paint.StrokeWidth = 1;

            foreach (var place in allPlaces) //If i get unexpected black background, an admin area probably got passed in with AllPlaces. Filter those out at the level above this function.
            {
                //var hexcolor = areaColorReference[place.AreaTypeId].FirstOrDefault();

                //if (colorEachPlace == false)
                    //SKColor.TryParse(hexcolor, out color); //NOTE: this is AARRGGBB, so when I do transparency I need to add that to the front, not the back.
                //else
                    //color = PickColorForAdminBounds(place);
                paint.Color = color;
                //paint.StrokeWidth = 1;
                switch (place.place.GeometryType)
                {
                    case "Polygon":
                        var path = new SKPath();
                        path.AddPoly(Converters.PolygonToSKPoints(place.place, totalArea, degreesPerPixelX, degreesPerPixelY));
                        paint.Style = SKPaintStyle.Fill;
                        canvas.DrawPath(path, paint);
                        break;
                    case "MultiPolygon":
                        foreach (var p in ((MultiPolygon)place.place).Geometries)
                        {
                            var path2 = new SKPath();
                            path2.AddPoly(Converters.PolygonToSKPoints(p, totalArea, degreesPerPixelX, degreesPerPixelY));
                            paint.Style = SKPaintStyle.Fill;
                            canvas.DrawPath(path2, paint);
                        }
                        break;
                    case "LineString":
                        paint.Style = SKPaintStyle.Stroke;
                        var points = Converters.PolygonToSKPoints(place.place, totalArea, degreesPerPixelX, degreesPerPixelY);
                        for (var line = 0; line < points.Length - 1; line++)
                            canvas.DrawLine(points[line], points[line + 1], paint);
                        break;
                    case "MultiLineString":
                        foreach (var p in ((MultiLineString)place.place).Geometries)
                        {
                            paint.Style = SKPaintStyle.Stroke;
                            var points2 = Converters.PolygonToSKPoints(p, totalArea, degreesPerPixelX, degreesPerPixelY);
                            for (var line = 0; line < points2.Length - 1; line++)
                                canvas.DrawLine(points2[line], points2[line + 1], paint);
                        }
                        break;
                    case "Point":
                        paint.Style = SKPaintStyle.Fill;
                        var circleRadius = (float)(resolutionCell10 / degreesPerPixelX / 2); //I want points to be drawn as 1 Cell10 in diameter.
                        var convertedPoint = Converters.PolygonToSKPoints(place.place, totalArea, degreesPerPixelX, degreesPerPixelY);
                        canvas.DrawCircle(convertedPoint[0], circleRadius, paint);
                        break;
                }
            }

            var ms = new MemoryStream();
            var skms = new SKManagedWStream(ms);
            bitmap.Encode(skms, SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            return results;
        }

        public static void ExpireMapTiles(Geometry g, int limitModeTo = 0)
        {
            //If this would be faster as raw SQL, see function below for a template on how to write that.
            var db = new PraxisContext();
            var mapTiles = db.MapTiles.Where(m => m.areaCovered.Intersects(g) && (limitModeTo == 0 || m.mode == limitModeTo)).ToList(); //TODO: can I select only the ExpiresOn value and have that save back correctly?
            foreach (var mt in mapTiles)
                mt.ExpireOn = DateTime.Now;

            db.SaveChanges();
        }

        public static void ExpireSlippyMapTiles(Geometry g, int limitModeTo = 0)
        {
            //Might this be better off as raw SQL? If I expire, say, an entire state, that could be a lot of map tiles to pull into RAM just for a date to change.
            //var raw = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE ST_INTERSECTS(areaCovered, ST_GeomFromText(" + g.AsText() + "))";
            var db = new PraxisContext();
            var mapTiles = db.SlippyMapTiles.Where(m => m.areaCovered.Intersects(g) && (limitModeTo == 0 || m.mode == limitModeTo)).ToList(); //TODO: can I select only the ExpiresOn value and have that save back correctly?
            foreach (var mt in mapTiles)
                mt.ExpireOn = DateTime.Now;

            db.SaveChanges();
        }

        public static SKColor PickColorForAdminBounds(MapData place)
        {
            //TODO: this is a test function, and should be treated as such. Move to Corecomponents and update to the new data storage setup.
            //Each place should get a unique, but consistent, color. Which means we're mostly looking for a hash
            var hasher = System.Security.Cryptography.MD5.Create();
            var value = place.name.ToByteArrayUnicode();
            var hash = hasher.ComputeHash(value);

            SKColor results = new SKColor(hash[0], hash[1], hash[2], Convert.ToByte(64)); //all have the same transparency level
            return results;
        }

        public static byte[] DrawUserPath(string pointListAsString)
        {
            //String is formatted as Lat,Lon~Lat,Lon~ repeating. Characters chosen to not be percent-encoded if submitted as part of the URL.
            //first, convert this to a list of latlon points
            string[] pointToConvert = pointListAsString.Split("|");
            List<Coordinate> coords = pointToConvert.Select(p => new Coordinate(double.Parse(p.Split(',')[0]), double.Parse(p.Split(',')[1]))).ToList();

            var mapBuffer = resolutionCell8 / 2; //Leave some area around the edges of where they went.
            GeoArea mapToDraw = new GeoArea(coords.Min(c => c.Y) - mapBuffer, coords.Min(c => c.X) - mapBuffer, coords.Max(c => c.Y) + mapBuffer, coords.Max(c => c.X) + mapBuffer);

            ImageStats info = new ImageStats(mapToDraw, 1024, 1024);

            double degreesPerPixelX = mapToDraw.LongitudeWidth / 1024;
            double degreesPerPixelY = mapToDraw.LatitudeHeight / 1024;

            LineString line = new LineString(coords.ToArray());
            var drawableLine = Converters.PolygonToSKPoints(line, mapToDraw, degreesPerPixelX, degreesPerPixelY);

            //Now, draw that path on the map.
            var places = GetPlaces(mapToDraw); //, null, false, false, degreesPerPixelX * 4 ///TODO: restore item filtering
            var baseImage = DrawAreaAtSizeV4(info, places); //InnerDrawSkia(ref places, mapToDraw, degreesPerPixelX, degreesPerPixelY, 1024, 1024);

            SKBitmap sKBitmap = SKBitmap.Decode(baseImage);
            SKCanvas canvas = new SKCanvas(sKBitmap);
            SKPaint paint = new SKPaint();
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 4; //Larger than normal lines at any zoom level.
            paint.Color = new SKColor(255, 255, 255); //Pure White, for maximum visibility.
            for (var x = 0; x < drawableLine.Length - 1; x++)
                canvas.DrawLine(drawableLine[x], drawableLine[x + 1], paint);

            var ms = new MemoryStream();
            var skms = new SKManagedWStream(ms);
            sKBitmap.Encode(skms, SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            return results;
        }

        public static byte[] DrawCell8V4(GeoArea Cell8, List<StoredWay> drawnItems = null)
        {
            //This should become the standard path as I convert parts of the app to the new system.
            //Once it is, copy that code to this function and remove unnecesssary or redundant parts
            //like calculating degrees per pixel.
            return DrawAreaAtSizeV4(Cell8, 80, 100,  drawnItems);
        }

        public static byte[] DrawAreaAtSizeV4(ImageStats stats, List<StoredWay> drawnItems = null, List<TagParserEntry> styles = null)
        {
            //This should become the standard path as I convert parts of the app to the new system.
            //Once it is, copy that code to this function and remove unnecesssary or redundant parts
            //like calculating degrees per pixel.
            return DrawAreaAtSizeV4(stats.area, stats.imageSizeX, stats.imageSizeY, drawnItems, styles);
        }

        //This generic function takes the area to draw, a size to make the canvas, and then draws it all.
        //Optional parameter allows you to pass in different stuff that the DB alone has, possibly for manual or one-off changes to styling
        //or other elements converted for maptile purposes.
        public static byte[] DrawAreaAtSizeV4(GeoArea relevantArea, int imageSizeX, int imageSizeY, List<StoredWay> drawnItems = null, List<TagParserEntry> styles = null)
        {
            //This is the new core drawing function. Takes in an area, the items to draw, and the size of the image to draw. 
            //The drawn items get their paint pulled from the TagParser's list. If I need multiple match lists, I'll need to make a way
            //to pick which list of tagparser rules to use.

            if (styles == null)
                styles = TagParser.styles;
          
            var db = new PraxisContext();
            var geo = Converters.GeoAreaToPolygon(relevantArea);
            if (drawnItems == null)
                drawnItems = db.StoredWays.Include(c => c.WayTags).Where(w => geo.Intersects(w.wayGeometry)).OrderByDescending(w => w.wayGeometry.Area).ThenByDescending(w => w.wayGeometry.Length).ToList();

            //baseline image data stuff           
            double degreesPerPixelX = relevantArea.LongitudeWidth / imageSizeX;
            double degreesPerPixelY = relevantArea.LatitudeHeight / imageSizeX;
            SKBitmap bitmap = new SKBitmap(imageSizeX, imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            var bgColor = new SKColor();
            SKColor.TryParse("00FFFFFF", out bgColor); //transparent white
            canvas.Clear(bgColor);
            canvas.Scale(1, -1, imageSizeX / 2, imageSizeY / 2);
            SKPaint paint = new SKPaint();

            foreach (var w in drawnItems)
            {
                var tempList = new List<WayTags>();
                if (w.WayTags != null)
                    tempList = w.WayTags.ToList();
                var style = CoreComponents.TagParser.GetStyleForOsmWay(tempList);
                paint = style.paint;
                if (paint.Color.Alpha == 0)
                    continue; //This area is transparent, skip drawing it entirely.

                var path = new SKPath();
                switch (w.wayGeometry.GeometryType)
                {
                    //Polygons without holes are super easy and fast: draw the path.
                    //Polygons with holes require their own bitmap to be drawn correctly and then overlaid onto the canvas.
                    //I want to use paths to fix things for performance reasons, but I have to use Bitmaps because paths apply their blend mode to
                    //ALL elements already drawn, not just the last one.
                    case "Polygon":
                        var p = w.wayGeometry as Polygon;
                        if (p.Holes.Length == 0)
                        {
                            path.AddPoly(Converters.PolygonToSKPoints(p, relevantArea, degreesPerPixelX, degreesPerPixelY));
                            canvas.DrawPath(path, paint);
                        }
                        else
                        {
                            var innerBitmap = DrawPolygon((Polygon)w.wayGeometry, paint, relevantArea, imageSizeX, imageSizeY, degreesPerPixelX, degreesPerPixelY);
                            canvas.DrawBitmap(innerBitmap, 0, 0, paint);
                            //canvas.Save(); //Not sure if this helps or not
                        }
                        break;
                    case "MultiPolygon":
                        foreach (var p2 in ((MultiPolygon)w.wayGeometry).Geometries)
                        {
                            var p2p = p2 as Polygon;
                            if (p2p.Holes.Length == 0)
                            {
                                path.AddPoly(Converters.PolygonToSKPoints(p2p, relevantArea, degreesPerPixelX, degreesPerPixelY));
                                canvas.DrawPath(path, paint);
                            }
                            else
                            {
                                var innerBitmap = DrawPolygon(p2p, paint, relevantArea, imageSizeX, imageSizeY, degreesPerPixelX, degreesPerPixelY);
                                canvas.DrawBitmap(innerBitmap, 0, 0, paint);
                                //canvas.Save(); //Not sure if this helps or not
                            }
                        }
                        break;
                    case "LineString":
                        var firstPoint = w.wayGeometry.Coordinates.First();
                        var lastPoint = w.wayGeometry.Coordinates.Last();
                        var points = Converters.PolygonToSKPoints(w.wayGeometry, relevantArea, degreesPerPixelX, degreesPerPixelY);
                        if (firstPoint.Equals(lastPoint))
                        {
                            //This is a closed shape. Check to see if it's supposed to be filled in.
                            if (paint.Style == SKPaintStyle.Fill)
                            {
                                path.AddPoly(points);
                                canvas.DrawPath(path, paint);
                                continue;
                            }
                        }
                        for (var line = 0; line < points.Length - 1; line++)
                            canvas.DrawLine(points[line], points[line + 1], paint);
                        break;
                    case "MultiLineString":
                        foreach (var p3 in ((MultiLineString)w.wayGeometry).Geometries)
                        {
                            //TODO: might want to see if I need to move the LineString logic here, or if multiline string are never filled areas.
                            var points2 = Converters.PolygonToSKPoints(p3, relevantArea, degreesPerPixelX, degreesPerPixelY);
                            for (var line = 0; line < points2.Length - 1; line++)
                                canvas.DrawLine(points2[line], points2[line + 1], paint);
                        }
                        break;
                    case "Point":
                        var circleRadius = (float)(.000125 / degreesPerPixelX / 2); //I want points to be drawn as 1 Cell10 in diameter.
                        var convertedPoint = Converters.PolygonToSKPoints(w.wayGeometry, relevantArea, degreesPerPixelX, degreesPerPixelY);
                        canvas.DrawCircle(convertedPoint[0], circleRadius, paint);
                        break;
                    default:
                        Log.WriteLog("Unknown geometry type found, not drawn. Element " + w.id);
                        break;
                }
            }

            var ms = new MemoryStream();
            var skms = new SKManagedWStream(ms);
            bitmap.Encode(skms, SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            return results;
        }

        public static SKBitmap DrawPolygon(Polygon polygon, SKPaint paint, GeoArea relevantArea, int imageSizeX, int imageSizeY, double degreesPerPixelX, double degreesPerPixelY)
        {
            //In order to do this the most correct, i have to draw the outer ring, then erase all the innner rings.
            //THEN draw that image overtop the original.
            SKBitmap bitmap = new SKBitmap(imageSizeX, imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            var bgColor = new SKColor();
            SKColor.TryParse("000000000", out bgColor); //Be transparent, not white, for this operation.
            canvas.Clear(bgColor);
            canvas.Scale(1, 1, imageSizeX / 2, imageSizeY / 2); 
            var path = new SKPath();
            path.AddPoly(Converters.PolygonToSKPoints(polygon.ExteriorRing, relevantArea, degreesPerPixelX, degreesPerPixelY));
            canvas.DrawPath(path, paint);

            foreach (var hole in polygon.InteriorRings)
            {
                path = new SKPath();
                path.AddPoly(Converters.PolygonToSKPoints(hole, relevantArea, degreesPerPixelX, degreesPerPixelY));
                canvas.DrawPath(path, eraser);
            }

            return bitmap;
        }
    }
}

