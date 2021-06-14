﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreComponents
{
    public class StandaloneDbTables
    {
        //DB Tables below here
        public class MapTileDB //read-only for the destination app
        {
            public long id { get; set; }
            public string PlusCode { get; set; }
            public byte[] image { get; set; }
            public int layer { get; set; } //I might want to do variant maptiles where each area cliamed adds an overlay to the base map tile, this tracks which stacking order this goes in.

        }

        public class TerrainInfo //read-only for the destination app. writes go to PlusCodesVisited
        {
            public long id { get; set; }
            public string PlusCode { get; set; }
            //public TerrainData terrainData { get; set; }
            public List<TerrainDataSmall> TerrainData { get; set; }
        }

        public class TerrainData //read-only for the destination app. Reduces storage space on big areas.
        {
            public long id { get; set; }
            public string Name { get; set; }
            public string areaType { get; set; } //the game element name
            //These 2 columns are used by MapDataController.LearnCell8, so they stay, even though I'm not using them in the self contained DB.
            public long OsmElementId { get; set; } //Might need to be a long. Might be irrelevant on self-contained DB (except maybe for loading an overlay image on a maptile?)
            public long OsmElementType { get; set; } //Could be unnecessary on the standalone DB.
        }

        public class TerrainDataSmall //read-only for the destination app. As above, but only stores names/area types instead of elements.
        {
            public long id { get; set; }
            public string Name { get; set; }
            public string areaType { get; set; } //the game element name
        }

        public class Bounds //readonly for the destination app
        {
            public int id { get; set; }
            public double NorthBound { get; set; }
            public double SouthBound { get; set; }
            public double EastBound { get; set; }
            public double WestBound { get; set; }

        }

        public class PlusCodesVisited //read-write. has bool for any visit, last visit date, is used for daily/weekly checks, and PaintTheTown mode.
        {
            public int id { get; set; }
            public string PlusCode { get; set; }
            public int visited { get; set; } //0 for false, 1 for true
                                             //Times in Lua are longs, so store that instead of a string
            public long lastVisit { get; set; }
            public long nextDailyBonus { get; set; }
            public long nextWeeklyBonus { get; set; }
        }

        public class PlayerStats //read-write, doesn't leave the device
        {
            public int id { get; set; }
            public int timePlayed { get; set; }
            public double distanceWalked { get; set; }
            public long score { get; set; }
        }

        public class ScavengerHuntStandalone
        {
            public int id { get; set; }
            //public int listId { get; set; } //Which list does this entry appear on. A 
            public string listName { get; set; } //using name as an ID, to avoid needed a separate table thats just ids and names.
            public string description { get; set; } //Defaults to element name on auto-generated lists. Users could make this hints or clues instead.
            public bool playerHasVisited { get; set; } //Single player mode means I can store this inline.
            //public string name { get; set; } //All elements with the same name count. This fixes entries that show up multiple times in a list.
            //public long OsmElementId { get; set; } //Reference to see what this thing is in the source data. Empty for user-created items.
            //public long OsmElementType { get; set; } //as above.

        }
    }
}
