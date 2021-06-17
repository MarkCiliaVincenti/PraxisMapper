﻿using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using OsmSharp.Tags;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using static CoreComponents.DbTables;
using static CoreComponents.Singletons;

namespace CoreComponents
{
    public static class TagParser
    {
        public static List<TagParserEntry> styles; //For drawing maptiles
        public static List<TagParserEntry> teams; //For doing Area Control tiles.
        public static TagParserEntry defaultStyle; //background color must be last if I want un-matched areas to be hidden, its own color if i want areas with no ways at all to show up.
        public static TagParserEntry defaultTeam; //background color must be last if I want un-matched areas to be hidden, its own color if i want areas with no ways at all to show up.

        public static void Initialize(bool onlyDefaults = false)
        {
            //Load TPE entries from DB for app.
            var db = new PraxisContext();
            styles = db.TagParserEntries.Include(t => t.TagParserMatchRules).ToList();
            if (onlyDefaults || styles == null || styles.Count() == 0)
                styles = Singletons.defaultTagParserEntries;

            foreach (var s in styles)
                SetPaintForTPE(s);

            defaultStyle = styles.Last();

            //TODO: load team data from DB. Either its own table set or add a parameter to the main TagParserEntries table to indicate its purpose.
            teams = Singletons.defaultTeamColors;
            foreach (var t in teams)
                SetPaintForTPE(t);
        }

        public static void SetPaintForTPE(TagParserEntry tpe)
        {
            var paint = new SKPaint();
            //TODO: enable a style to use static-random colors.
            
            paint.Color = SKColor.Parse(tpe.HtmlColorCode);
            if (tpe.FillOrStroke == "fill")
                paint.Style = SKPaintStyle.StrokeAndFill;
            else
                paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = tpe.LineWidth;
            if (tpe.LinePattern != "solid")
            {
                float[] linesAndGaps = tpe.LinePattern.Split('|').Select(t => float.Parse(t)).ToArray();
                paint.PathEffect = SKPathEffect.CreateDash(linesAndGaps, 0);
                paint.StrokeCap = SKStrokeCap.Butt;
            }
            paint.StrokeJoin = SKStrokeJoin.Round;
            paint.IsAntialias = true;
            tpe.paint = paint;
        }

        public static TagParserEntry GetStyleForOsmWay(List<ElementTags> tags)
    {
            if (tags == null || tags.Count() == 0)
                return defaultStyle;
            
            foreach (var drawingRules in styles)
            {
                if (MatchOnTags(drawingRules, tags))
                    return drawingRules;
            }
            
            return defaultStyle;
        }

        public static TagParserEntry GetStyleForOsmWay(TagsCollectionBase tags)
        {
            var tempTags = tags.Select(t => new ElementTags() { Key = t.Key, Value = t.Value }).ToList();
            return GetStyleForOsmWay(tempTags);
        }

        public static string GetAreaType(TagsCollectionBase tags)
        {
            var tempTags = tags.Select(t => new ElementTags() { Key = t.Key, Value = t.Value }).ToList();
            return GetAreaType(tempTags);
        }
        public static string GetAreaType(List<ElementTags> tags)
        {
            if (tags == null || tags.Count() == 0)
                return defaultStyle.name;

            foreach (var drawingRules in styles)
                if (MatchOnTags(drawingRules, tags))
                    return drawingRules.name;

            return defaultStyle.name;
        }

        public static bool MatchOnTags(TagParserEntry tpe, StoredOsmElement sw)
        {
            return MatchOnTags(tpe, sw.Tags.ToList());
        }
        
        public static bool MatchOnTags(TagParserEntry tpe, List<ElementTags> tags)
        {
            //Changing this to return as soon as any entry fails makes it run about twice as fast.
            bool OrMatched = false;
            int orRuleCount = 0;

            //Step 1: check all the rules against these tags.
            //The * value is required for all the rules, so check it first.
            for (var i = 0; i < tpe.TagParserMatchRules.Count(); i++)
            {
                var entry = tpe.TagParserMatchRules.ElementAt(i);
                if (entry.Value == "*") //The Key needs to exist, but any value counts.
                {
                    if (tags.Any(t => t.Key == entry.Key))
                        continue;
                }

                switch (entry.MatchType)
                {
                    case "any":
                        if (!tags.Any(t => t.Key == entry.Key))
                            return false;

                        var possibleValues = entry.Value.Split("|");
                        var actualValue = tags.Where(t => t.Key == entry.Key).Select(t => t.Value).FirstOrDefault();
                        if (!possibleValues.Contains(actualValue))
                            return false;
                        break;
                    case "or": //Or rules don't fail early, since only one of them needs to match. Otherwise is the same as ANY logic.
                        orRuleCount++;
                        if (!tags.Any(t => t.Key == entry.Key))
                            continue;

                        var possibleValuesOr = entry.Value.Split("|");
                        var actualValueOr = tags.Where(t => t.Key == entry.Key).Select(t => t.Value).FirstOrDefault();
                        if (possibleValuesOr.Contains(actualValueOr))
                            OrMatched = true;
                        break;
                    case "not":
                        if (!tags.Any(t => t.Key == entry.Key))
                            continue;

                        var possibleValuesNot = entry.Value.Split("|");
                        var actualValueNot = tags.Where(t => t.Key == entry.Key).Select(t => t.Value).FirstOrDefault();
                        if (possibleValuesNot.Contains(actualValueNot))
                            return false; //Not does not want to match this.
                        break;
                    case "equals": //for single possible values, EQUALS is slightly faster than ANY
                        if (!tags.Any(t => t.Key == entry.Key))
                            return false;
                        if (tags.Where(t => t.Key == entry.Key).Select(t => t.Value).FirstOrDefault() != entry.Value)
                            return false;
                        break;
                    case "none":
                        //never matches anything. Useful for background color or other special styles that need to exist but don't want to appear normally.
                        return false;
                    case "default":
                        //Always matches. Can only be on one entry, which is the last entry and the default color
                        return true;
                }
            }

            //Now, we should have bailed out if any mandatory thing didn't match. Should only be on whether or not any of our Or checks passsed.
            if (OrMatched || orRuleCount == 0)
                return true;

            return false;
        }

        //public static void UpdateDbForStyleChange() //Unused, but potentially important if I am saving geometry formats based on the default tag results. If I'm doing that, i should stop.
        //{
        //    var db = new PraxisContext();
        //    foreach (var sw in db.StoredOsmElements)
        //    {
        //        var paintStyle = GetStyleForOsmWay(sw);
        //        if (sw.elementGeometry.GeometryType == "LinearRing" && paintStyle.paint.Style == SKPaintStyle.Fill)
        //        {
        //            var poly = factory.CreatePolygon((LinearRing)sw.elementGeometry);
        //            sw.elementGeometry = poly;
        //        }
        //    }
        //}

        public static List<ElementTags> getFilteredTags(TagsCollectionBase rawTags)
        {
            return rawTags.Where(t =>
                t.Key != "source" &&
                !t.Key.StartsWith("addr:") &&
                !t.Key.StartsWith("alt_name:") &&
                !t.Key.StartsWith("brand") &&
                !t.Key.StartsWith("building:") &&
                !t.Key.StartsWith("change:") &&
                !t.Key.StartsWith("contact:") &&
                !t.Key.StartsWith("created_by") &&
                !t.Key.StartsWith("demolished:") &&
                !t.Key.StartsWith("destination:") &&
                !t.Key.StartsWith("disused:") &&
                !t.Key.StartsWith("email") &&
                !t.Key.StartsWith("fax") &&
                !t.Key.StartsWith("FIXME") &&
                !t.Key.StartsWith("generator:") &&
                !t.Key.StartsWith("gnis:") &&
                !t.Key.StartsWith("hgv:") &&
                !t.Key.StartsWith("import_uuid") &&
                !t.Key.StartsWith("is_in") &&
                !t.Key.StartsWith("junction:") &&
                !t.Key.StartsWith("maxspeed") &&
                !t.Key.StartsWith("mtb:") &&
                !t.Key.StartsWith("nist:") &&
                !t.Key.StartsWith("not:") &&
                !t.Key.StartsWith("old_name:") &&
                !t.Key.StartsWith("parking:") &&
                !t.Key.StartsWith("payment:") &&
                !t.Key.StartsWith("phone") &&
                !t.Key.StartsWith("name:") &&
                !t.Key.StartsWith("recycling:") &&
                !t.Key.StartsWith("ref:") &&
                !t.Key.StartsWith("reg_name:") &&
                !t.Key.StartsWith("roof:") &&
                !t.Key.StartsWith("source:") &&
                !t.Key.StartsWith("subject:") &&
                !t.Key.StartsWith("telephone") &&
                !t.Key.StartsWith("tiger:") &&
                !t.Key.StartsWith("turn:") &&
                !t.Key.StartsWith("was:") &&
                !t.Key.StartsWith("website") 
                )
                .Select(t => new ElementTags() { Key = t.Key, Value = t.Value }).ToList();
        }

        public static string GetPlaceName(TagsCollectionBase tagsO) //Should this be part of TagParser? Probably.
        {
            if (tagsO.Count() == 0)
                return "";
            var retVal = tagsO.GetValue("name");
            if (retVal == null)
                retVal = "";

            return retVal;
        }

        public static string GetWikipediaLink(StoredOsmElement element)
        {
            var wikiTag = element.Tags.Where(t => t.Key == "wikipedia").FirstOrDefault();
            if (wikiTag == null)
                return "";

            string[] splitValue = wikiTag.Value.Split(":");
            return "https://" + splitValue[0] + ".wikipedia.org/wiki/" + splitValue[1]; //TODO: check if special characters need replaced or encoded on this.
        }

        public static List<StoredOsmElement> ApplyTags(List<StoredOsmElement> places)
        {
            foreach (var p in places)
            {
                var style = GetStyleForOsmWay(p.Tags.ToList());
                p.GameElementName = style.name;
                p.IsGameElement = style.IsGameElement;
            }
            return places;
        }

        public static SKColor PickStaticColorForArea(string areaname)
        {
            var hasher = System.Security.Cryptography.MD5.Create();
            var value = areaname.ToByteArrayUnicode();
            var hash = hasher.ComputeHash(value);

            SKColor results = new SKColor(hash[0], hash[1], hash[2], Convert.ToByte(32)); //all have the same transparency level
            return results;
        }
    }
}
