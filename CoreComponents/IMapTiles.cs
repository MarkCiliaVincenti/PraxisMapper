﻿using Google.OpenLocationCode;
using PraxisCore.Support;
using System.Collections.Generic;
using static PraxisCore.DbTables;

namespace PraxisCore
{
    public interface IMapTiles
    {
        public static int SlippyTileSizeSquare;
        public static double GameTileScale;
        public static double BufferSize;

        public void Initialize(); //Replaces some TagParser stuff, since a lot of drawing optimization happened there.
        public byte[] DrawOfflineEstimatedAreas(ImageStats info, List<DbTables.Place> items);
        public byte[] DrawCell8GridLines(GeoArea totalArea);
        public byte[] DrawCell10GridLines(GeoArea totalArea);
        public byte[] DrawUserPath(string pointListAsString);
        public byte[] DrawAreaAtSize(ImageStats stats, List<DbTables.Place> drawnItems = null, string styleSet = "mapTiles", bool filterSmallAreas = true);
        public byte[] DrawAreaAtSize(ImageStats stats, List<CompletePaintOp> paintOps);
        public string DrawAreaAtSizeSVG(ImageStats stats, List<DbTables.Place> drawnItems = null, Dictionary<string, StyleEntry> styles = null, bool filterSmallAreas = true);
        public byte[] LayerTiles(ImageStats info, byte[] bottomTile, byte[] topTile);
    }
}
