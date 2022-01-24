﻿using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static PraxisCore.ConstantValues;
using static PraxisCore.DbTables;
using static PraxisCore.Place;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using PraxisCore.Support;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;

namespace PraxisCore
{
    /// <summary>
    /// All functions related to generating or expiring map tiles. Both PlusCode sized tiles for gameplay or SlippyMap tiles for a webview.
    /// </summary>
    public class MapTiles : IMapTiles
    {
        public int MapTileSizeSquare = 512; //Default value, updated by PraxisMapper at startup. COvers Slippy tiles, not gameplay tiles.
        public double GameTileScale = 2;
        public double bufferSize = resolutionCell10; //How much space to add to an area to make sure elements are drawn correctly. Mostly to stop points from being clipped.
        //static SKPaint eraser = new SKPaint() { Color = SKColors.Transparent, BlendMode = SKBlendMode.Src, Style = SKPaintStyle.StrokeAndFill }; //BlendMode is the important part for an Eraser.
        static Random r = new Random();
        public static Dictionary<string, Image> cachedBitmaps = new Dictionary<string, Image>(); //Icons for points separate from pattern fills, though I suspect if I made a pattern fill with the same size as the icon I wouldn't need this.

        public void Initialize()
        {
            //TODO: replace TagParser optimizations here.
        }

        /// <summary>
        /// Draw square boxes around each area to approximate how they would behave in an offline app
        /// </summary>
        /// <param name="info">the image information for drawing</param>
        /// <param name="items">the elements to draw.</param>
        /// <returns>byte array of the generated .png tile image</returns>
        public byte[] DrawOfflineEstimatedAreas(ImageStats info, List<StoredOsmElement> items)
        {
            //TODO retest this.
            var image = new Image<Rgba32>(info.imageSizeX, info.imageSizeY);
            var bgColor = Rgba32.ParseHex("00000000");
            image.Mutate(x => x.Fill(bgColor));
            var fillColor = Rgba32.ParseHex("000000");
            var strokeColor = Rgba32.ParseHex("000000");

            var placeInfo = PraxisCore.Standalone.Standalone.GetPlaceInfo(items.Where(i =>
            i.IsGameElement
            ).ToList());

            //this is for rectangles.
            foreach (var pi in placeInfo)
            {
                var rect = PlaceInfoToRect(pi, info);
                fillColor = Rgba32.ParseHex(TagParser.PickStaticColorForArea(pi.Name));
                image.Mutate(x => x.Fill(fillColor, rect));
                image.Mutate(x => x.Draw(strokeColor, 3, rect));
            }

            image.Mutate(x => x.Flip(FlipMode.Vertical)); ; //inverts the inverted image again!
            foreach (var pi in placeInfo)
            {
                //NOTE: would be better to load fonts once and share that for the app's lifetime.
                var fonts = new SixLabors.Fonts.FontCollection();
                var family = fonts.Install("fontHere.ttf");
                var font = family.CreateFont(12, FontStyle.Regular);
                var rect = PlaceInfoToRect(pi, info);
                image.Mutate(x => x.DrawText(pi.Name, font, strokeColor, new PointF((float)(pi.lonCenter * info.pixelsPerDegreeX), (float)(pi.latCenter * info.pixelsPerDegreeY))));
            }

            image.Mutate(x => x.Flip(FlipMode.Vertical)); //Plus codes are south-to-north, so invert the image to make it correct.
            var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Draws grid lines to match boundaries for 8 character PlusCodes.
        /// </summary>
        /// <param name="totalArea">the GeoArea to draw lines in</param>
        /// <returns>the byte array for the maptile png file</returns>
        public byte[] DrawCell8GridLines(GeoArea totalArea)
        {
            int imageSizeX = MapTileSizeSquare;
            int imageSizeY = MapTileSizeSquare;
            Image<Rgba32> image = new Image<Rgba32>(imageSizeX, imageSizeY);
            var bgColor = Rgba32.ParseHex("00000000");
            image.Mutate(x => x.Fill(bgColor));

            var lineColor = Rgba32.ParseHex("FF0000");
            var StrokeWidth = 3;

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
                var points = PolygonToDrawingLine(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
                image.Mutate(x => x.Draw(lineColor, StrokeWidth, new SixLabors.ImageSharp.Drawing.Path(points)));
                lonLineTrackerDegrees += resolutionCell8;
            }

            double latLineTrackerDegrees = imageBottom - spaceToFirstLineBottom; //This is degree coords
            while (latLineTrackerDegrees <= totalArea.NorthLatitude + resolutionCell8) //This means we should always draw at least 2 lines, even if they're off-canvas.
            {
                var geoLine = new LineString(new Coordinate[] { new Coordinate(180, latLineTrackerDegrees), new Coordinate(-180, latLineTrackerDegrees) });
                var points = PolygonToDrawingLine(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
                image.Mutate(x => x.Draw(lineColor, StrokeWidth, new SixLabors.ImageSharp.Drawing.Path(points)));
                latLineTrackerDegrees += resolutionCell8;
            }

            image.Mutate(x => x.Flip(FlipMode.Vertical)); //Plus codes are south-to-north, so invert the image to make it correct.
            var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Draws grid lines to match boundaries for 10 character PlusCodes.
        /// </summary>
        /// <param name="totalArea">the GeoArea to draw lines in</param>
        /// <returns>the byte array for the maptile png file</returns>
        public byte[] DrawCell10GridLines(GeoArea totalArea)
        {

            int imageSizeX = MapTileSizeSquare;
            int imageSizeY = MapTileSizeSquare;
            Image<Rgba32> image = new Image<Rgba32>(imageSizeX, imageSizeY);
            var bgColor = Rgba32.ParseHex("00000000");
            image.Mutate(x => x.Fill(bgColor));

            var lineColor = Rgba32.ParseHex("00CCFF");
            var StrokeWidth = 1;

            double degreesPerPixelX = totalArea.LongitudeWidth / imageSizeX;
            double degreesPerPixelY = totalArea.LatitudeHeight / imageSizeY;

            //This is hardcoded to Cell 10 spaced gridlines.
            var imageLeft = totalArea.WestLongitude;
            var spaceToFirstLineLeft = (imageLeft % resolutionCell10);

            var imageBottom = totalArea.SouthLatitude;
            var spaceToFirstLineBottom = (imageBottom % resolutionCell10);

            double lonLineTrackerDegrees = imageLeft - spaceToFirstLineLeft; //This is degree coords
            while (lonLineTrackerDegrees <= totalArea.EastLongitude + resolutionCell10) //This means we should always draw at least 2 lines, even if they're off-canvas.
            {
                var geoLine = new LineString(new Coordinate[] { new Coordinate(lonLineTrackerDegrees, 90), new Coordinate(lonLineTrackerDegrees, -90) });
                var points = PolygonToDrawingLine(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
                image.Mutate(x => x.Draw(lineColor, StrokeWidth, new SixLabors.ImageSharp.Drawing.Path(points)));
                lonLineTrackerDegrees += resolutionCell10;
            }

            double latLineTrackerDegrees = imageBottom - spaceToFirstLineBottom; //This is degree coords
            while (latLineTrackerDegrees <= totalArea.NorthLatitude + resolutionCell10) //This means we should always draw at least 2 lines, even if they're off-canvas.
            {
                var geoLine = new LineString(new Coordinate[] { new Coordinate(180, latLineTrackerDegrees), new Coordinate(-180, latLineTrackerDegrees) });
                var points = PolygonToDrawingLine(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
                image.Mutate(x => x.Draw(lineColor, StrokeWidth, new SixLabors.ImageSharp.Drawing.Path(points)));
                latLineTrackerDegrees += resolutionCell10;
            }

            image.Mutate(x => x.Flip(FlipMode.Vertical)); //Plus codes are south-to-north, so invert the image to make it correct.
            var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Take a path provided by a user, draw it as a maptile. Potentially useful for exercise trackers. Resulting file must not be saved to the server as that would be user tracking.
        /// </summary>
        /// <param name="pointListAsString">a string of points separate by , and | </param>
        /// <returns>the png file with the path drawn over the mapdata in the area.</returns>
        public byte[] DrawUserPath(string pointListAsString)
        {
            //String is formatted as Lat,Lon~Lat,Lon~ repeating. Characters chosen to not be percent-encoded if submitted as part of the URL.
            //first, convert this to a list of latlon points
            string[] pointToConvert = pointListAsString.Split("|");
            List<Coordinate> coords = pointToConvert.Select(p => new Coordinate(double.Parse(p.Split(',')[0]), double.Parse(p.Split(',')[1]))).ToList();

            var mapBuffer = resolutionCell8 / 2; //Leave some area around the edges of where they went.
            GeoArea mapToDraw = new GeoArea(coords.Min(c => c.Y) - mapBuffer, coords.Min(c => c.X) - mapBuffer, coords.Max(c => c.Y) + mapBuffer, coords.Max(c => c.X) + mapBuffer);

            ImageStats info = new ImageStats(mapToDraw, 1024, 1024);

            LineString line = new LineString(coords.ToArray());
            var drawableLine = PolygonToDrawingLine(line, mapToDraw, info.degreesPerPixelX, info.degreesPerPixelY);

            //Now, draw that path on the map.
            var places = GetPlaces(mapToDraw); //, null, false, false, degreesPerPixelX * 4 ///TODO: restore item filtering
            var baseImage = DrawAreaAtSize(info, places);

            Image<Rgba32> image = new Image<Rgba32>(info.imageSizeX, info.imageSizeY);
            Rgba32 strokeColor = Rgba32.ParseHex("000000");
            image.Mutate(x => x.Draw(strokeColor, 4, new SixLabors.ImageSharp.Drawing.Path(drawableLine)));
            
            image.Mutate(x => x.Flip(FlipMode.Vertical)); //Plus codes are south-to-north, so invert the image to make it correct.
            var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        }

        //Optional parameter allows you to pass in different stuff that the DB alone has, possibly for manual or one-off changes to styling
        //or other elements converted for maptile purposes.
        /// <summary>
        /// //This generic function takes the area to draw and creates an image for it. Can optionally be provided specific elements, a specific style set, and told to filter small areas out of the results.
        /// </summary>
        /// <param name="stats">Image information, including width and height.</param>
        /// <param name="drawnItems">the elments to draw</param>
        /// <param name="styleSet">the style rules to use when drawing</param>
        /// <param name="filterSmallAreas">if true, removes elements from the drawing that take up fewer than 8 pixels.</param>
        /// <returns></returns>
        public byte[] DrawAreaAtSize(ImageStats stats, List<StoredOsmElement> drawnItems = null, string styleSet = "mapTiles", bool filterSmallAreas = true)
        {
            //This is the new core drawing function. Takes in an area, the items to draw, and the size of the image to draw. 
            //The drawn items get their paint pulled from the TagParser's list. If I need multiple match lists, I'll need to make a way
            //to pick which list of tagparser rules to use.
            //This can work for user data by using the linked StoredOsmElements from the items in CustomDataStoredElement.
            //I need a slightly different function for using CustomDataPlusCode, or another optional parameter here

            //This should just get hte paint ops then call the core drawing function.
            double minimumSize = 0;
            if (filterSmallAreas)
            {
                minimumSize = stats.degreesPerPixelX * 8; //don't draw small elements. THis runs on perimeter/length
            }

            //Single points are excluded separately so that small areas or lines can still be drawn when points aren't.
            bool includePoints = true;
            if (stats.degreesPerPixelX > ConstantValues.zoom14DegPerPixelX)
                includePoints = false;

            if (drawnItems == null)
                drawnItems = GetPlaces(stats.area, filterSize: minimumSize, includePoints: includePoints);

            var paintOps = MapTileSupport.GetPaintOpsForStoredElements(drawnItems, styleSet, stats);
            return DrawAreaAtSize(stats, paintOps);
        }

        public byte[] DrawAreaAtSize(ImageStats stats, List<CompletePaintOp> paintOps)
        {
            //THIS is the core drawing function, and other version should call this so there's 1 function that handles the inner loop.
            //baseline image data stuff           
            var image = new Image<Rgba32>(stats.imageSizeX, stats.imageSizeY);
            foreach (var w in paintOps.OrderByDescending(p => p.paintOp.layerId).ThenByDescending(p => p.areaSize))
            {
                string htmlColor = w.paintOp.HtmlColorCode;
                if (htmlColor.Length == 8)
                    htmlColor = htmlColor.Substring(2, 6) + htmlColor.Substring(0, 2);
                var color = Rgba32.ParseHex(htmlColor);
                if (color.A == 0)
                    continue; //This area is transparent, skip drawing it entirely.

                //NOTE TODO: 
                //paintOps should be setting the linewidth to be thick enough to paint, but it looks like it isn't.
                //and that seems to be why lines aren't drawing here, they're 3* 10^-5 pixels thick or so.
                //but that value works fine in SkiaSharp for some reason?

                //ImageSharp is just not fast enough on big areas, so we have to crop them down here. Maybe. Causes other issues.
                //var thisGeometry = w.elementGeometry.Intersection(Converters.GeoAreaToPolygon(stats.area)); //for testing performance improvements. Causes other issues.
                var thisGeometry = w.elementGeometry; //For testing at regular speed. 

                //Potential logic fix:
                //for geometries, clamp all points to the current tile's bounds
                //if its the same location as the previous point after clamping, remove it from the list of points.
                //Or try and be clever: if any point is beyond the tile bounds in 2 directions, set 1 point at the corner (or just outside of it)
                //and erase all points beyond it?
                
                switch (thisGeometry.GeometryType)
                {
                    case "Polygon":
                        //after trimming this might not work out as well. Don't draw broken/partial polygons? or only as lines?
                        if (thisGeometry.Coordinates.Length > 2)
                        {
                            var drawThis = PolygonToDrawingPolygon(thisGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            if (w.paintOp.FillOrStroke == "fill")
                                image.Mutate(x => x.Fill(color, drawThis));
                            else
                                image.Mutate(x => x.Draw(color, w.paintOp.LineWidth, drawThis));
                        }
                        break;
                    case "MultiPolygon":
                        var lines = new List<LinearLineSegment>();
                        foreach (var p2 in ((MultiPolygon)thisGeometry).Geometries)
                        {
                            var drawThis2 = PolygonToDrawingPolygon(p2, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            if (w.paintOp.FillOrStroke == "fill")
                                image.Mutate(x => x.Fill(color, drawThis2));
                            else
                                image.Mutate(x => x.Draw(color, w.paintOp.LineWidth, drawThis2));


                            //var drawThis1 = PolygonToDrawingLine(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            //lines.Add(drawThis1);

                            //foreach (var hole in ((NetTopologySuite.Geometries.Polygon)p2).InteriorRings)
                            //{
                            //    var drawthis2 = PolygonToDrawingLine(hole);
                            //    lineSegmentList.Add(drawthis2);
                            //}
                        }
                        //var mp = new SixLabors.ImageSharp.Drawing.Polygon(lines);
                        //var drawOpts = new DrawingOptions();
                        //drawOpts.ShapeOptions.IntersectionRule = IntersectionRule.OddEven;
                        //TODO: check this, consider switching shape options to OddEven if it doesn't draw holes right?
                        //if (w.paintOp.FillOrStroke == "fill")
                            //image.Mutate(x => x.Fill(drawOpts, color, mp));
                        //else
                            //image.Mutate(x => x.Draw(color, w.paintOp.LineWidth, mp));
                        break;
                    case "LineString":
                        var firstPoint = thisGeometry.Coordinates.First();
                        var lastPoint = thisGeometry.Coordinates.Last();
                        var line = LineToDrawingLine(thisGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);

                        if (firstPoint.Equals(lastPoint) && w.paintOp.FillOrStroke == "fill")
                            image.Mutate(x => x.Fill(color, new SixLabors.ImageSharp.Drawing.Polygon(new LinearLineSegment(line))));
                        else
                            image.Mutate(x => x.DrawLines(color, w.paintOp.LineWidth, line));
                        break;
                    case "MultiLineString":
                        foreach (var p3 in ((MultiLineString)thisGeometry).Geometries)
                        {
                            var line2 = LineToDrawingLine(p3, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            image.Mutate(x => x.DrawLines(color, w.paintOp.LineWidth, line2));
                        }
                        break;
                    case "Point":
                        var convertedPoint = PointToPointF(thisGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        //If this type has an icon, use it. Otherwise draw a circle in that type's color.
                        if (!string.IsNullOrEmpty(w.paintOp.fileName))
                        {
                            //this needs another Image TODO work out icon logic for ImageSharp.
                            Image i2 = Image.Load(w.paintOp.fileName);
                            //image.Mutate(x => x.DrawImage(w.paintOp.fileName, 1);
                            //TODO bitmaps on bitmaps in ImageSharp
                            //SKBitmap icon = TagParser.cachedBitmaps[w.paintOp.fileName];
                            //canvas.DrawBitmap(icon, convertedPoint[0]);
                        }
                        else
                        {
                            var circleRadius = (float)(ConstantValues.resolutionCell10 / stats.degreesPerPixelX / 2); //I want points to be drawn as 1 Cell10 in diameter.
                            var shape = new SixLabors.ImageSharp.Drawing.EllipsePolygon(
                                PointToPointF(thisGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY),
                                new SizeF(circleRadius, circleRadius));
                            image.Mutate(x => x.Fill(color, shape));
                            image.Mutate(x => x.Draw(Color.Black, 1, shape)); //TODO: double check colors and sizes for outlines
                        }
                        break;
                    default:
                        Log.WriteLog("Unknown geometry type found, not drawn.");
                        break;
                }
            }

            image.Mutate(x => x.Flip(FlipMode.Vertical)); //Plus codes are south-to-north, so invert the image to make it correct.
            var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// ImageSharp doesn't support this at all. Throws NotImplementedException when called.
        /// </summary>
        public string DrawAreaAtSizeSVG(ImageStats stats, List<StoredOsmElement> drawnItems = null, Dictionary<string, TagParserEntry> styles = null, bool filterSmallAreas = true)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Draws 1 tile overtop the other.
        /// </summary>
        /// <param name="info">Unused in this implementation, but requires per the interface.</param>
        /// <param name="bottomTile">the tile to use as the base of the image. Expected to be opaque.</param>
        /// <param name="topTile">The tile to layer on top. Expected to be at least partly transparent or translucent.</param>
        /// <returns></returns>
        public byte[] LayerTiles(ImageStats info, byte[] bottomTile, byte[] topTile)
        {
            Image i1 = Image.Load(bottomTile);
            Image i2 = Image.Load(topTile);

            i1.Mutate(x => x.DrawImage(i2, 1));
            var ms = new MemoryStream();
            i1.SaveAsPng(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Get the background color from a style set
        /// </summary>
        /// <param name="styleSet">the name of the style set to pull the background color from</param>
        /// <returns>the Rgba32 saved into the requested background paint object.</returns>
        public Rgba32 GetStyleBgColorString(string styleSet)
        {
            var color = Rgba32.ParseHex(TagParser.allStyleGroups[styleSet]["background"].paintOperations.First().HtmlColorCode);
            return color;
        }

        public SixLabors.ImageSharp.Drawing.Polygon PolygonToDrawingPolygon(Geometry place, GeoArea drawingArea, double resolutionX, double resolutionY)
        {
            var lineSegmentList = new List<LinearLineSegment>();
            NetTopologySuite.Geometries.Polygon p = (NetTopologySuite.Geometries.Polygon)place;
            var typeConvertedPoints = p.ExteriorRing.Coordinates.Select(o => new SixLabors.ImageSharp.PointF((float)((o.X - drawingArea.WestLongitude) * (1 / resolutionX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / resolutionY))));
            //SixLabors.ImageSharp.Drawing.LinearLineSegment part = new SixLabors.ImageSharp.Drawing.LinearLineSegment(typeConvertedPoints.ToArray());
            lineSegmentList.Add(new LinearLineSegment(typeConvertedPoints.ToArray()));

            foreach (var hole in p.InteriorRings)
            {
                typeConvertedPoints = p.ExteriorRing.Coordinates.Select(o => new SixLabors.ImageSharp.PointF((float)((o.X - drawingArea.WestLongitude) * (1 / resolutionX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / resolutionY))));
                lineSegmentList.Add(new LinearLineSegment(typeConvertedPoints.ToArray()));
            }

            var output = new SixLabors.ImageSharp.Drawing.Polygon(lineSegmentList);
            return output;
        }

        public SixLabors.ImageSharp.Drawing.LinearLineSegment PolygonToDrawingLine(Geometry place, GeoArea drawingArea, double resolutionX, double resolutionY)
        {
            //TODO: does this handle holes nicely? It should if I add them to the path.
            var typeConvertedPoints = place.Coordinates.Select(o => new SixLabors.ImageSharp.PointF((float)((o.X - drawingArea.WestLongitude) * (1 / resolutionX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / resolutionY))));
            SixLabors.ImageSharp.Drawing.LinearLineSegment part = new SixLabors.ImageSharp.Drawing.LinearLineSegment(typeConvertedPoints.ToArray());
            return part;
        }

        public SixLabors.ImageSharp.PointF[] LineToDrawingLine(Geometry place, GeoArea drawingArea, double resolutionX, double resolutionY)
        {
            var typeConvertedPoints = place.Coordinates.Select(o => new SixLabors.ImageSharp.PointF((float)((o.X - drawingArea.WestLongitude) * (1 / resolutionX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / resolutionY)))).ToList();
            return typeConvertedPoints.ToArray();
        }

        public SixLabors.ImageSharp.PointF PointToPointF(Geometry place, GeoArea drawingArea, double resolutionX, double resolutionY)
        {
            var coord = place.Coordinate;
            return new SixLabors.ImageSharp.PointF((float)((coord.X - drawingArea.WestLongitude) * (1 / resolutionX)), (float)((coord.Y - drawingArea.SouthLatitude) * (1 / resolutionY)));
        }

        public Rectangle PlaceInfoToRect(PraxisCore.StandaloneDbTables.PlaceInfo2 pi, ImageStats info)
        {
            //TODO test this.
            Rectangle r = new Rectangle();
            float heightMod = (float)pi.height / 2;
            float widthMod = (float)pi.width / 2;
            r.Width = (int)(pi.width * info.pixelsPerDegreeX);
            r.Height = (int)(pi.height * info.pixelsPerDegreeY);
            r.X = (int)(pi.lonCenter * info.pixelsPerDegreeX);
            r.Y = (int)(pi.latCenter * info.pixelsPerDegreeY);

            return r;
        }
    }
}
