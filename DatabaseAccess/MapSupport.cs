﻿using Google.OpenLocationCode;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DatabaseAccess.DbTables;
using OsmSharp;
using DatabaseAccess.Support;
using OsmSharp.Tags;
using NetTopologySuite.Operation.Buffer;
using Microsoft.EntityFrameworkCore;

namespace DatabaseAccess
{
    public static class MapSupport
    {
        //TODO: define a real purpose and location for this stuff.
        //Right now, this is mostly 'functions/consts I want to refer to in multiple projects'

        //TODO:
        //set up a command line parameter for OsmXmlParser to extract certain types of value from files (so different users could pull different features out to use)
        //continue renmaing and reorganizing things.
        //make PerformanceInfo tracking a toggle.

        public const double resolution10 = .000125; //the size of a 10-digit PlusCode, in degrees.
        public const double resolution8 = .0025; //the size of a 8-digit PlusCode, in degrees.
        public const double resolution6 = .05; //the size of a 6-digit PlusCode, in degrees.
        public const double resolution4 = 1; //the size of a 4-digit PlusCode, in degrees.
        public const double resolution2 = 20; //the size of a 2-digit PlusCode, in degrees.

        //public static List<string> relevantTags = new List<string>() { "name", "natural", "leisure", "landuse", "amenity", "tourism", "historic", "highway", "boundary" }; //The keys in tags we process to see if we want it included.
        public static List<string> relevantTourismValues = new List<string>() { "artwork", "attraction", "gallery", "museum", "viewpoint", "zoo" }; //The stuff we care about in the tourism category. Zoo and attraction are debatable.
        public static List<string> relevantHighwayValues = new List<string>() { "path", "bridleway", "cycleway", "footway" }; //The stuff we care about in the highway category. Still pulls in plain sidewalks with no additional tags fairly often.

        public static GeometryFactory factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values. Still pending thread-safety testing.

        public static List<AreaType> areaTypes = new List<AreaType>() {
            new AreaType() { AreaTypeId = 1, AreaName = "water", OsmTags = "" },
            new AreaType() { AreaTypeId = 2, AreaName = "wetland", OsmTags = "" },
            new AreaType() { AreaTypeId = 3, AreaName = "park", OsmTags = "" },
            new AreaType() { AreaTypeId = 4, AreaName = "beach", OsmTags = "" },
            new AreaType() { AreaTypeId = 5, AreaName = "university", OsmTags = "" },
            new AreaType() { AreaTypeId = 6, AreaName = "natureReserve", OsmTags = "" },
            new AreaType() { AreaTypeId = 7, AreaName = "cemetery", OsmTags = "" },
            //new AreaType() { AreaTypeId = 8, AreaName = "mall", OsmTags = "" },
            new AreaType() { AreaTypeId = 9, AreaName = "retail", OsmTags = "" },
            new AreaType() { AreaTypeId = 10, AreaName = "tourism", OsmTags = "" },
            new AreaType() { AreaTypeId = 11, AreaName = "historical", OsmTags = "" },
            new AreaType() { AreaTypeId = 12, AreaName = "trail", OsmTags = "" },
            new AreaType() { AreaTypeId = 13, AreaName = "admin", OsmTags = "" }
        };

        public static ILookup<string, int> areaTypeReference = areaTypes.ToLookup(k => k.AreaName, v => v.AreaTypeId);
        public static ILookup<int, string> areaIdReference = areaTypes.ToLookup(k => k.AreaTypeId, v => v.AreaName);

        public static List<MapData> GetPlaces(GeoArea area, List<MapData> source = null)
        {
            //TODO: this seems to have a lot of warmup time that I would like to get rid of. Would be a huge performance improvement.
            //The flexible core of the lookup functions. Takes an area, returns results that intersect from Source. If source is null, looks into the DB.
            //Intersects is the only indexable function on a geography column I would want here. Distance and Equals can also use the index, but I don't need those in this app.
            var coordSeq = MakeBox(area);
            var location = factory.CreatePolygon(coordSeq);
            List<MapData> places;
            if (source == null)
            {
                var db = new DatabaseAccess.GpsExploreContext();
                places = db.MapData.Where(md => md.place.Intersects(location)).ToList();
            }
            else
                places = source.Where(md => md.place.Intersects(location)).ToList();
            return places;
        }

        public static List<MapData> GetPlaces(Point point, List<MapData> source = null)
        {
            //NOTE: Intersects doesn't seem to work here, but neither does anything else?
            //I get back the same list of 3 entries without names.
            List<MapData> places;
            if (source == null)
            {
                var db = new DatabaseAccess.GpsExploreContext();
                places = db.MapData.Where(md => md.place.Intersects(point)).ToList();
            }
            else
                places = source.Where(md => md.place.Contains(point)).ToList();
            return places;
        }

        public static Coordinate[] MakeBox(GeoArea plusCodeArea)
        {
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
            var cord1 = new Coordinate(plusCodeArea.Min.Longitude, plusCodeArea.Min.Latitude);
            var cord2 = new Coordinate(plusCodeArea.Min.Longitude, plusCodeArea.Max.Latitude);
            var cord3 = new Coordinate(plusCodeArea.Max.Longitude, plusCodeArea.Max.Latitude);
            var cord4 = new Coordinate(plusCodeArea.Max.Longitude, plusCodeArea.Min.Latitude);
            var cordSeq = new Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };

            return cordSeq;
        }

        public static void SplitArea(GeoArea area, int divideCount, List<MapData> places, out List<MapData>[] placeArray, out GeoArea[] areaArray)
        {
            //Take area, divide it into a divideCount * divideCount grid of area. Return matching arrays of MapData and GeoArea, with indexes that correspond 1:1
            //The purpose of this function is to reduce code involved in optimizing a search, and to make it more flexible to test 
            //performance improvements on area splits.

            placeArray = new List<MapData>[1];
            areaArray = new GeoArea[1];

            if (divideCount == 0 || divideCount == 1)
            {
                placeArray[0] = places;
                areaArray[0] = area;
                return;
            }

            var latDivider = area.LatitudeHeight / divideCount;
            var lonDivider = area.LongitudeWidth / divideCount;

            List<List<MapData>> resultsPlace = new List<List<MapData>>();
            List<GeoArea> resultsArea = new List<GeoArea>();
            resultsArea.Capacity = divideCount * divideCount;
            resultsPlace.Capacity = divideCount * divideCount;

            for (var x = 0; x < divideCount; x++)
            {
                for (var y = 0; y < divideCount; y++)
                {
                    var box = new GeoArea(area.SouthLatitude + (latDivider * y), 
                        area.WestLongitude + (lonDivider * x), 
                        area.SouthLatitude + (latDivider * y) + latDivider, 
                        area.WestLongitude + (lonDivider * x) + lonDivider);
                    resultsPlace.Add(GetPlaces(box, places));
                    resultsArea.Add(box);
                }
            }

            placeArray = resultsPlace.ToArray();
            areaArray = resultsArea.ToArray();

            return;
        }

        public static StringBuilder SearchArea(ref GeoArea area, ref List<MapData> mapData, bool entireCode = false)
        {
            StringBuilder sb = new StringBuilder();
            if (mapData.Count() == 0)
                return sb;

            var xCells = area.LongitudeWidth / resolution10;
            var yCells = area.LatitudeHeight / resolution10;

            for (double xx = 0; xx < xCells; xx += 1)
            {
                for (double yy = 0; yy < yCells; yy += 1)
                {
                    double x = area.Min.Longitude + (resolution10 * xx);
                    double y = area.Min.Latitude + (resolution10 * yy);

                    var placesFound = MapSupport.FindPlacesIn10Cell(x, y, ref mapData, entireCode);
                    if (!string.IsNullOrWhiteSpace(placesFound))
                        sb.AppendLine(placesFound);
                }
            }

            return sb;
        }

        public static string FindPlacesIn10Cell(double x, double y, ref List<MapData> places, bool entireCode = false)
        {
            var box = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolution10, x + resolution10));
            var entriesHere = MapSupport.GetPlaces(box, places).Where(p => !p.type.StartsWith("admin")).ToList(); //Excluding admin boundaries from this list.  

            if (entriesHere.Count() == 0)
            {
                return "";
            }
            else
            {
                //New sorting rules:
                //If there's only one place, take it without any additional queries. Otherwise:
                //if there's a Point in the mapdata list, take the first one (No additional sub-sorting applied yet)
                //else if there's a Line in the mapdata list, take the first one (no additional sub-sorting applied yet)
                //else if there's polygonal areas here, take the smallest one by area 
                //(In general, the smaller areas should be overlaid on larger areas. This is more accurate than guessing by area types which one should be applied)
                string olc;
                if (entireCode)
                    olc = new OpenLocationCode(y, x).CodeDigits;
                else
                    olc = new OpenLocationCode(y, x).CodeDigits.Substring(6, 4); //This takes lat, long, Coordinate takes X, Y. This line is correct.
                
                if (entriesHere.Count() == 1)
                    return olc + "|" + entriesHere.First().name + "|" + entriesHere.First().type;

                var point = entriesHere.Where(e => e.place.GeometryType == "Point").FirstOrDefault();
                if (point != null)
                    return olc + "|" + point.name + "|" + point.type;

                var line = entriesHere.Where(e => e.place.GeometryType == "LineString" || e.place.GeometryType == "MultiLineString").FirstOrDefault();
                if (line != null)
                    return olc + "|" + line.name + "|" + line.type;

                var smallest = entriesHere.Where(e => e.place.GeometryType == "Polygon" || e.place.GeometryType == "MultiPolygon").OrderBy(e => e.place.Area).First();
                return olc + "|" + smallest.name + "|" + smallest.type;
            }
        }

        public static string LoadDataOnArea(long id)
        {
            //Debugging helper call. Loads up some information on an area and display it.
            var db = new GpsExploreContext();
            var entries = db.MapData.Where(m => m.WayId == id || m.RelationId == id || m.NodeId == id).ToList();
            string results = "";
            foreach (var entry in entries)
            {
                var shape = entry.place;

                results += "Name: " + entry.name + Environment.NewLine;
                results += "Game Type: " + entry.type + Environment.NewLine;
                results += "Geometry Type: " + shape.GeometryType + Environment.NewLine;
                results += "IsValid? : " + shape.IsValid + Environment.NewLine;
                results += "Area: " + shape.Area + Environment.NewLine; //Not documented, but I believe this is the area in square degrees. Is that a real unit?
                results += "As Text: " + shape.AsText() + Environment.NewLine;
            }

            return results;
        }

        public static MapData ConvertWayToMapData(ref WayData w)
        {

            if (w.name == "" && w.AreaType == "")
                return null;

            //Take a single tagged Way, and make it a usable MapData entry for the app.
            MapData md = new MapData();
            md.name = w.name;
            md.WayId = w.id;
            md.type = w.AreaType;
            //md.AreaTypeId = MapSupport.areaTypes.Where(a => a.AreaName == md.type).First().AreaTypeId;

            //Adding support for LineStrings. A lot of rivers/streams/footpaths are treated this way.
            if (w.nds.First().id != w.nds.Last().id)
            {
                LineString temp2 = factory.CreateLineString(w.nds.Select(n => new Coordinate(n.lon, n.lat)).ToArray());
                md.place = temp2;
            }
            else
            {
                if (w.nds.Count <= 3)
                {
                    Log.WriteLog("Way " + w.id + " doesn't have enough nodes to parse into a Polygon. This entry is an awkward line, not processing.");
                    return null;
                }

                Polygon temp = factory.CreatePolygon(w.nds.Select(n => new Coordinate(n.lon, n.lat)).ToArray());
                if (!temp.Shell.IsCCW)
                {
                    temp = (Polygon)temp.Reverse();
                    if (!temp.Shell.IsCCW)
                    {
                        Log.WriteLog("Way " + w.id + " needs more work to be parsable, it's not counter-clockwise forward or reversed.");
                        return null;
                    }
                    if (!temp.IsValid)
                    {
                        Log.WriteLog("Way " + w.id + " needs more work to be parsable, it's not valid according to its own internal check.");
                        return null;
                    }
                }
                md.place = temp;
                md.WayId = w.id;
            }
            w = null;
            return md;

        }

        public static MapData ConvertNodeToMapData(NodeReference n)
        {
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
            return new MapData()
            {
                name = n.name,
                type = n.type,
                place = factory.CreatePoint(new Coordinate(n.lon, n.lat)),
                NodeId = n.Id,
                //AreaTypeId = MapSupport.areaTypes.Where(a => a.AreaName == n.type).First().AreaTypeId
            };
        }

        public static string GetElementName(TagsCollectionBase tags)
        {
            string name = tags.Where(t => t.Key == "name").FirstOrDefault().Value;
            if (name == null || name == "")
                //some things have a Note rather than a Name. Use that as a backup.
                name = tags.Where(t => t.Key == "note").FirstOrDefault().Value;
            if (name == null)
                return "";
            return name;
        }

        public static int GetTypeId(TagsCollectionBase tags)
        {
            //gets the ID associated with a type.
            string results = GetType(tags);
            if (results.StartsWith("admin")) //all admin levels are treated as the same type
                return areaTypeReference["admin"].First();

            return areaTypeReference[results].First();
        }

        public static string GetType(TagsCollectionBase tagsO)
        {
            //This is how we will figure out which area a cell counts as now.
            //Should make sure all of these exist in the AreaTypes table I made.
            //REMEMBER: this list needs to match the same tags as the ones in GetWays(relations)FromPBF or else data looks weird.
            //TODO: optimize this function, searching  the array 20 times in the worst-case scenario isn't good for big files. Takes an hour for a 7GB file.

            var tags = tagsO.ToLookup(k => k.Key, v => v.Value);

            if (tags.Count() == 0)
                return ""; //Sanity check

            //Entries are currently sorted by rough frequency of occurrence in my home area.

            //Water spaces should be displayed. Not sure if I want players to be in them for resources.
            //Water should probably override other values as a safety concern?
            if (ParserSettings.processWater && tags["natural"].Any(v => v == "water") || tags["waterway"].Count() > 0)
                return "water";

            //Trail. Will likely show up in varying places for various reasons. Trying to limit this down to hiking trails like in parks and similar.
            //In my local park, i see both path and footway used (Footway is an unpaved trail, Path is a paved one)
            //highway=track is for tractors and other vehicles.  Don't include. that.
            //highway=path is non-motor vehicle, and otherwise very generic. 5% of all Ways in OSM.
            //highway=footway is pedestrian traffic only, maybe including bikes. Mostly sidewalks, which I dont' want to include.
            //highway=bridleway is horse paths, maybe including pedestrians and bikes
            //highway=cycleway is for bikes, maybe including pedesterians.
            if (ParserSettings.processTrail && tags["highway"].Any(v => relevantHighwayValues.Contains(v) 
                && !tags["footway"].Any(v => v == "sidewalk" || v == "crossing")))
                return "trail";

            //Parks are good. Possibly core to this game.
            if (ParserSettings.processPark && tags["leisure"].Any(v => v == "park"))
                return "park";

            //admin boundaries: identify what political entities you're in. Smaller numbers are bigger levels (countries), bigger numbers are smaller entries (states, counties, cities, neighborhoods)
            //This should be the lowest priority tag on a cell, since this is probably the least interesting piece of info you could know.
            //OSM Wiki has more info on which ones mean what and where they're used.
            if (ParserSettings.processAdmin && tags["boundary"].Any(v => v == "administrative")) //Admin_level appears on other elements, including goverment-tagged stuff, capitals, etc.
            {
                string level = tags["admin_level"].FirstOrDefault();
                if (level != null)
                    return "admin" + level.ToInt();
                return "admin0"; //indicates relation wasn't tagged with a level.
            }

            //Cemetaries are ok. They don't seem to appreciate Pokemon Go, but they're a public space and often encourage activity in them (thats not PoGo)
            if (ParserSettings.processCemetery && (tags["landuse"].Any(v => v == "cemetery") 
                || tags["amenity"].Any(v => v == "grave_yard")))
                return "cemetery";

            //Generic shopping area is ok. I don't want to advertise businesses, but this is a common area type.
            //TODO: expand this to buildings, not just landuse?
            //Landuse=retail has 200k entries. building=retail has 500k entries.
            //shop=* has about 5 million entries, mostly nodes.
            //Malls should be merged here, and are a sub-set of shop entries
            if (ParserSettings.processRetail && (tags["landuse"].Any(v => v == "retail")
                || tags["building"].Any(v => v == "retail") 
                || tags["shop"].Count() > 0)) // mall is a value of shop, so those are included here now.
                return "retail";

            //I have historical as a tag to save, but not necessarily sub-sets yet of whats interesting there.
            //NOTE: the OSM tag doesn't match my value
            if (ParserSettings.processHistorical && tags["historic"].Count() > 0)
                return "historical";

            //Wetlands should also be high priority.
            if (ParserSettings.processWetland && tags["natural"].Any(v => v == "wetland"))
                return "wetland";

            //Nature Reserve. Should be included
            if (ParserSettings.processNatureReserve && tags["leisure"].Any(v => v== "nature_reserve"))
                return "natureReserve";

            //I have tourism as a tag to save, but not necessarily sub-sets yet of whats interesting there.
            if (ParserSettings.processTourism && tags["tourism"].Any(v => relevantTourismValues.Contains(v)))
                return "tourism"; //TODO: create sub-values for tourism types?

            //Universities are good. Primary schools are not so good.  Don't include all education values.
            if (ParserSettings.processUniversity && tags["amenity"].Any(v => v == "university" || v =="college"))
                return "university";

            //Beaches are good. Managed beaches are less good but I'll count those too.
            if (ParserSettings.processBeach && tags["natural"].Any(v => v == "beach")
            || (tags["leisure"].Any(v => v  == "beach_resort")))
                return "beach";

            //Possibly of interest:
            //landuse:forest / landuse:orchard  / natural:wood
            //natural:sand may be of interest for desert area?
            //natural:spring / natural:hot_spring
            //amenity:theatre for plays/music/etc (amenity:cinema is a movie theater)
            //Anything else seems un-interesting or irrelevant.

            return ""; //not a way we need to save right now.
        }

        public static Coordinate[] WayToCoordArray(Support.WayData w)
        {
            if (w == null)
                return null;

            List<Coordinate> results = new List<Coordinate>();

            foreach (var node in w.nds)
                results.Add(new Coordinate(node.lon, node.lat));

            return results.ToArray();
        }

        public static string GetPlusCode(double lat, double lon)
        {
            return OpenLocationCode.Encode(lat, lon).Replace("+", "");
        }

        public static CoordPair GetRandomPoint()
        {

            //Global scale testing.
            Random r = new Random();
            double lat = 90 * r.NextDouble() * (r.Next() % 2 == 0 ? 1 : -1);
            double lon = 180 * r.NextDouble() * (r.Next() % 2 == 0 ? 1 : -1);
            return new CoordPair(lat, lon);
        }

        public static CoordPair GetRandomBoundedPoint()
        {
            //randomize lat and long to roughly somewhere in Ohio. For testing a limited geographic area.
            //42, -80 NE
            //38, -84 SW
            //so 38 + (0-4), -84 = (0-4) coords.
            Random r = new Random();
            double lat = 38 + (r.NextDouble() * 4);
            double lon = -84 + (r.NextDouble() * 4);
            return new CoordPair(lat, lon);
        }

        public static void InsertAreaTypes()
        {
            var db = new GpsExploreContext();
            db.Database.BeginTransaction();
            db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT AreaTypes ON;");
            db.AreaTypes.AddRange(areaTypes);
            db.SaveChanges();
            db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT dbo.AreaTypes OFF;");
            db.Database.CommitTransaction();
        }

        public static Geometry SimplifyArea(Geometry place)
        {
            var simplerPlace = NetTopologySuite.Simplify.TopologyPreservingSimplifier.Simplify(place, resolution10); //i can't identify details below this in the server
            return simplerPlace;
        }
    }
}
