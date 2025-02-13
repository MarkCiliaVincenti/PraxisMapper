﻿using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using System;
using System.Collections.Generic;
using static PraxisCore.DbTables;

namespace PraxisCore
{
    public static class Singletons
    {
        public static GeometryFactory factory = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
        public static PreparedGeometryFactory pgf = new PreparedGeometryFactory();
        public static bool SimplifyAreas = false;

        /// <summary>
        /// The predefined list of shapes to generate areas in. 4 basic geometric shapes.
        /// </summary>
        public static List<List<Coordinate>> possibleShapes = new List<List<Coordinate>>() //When generating gameplay areas in empty Cell8s
        {
            new List<Coordinate>() { new Coordinate(0, 0), new Coordinate(.5, 1), new Coordinate(1, 0)}, //triangle.
            new List<Coordinate>() { new Coordinate(0, 0), new Coordinate(0, 1), new Coordinate(1, 1), new Coordinate(1, 0) }, //square.
            new List<Coordinate>() { new Coordinate(.2, 0), new Coordinate(0, .8), new Coordinate(.5, 1), new Coordinate(1, .8), new Coordinate(.8, 0) }, //roughly a pentagon.
            new List<Coordinate>() { new Coordinate(.5, 0), new Coordinate(0, .33), new Coordinate(0, .66), new Coordinate(.5, 1), new Coordinate(1, .66), new Coordinate(1, .33) }, //roughly a hexagon.
            //TODO: more shapes, ideally more interesting than simple polygons? Star? Heart? Arc?

        };

        //A * value is a wildcard that means any value counts as a match for that key. If the tag exists, its value is irrelevant. Cannot be used in NOT checks.
        //A * key is a rule that will not match based on tag values, as no key will == *. Used for backgrounds and special styles called up by name.
        //types vary:
        //any: one of the pipe delimited values in the value is present for the given key.
        //equals: the one specific value exists on that key. Slightly faster than Any when matching a single key, but trivially so.
        //or: as long as one of the rules with or is true, accept this entry as a match. each Or entry should be treated as its own 'any' for parsing purposes.
        //not: this rule must be FALSE for the style to be applied.
        //default: always true.
        //New Layer Rules:
        //Roads 90-99
        //default content: 100

        /// <summary>
        /// The baseline set of TagParser styles. 
        /// <list type="bullet">
        /// <item><description>mapTiles: The baseline map tile style, based on OSMCarto</description></item>
        /// <item><description>teamColor: 3 predefined styles to allow for Red(1), Green(2) and Blue(3) teams in a game. Set a tag to the color's ID and then call a DrawCustomX function with this style.</description></item>
        /// <item><description>paintTown: A simple styleset that pulls the color to use from the tag provided.</description></item>
        /// <item><description>adminBounds: Draws only admin boundaries, all levels supported but names match USA's common usage of them.</description></item>
        /// <item><description>outlines: Draws a black border outline for all elements.</description></item>
        /// <item><description>suggestedGameplay: Bolder colored overlay for places that are probably OK for public interaction.</description></item>
        /// </list>
        /// </summary>
        public static List<StyleEntry> defaultStyleEntries = new List<StyleEntry>()
        {

            //NOTE:
            //Drawing order rules: bigger numbers are drawn first == smaller numbers get drawn over bigger numbers for LayerId. Smaller areas will be drawn after (on top of) larger areas in the same layer.
            //10000: unmatched (transparent)
            //9999: background (always present, added into the draw list by processing.)
            //100: MOST area elements.
            //99: tertiary top-layer, area element outlines
            //98: tertiary bottom-layer
            //97: secondary top, tree top
            //96: secondary bottom, tree bottom
            //95: primary top
            //94: primary bottom
            //93: motorway top
            //92: motorway bottom
            //70: admin boundaries, if they weren't transparent.


            //NOTE: a rough analysis suggests that about 1/3 of entries are 'tertiary' roads and another third are 'building'
            //BUT buildings can often be marked with another interesting property, like retail, tourism, or historical, and I'd prefer those match first.
            //So I will re-order this to handle tertiary first, then the gameElements that might be buildings, then plain buildings, and the remaining elements after that.
            //This should mean that 60%+ of elements match in 6 checks or less. 
            //MapTiles: Roads of varying sizes and colors to match OSM colors
            new StyleEntry() { MatchOrder = 10, Name ="tertiary", StyleSet = "mapTiles", //This is MatchOrde 1 because its one of the most common entries, is the correct answer 30% of the time.
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "ffffff", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 98, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                    new StylePaint() { HtmlColorCode = "8f8f8f", FillOrStroke = "stroke", LineWidthDegrees=0.0000375F, LinePattern= "solid", LayerId = 99, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "tertiary|unclassified|residential|tertiary_link|service|road", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 20, Name ="university", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "FFFFE5", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "university|college", MatchType = "any" }}
            },
            new StyleEntry() { MatchOrder = 30, Name ="retail", StyleSet = "mapTiles", IsGameElement = true,
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "FFD4CE", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "retail|commercial", MatchType = "or"},
                    new StyleMatchRule() {Key="building", Value="retail|commercial", MatchType="or" },
                    new StyleMatchRule() {Key="shop", Value="*", MatchType="or" }
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 40, Name ="tourism", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "660033", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "*", MatchType = "equals" }}
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 49, Name ="wreck", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "A52A2A", FileName="Wreck.png", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "historic", Value = "wreck", MatchType = "equals" }}
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 50, Name ="historical", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "B3B3B3", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "historic", Value = "*", MatchType = "equals" }}
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 60, Name ="artsCulture", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "theatre|concert hall|arts centre|planetarium", MatchType = "or" }} //TODO: expand this. Might need to swap order with tourism to catch several other entries.
            },
            new StyleEntry() { MatchOrder = 69, Name ="namedBuilding", StyleSet = "mapTiles", IsGameElement = true,
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "d1b6a1", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "806b5b", FillOrStroke = "stroke", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "building", Value = "*", MatchType = "equals" },
                    new StyleMatchRule() { Key = "name", Value = "*", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 70, Name ="building", StyleSet = "mapTiles", //NOTE: making this matchOrder=2 makes map tiles draw faster, but hides some gameplay-element colors.
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "d9d0c9", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "B8A89C", FillOrStroke = "stroke", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "building", Value = "*", MatchType = "equals" }} },
            new StyleEntry() { MatchOrder = 80, Name ="water", StyleSet = "mapTiles", IsGameElement = true,
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "aad3df", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "natural", Value = "water|strait|bay", MatchType = "or"}, //Coastline intentionally removed, as those are saved to the water polygon shapefiles and it's much more reasonable to simply import those and tag them as natural=water
                    new StyleMatchRule() {Key = "waterway", Value ="*", MatchType="or" },
                    new StyleMatchRule() {Key = "landuse", Value ="basin", MatchType="or" },
                    new StyleMatchRule() {Key = "leisure", Value ="swimming_pool", MatchType="or" },
                    new StyleMatchRule() {Key = "place", Value ="sea", MatchType="or" }, //stupid Labrador sea value.
                }},
            new StyleEntry() {IsGameElement = true,  MatchOrder = 90, Name ="wetland", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "0C4026", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                 StyleMatchRules = new List<StyleMatchRule>() {
                     new StyleMatchRule() { Key = "natural", Value = "wetland", MatchType = "equals" }
                 }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 100, Name ="park", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "C8FACC", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "leisure", Value = "park", MatchType = "or" },
                    new StyleMatchRule() { Key = "leisure", Value = "playground", MatchType = "or" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 110, Name ="beach", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "fff1ba", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "natural", Value = "beach|shoal", MatchType = "or" },
                    new StyleMatchRule() {Key = "leisure", Value="beach_resort", MatchType="or"}
            } },

            new StyleEntry() { IsGameElement = true, MatchOrder = 120, Name ="natureReserve", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "124504", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "leisure", Value = "nature_reserve", MatchType = "equals" }}
            },
            new StyleEntry() {IsGameElement = true, MatchOrder = 130, Name ="cemetery", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "AACBAF", FillOrStroke = "fill", FileName="Landuse-cemetery.png", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "cemetery", MatchType = "or" },
                    new StyleMatchRule() {Key="amenity", Value="grave_yard", MatchType="or" } }
            },
            new StyleEntry() { MatchOrder = 140, Name ="trailFilled", StyleSet = "mapTiles", IsGameElement = false, //This exists to make the map look correct, but these are so few removing them as game elements should not impact games.
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "F0E68C", FillOrStroke = "fill", LineWidthDegrees=0.000025F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key="highway", Value="path|bridleway|cycleway|footway|living_street", MatchType="any"},
                    new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
                    new StyleMatchRule() { Key="area", Value="yes", MatchType="equals"}
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 150, Name ="trail", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "F0E68C", FillOrStroke = "stroke", LineWidthDegrees=0.000025F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom14DegPerPixelX }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key="highway", Value="path|bridleway|cycleway|footway|living_street", MatchType="any"},
                    new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"}
            }},
            //Admin bounds are transparent on mapTiles, because I would prefer to find and skip them on this style. Use the adminBounds style to draw them on their own tile layer.
            new StyleEntry() { MatchOrder = 160, Name ="admin", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00FF2020", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "10|5", LayerId = 70 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" }}
            },
            new StyleEntry() { MatchOrder = 170, Name ="parking", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "EEEEEE", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MinDrawRes = ConstantValues.zoom12DegPerPixelX}
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "parking", MatchType = "equals" }}
            },
            //New generic entries for mapping by color
            new StyleEntry() { MatchOrder = 180, Name ="grass",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "cdebb0", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "grass|meadow|recreation_ground|village_green", MatchType = "or" },
                    new StyleMatchRule() { Key = "natural", Value = "heath|grassland", MatchType = "or" },
                    new StyleMatchRule() { Key = "leisure", Value = "garden", MatchType = "or" },
                    new StyleMatchRule() { Key = "golf", Value = "tee|fairway|driving_range", MatchType = "or" },
            }},
            new StyleEntry() { MatchOrder = 190, Name ="sandColored",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "D7B526", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "natural", Value = "shingle|dune|scree", MatchType = "or" },
                    new StyleMatchRule() { Key = "surface", Value = "sand", MatchType = "or" }
            }},
            new StyleEntry() { MatchOrder = 200, Name ="forest",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "ADD19E", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "natural", Value = "wood", MatchType = "or" },
                    new StyleMatchRule() { Key = "landuse", Value = "forest", MatchType = "or" },
            }},
            new StyleEntry() { MatchOrder = 210, Name ="industrial",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "EBDBE8", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "industrial", MatchType = "equals" },
            }},
            new StyleEntry() { MatchOrder = 220, Name ="residential",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "009933", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "residential", MatchType = "equals" },
            }},
            new StyleEntry() { MatchOrder = 230, Name ="sidewalk", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "C0C0C0", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "highway", Value = "pedestrian", MatchType = "equals" },
            }},
            //Transparent: we don't usually want to draw census boundaries
            new StyleEntry() { MatchOrder = 240, Name ="censusbounds",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "census", MatchType = "equals" },
            }},
            //Transparents: Explicitly things that don't help when drawn in one color.
            new StyleEntry() { MatchOrder = 250, Name ="donotdraw",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "place", Value = "locality|islet", MatchType = "any" },
            }},
            new StyleEntry() { MatchOrder = 260, Name ="greyFill",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "AAAAAA", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "man_made", Value = "breakwater", MatchType = "any" },
            }},

            //Roads of varying sizes and colors to match OSM colors
            new StyleEntry() { MatchOrder = 270, Name ="motorwayFilled", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "e892a2", FillOrStroke = "fill", LineWidthDegrees=0.000125F, LinePattern= "solid", LayerId = 92},
                    new StylePaint() { HtmlColorCode = "dc2a67", FillOrStroke = "fill", LineWidthDegrees=0.000155F, LinePattern= "solid", LayerId = 93}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "motorway|trunk|motorway_link|trunk_link", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
                new StyleMatchRule() { Key="area", Value="yes", MatchType="equals"}
            }},
            //Roads of varying sizes and colors to match OSM colors
            new StyleEntry() { MatchOrder = 280, Name ="motorway", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "e892a2", FillOrStroke = "fill", LineWidthDegrees=0.000125F, LinePattern= "solid", LayerId = 92},
                    new StylePaint() { HtmlColorCode = "dc2a67", FillOrStroke = "fill", LineWidthDegrees=0.000155F, LinePattern= "solid", LayerId = 93}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "motorway|trunk|motorway_link|trunk_link", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
            }},
            new StyleEntry() { MatchOrder = 290, Name ="primaryFilled", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "fcd6a4", FillOrStroke = "fill", LineWidthDegrees=0.000025F, LinePattern= "solid", LayerId = 94, },
                    new StylePaint() { HtmlColorCode = "a06b00", FillOrStroke = "fill", LineWidthDegrees=0.00004275F, LinePattern= "solid", LayerId = 95, }
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "primary|primary_link", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
                new StyleMatchRule() { Key="area", Value="yes", MatchType="equals"}
            }},
            //Roads of varying sizes and colors to match OSM colors
            new StyleEntry() { MatchOrder = 300, Name ="primary", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "fcd6a4", FillOrStroke = "stroke", LineWidthDegrees=0.00005F, LinePattern= "solid", LayerId = 94, MaxDrawRes = ConstantValues.zoom6DegPerPixelX /2 },
                    new StylePaint() { HtmlColorCode = "a06b00", FillOrStroke = "stroke", LineWidthDegrees=0.000085F, LinePattern= "solid", LayerId = 95, MaxDrawRes = ConstantValues.zoom6DegPerPixelX /2}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "primary|primary_link", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
            }},
            new StyleEntry() { MatchOrder = 310, Name ="secondaryFilled",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "f7fabf", FillOrStroke = "fill", LineWidthDegrees=0.0000375F, LinePattern= "solid", LayerId = 96, MaxDrawRes = ConstantValues.zoom8DegPerPixelX,},
                    new StylePaint() { HtmlColorCode = "707d05", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 97, MaxDrawRes = ConstantValues.zoom8DegPerPixelX,}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "secondary|secondary_link", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
                new StyleMatchRule() { Key="area", Value="yes", MatchType="equals"}
            }},
            //Roads of varying sizes and colors to match OSM colors
            new StyleEntry() { MatchOrder = 320, Name ="secondary",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "f7fabf", FillOrStroke = "stroke", LineWidthDegrees=0.0000375F, LinePattern= "solid", LayerId = 96, MaxDrawRes = ConstantValues.zoom8DegPerPixelX,},
                    new StylePaint() { HtmlColorCode = "707d05", FillOrStroke = "stroke", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 97, MaxDrawRes = ConstantValues.zoom8DegPerPixelX,}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "secondary|secondary_link", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
            }},
            new StyleEntry() { MatchOrder = 330, Name ="tertiaryFilled", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "ffffff", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 98, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                    new StylePaint() { HtmlColorCode = "8f8f8f", FillOrStroke = "fill", LineWidthDegrees=0.0000375F, LinePattern= "solid", LayerId = 99, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "tertiary|unclassified|residential|tertiary_link|service|road", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
                new StyleMatchRule() { Key="area", Value="yes", MatchType="equals"}
            }},
            //New in Release 8: Additional styles for more accurate map tiles.
            new StyleEntry() { MatchOrder = 340, Name ="tree", StyleSet = "mapTiles", //Trees are transparent, so they blend the color of what's under thenm.
                PaintOperations = new List<StylePaint>() { //TODO: consider scaling to 32x32px at zoom 20. Do the math to conver that to degrees.
                    new StylePaint() { HtmlColorCode = "42aedfa3", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 97, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                    new StylePaint() { HtmlColorCode = "42a52a2a", FillOrStroke = "fill", LineWidthDegrees=0.000001425F, LinePattern= "solid", LayerId = 96, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="natural", Value="tree", MatchType="equals"}
            }},
            new StyleEntry() { MatchOrder = 350, Name ="landfill", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "b6b592", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="landuse", Value="landfill", MatchType="equals"}
            }},
            new StyleEntry() { MatchOrder = 360, Name ="farmland", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "eef0d5", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="landuse", Value="farmland", MatchType="or"},
                new StyleMatchRule() { Key="landuse", Value="greenhouse_horticulture", MatchType="or"},
            }},
            new StyleEntry() { MatchOrder = 370, Name ="farmyard", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "f5dcba", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="landuse", Value="farmyard", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 380, Name ="scrub", StyleSet = "mapTiles", //TODO: import pattern.
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "c8d7ab", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="natural", Value="scrub", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 390, Name ="garages", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "dfddce", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="landuse", Value="garages", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 400, Name ="military", StyleSet = "mapTiles", //TODO import pattern.
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "ff5555", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="landuse", Value="military", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 410, Name ="sportspitch", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "aae0cb", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="leisure", Value="pitch|track", MatchType="any"},
            }},
            new StyleEntry() { MatchOrder = 420, Name ="golfgreen", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "aae0cb", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="golf", Value="green", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 430, Name ="golfcourse", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "def6c0", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="leisure", Value="golf_course|miniature_golf", MatchType="any"},
            }},
            new StyleEntry() { MatchOrder = 440, Name ="stadium", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "dffce2", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="leisure", Value="sports_centre|stadium", MatchType="or"},
                new StyleMatchRule() { Key="landuse", Value="recreation_ground", MatchType="or"},
            }},
            new StyleEntry() { MatchOrder = 440, Name ="railway", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "ffc0cb", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="landuse", Value="railway", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 450, Name ="airportapron", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "dbdbe1", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="aeroway", Value="apron", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 451, Name ="airportrunway", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "bbbbcc", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="aeroway", Value="runway|taxiway|helipad", MatchType="equals"}, //TODO: helipad is the same color, but has a symbol.
            }},
            new StyleEntry() { MatchOrder = 453, Name ="airporterminal", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "c5b7ad", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="aeroway", Value="terminal", MatchType="or"},
                new StyleMatchRule() { Key="building", Value="train_station", MatchType="or"},
                new StyleMatchRule() { Key="aerialway", Value="station", MatchType="or"},
                new StyleMatchRule() { Key="public_transport", Value="station", MatchType="or"},
            }},
            new StyleEntry() { MatchOrder = 460, Name ="raceway", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "ffc0cb", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="highway", Value="raceway", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 470, Name ="placeofworship", StyleSet = "mapTiles", //TODO more before building style entry.
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "d0d0d0", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="amenity", Value="place_of_worship", MatchType="or"},
                new StyleMatchRule() { Key="landuse", Value="religious", MatchType="or"},
            }},
            new StyleEntry() { MatchOrder = 480, Name ="camping", StyleSet = "mapTiles", //TODO: move before tourism entry.
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "def6c0", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="tourism", Value="camp_site|caravan_site", MatchType="any"},
            }},
            new StyleEntry() { MatchOrder = 490, Name ="heath", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "d6d99f", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="natural", Value="heath", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 500, Name ="sand", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "f5e9c6", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="natural", Value="sand", MatchType="or"},
                new StyleMatchRule() { Key="golf", Value="bunker", MatchType="or"},
            }},
            new StyleEntry() { MatchOrder = 510, Name ="glacier", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "ddecec", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="natural", Value="glacier", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 520, Name ="aerodrome", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "e9e7e2", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="aeroway", Value="aerodrome", MatchType="or"},
                new StyleMatchRule() { Key="amenity", Value="ferry_terminal", MatchType="or"},
                new StyleMatchRule() { Key="amenity", Value="bus_station", MatchType="or"},
            }},
            new StyleEntry() { MatchOrder = 530, Name ="restarea", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "f9c6c6", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
                {
                    new StyleMatchRule() { Key="highway", Value="services|rest_area", MatchType="any"},
             }},
            new StyleEntry() { MatchOrder = 540, Name ="bridge", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "f9c6c6", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
                {
                    new StyleMatchRule() { Key="man_made", Value="bridge", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 550, Name ="power", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "f9c6c6", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
                {
                    new StyleMatchRule() { Key="power", Value="generator|substation|plant", MatchType="any"},
            }},
            new StyleEntry() { MatchOrder = 560, Name ="tree_row", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "99add19e", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
                {
                    new StyleMatchRule() { Key="natural", Value="tree_row", MatchType="equals"},
            }}, 
            new StyleEntry() { MatchOrder = 570, Name ="orchard", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "aedfa3", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
                {
                    new StyleMatchRule() { Key="landuse", Value="orchard|vineyard|plant_nursery", MatchType="any"},
            }},
            new StyleEntry() { MatchOrder = 570, Name ="allotments", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "c9e1bf", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
                {
                    new StyleMatchRule() { Key="landuse", Value="allotments", MatchType="any"},
            }},




            //additional areas that I can work out info on go here, though they may not necessarily be the most interesting areas to handle.
            //single color terrains:
            //manmade:clearcut: grass (requires forest to be darker green), but this also isnt mapped on OSMCarto

            //patterns to work in: (colors present)
            //orchard
            //golf rough.
            //military
            //military danger zone



            //NOTE: hiding elements of a given type is done by drawing those elements in a transparent color
            //My default set wants to draw things that haven't yet been identified, so I can see what needs improvement or matched by a rule.
            new StyleEntry() { MatchOrder = 9999, Name ="background",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "F2EFE9", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "*", Value = "*", MatchType = "none" }} },

            new StyleEntry() { MatchOrder = 10000, Name ="unmatched",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "*", Value = "*", MatchType = "default" }}
            },

            //TODO: these need their sizes adjusted to be wider. Right now, in degrees, the overlap is nearly invisible unless you're at zoom 20.
            //More specific admin-bounds tags, named matching USA values for now.
            new StyleEntry() { MatchOrder = 1, Name ="country",  StyleSet = "adminBounds",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "E31010", FillOrStroke = "stroke", LineWidthDegrees=0.000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "2", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 2, Name ="region",  StyleSet = "adminBounds", //dot pattern
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC8A58", FillOrStroke = "stroke", LineWidthDegrees=0.0001125F, LinePattern= "10|10", LayerId = 90 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "3", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 3, Name ="state",   StyleSet = "adminBounds", //dot pattern
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "FFE30D", FillOrStroke = "stroke", LineWidthDegrees=0.0001F, LinePattern= "10|10", LayerId = 80 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "4", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 4, Name ="admin5", StyleSet = "adminBounds", //Line pattern is dash-dot-dot
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "369670", FillOrStroke = "stroke", LineWidthDegrees=0.0000875F, LinePattern= "20|10|10|10|10|10", LayerId = 70 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "5", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 5, Name ="county",  StyleSet = "adminBounds", //dash-dot pattern
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3E8A25", FillOrStroke = "stroke", LineWidthDegrees=0.000075F, LinePattern= "20|10|10|10", LayerId = 60 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "6", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 6, Name ="township",  StyleSet = "adminBounds", //dash
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "32FCF6", FillOrStroke = "stroke", LineWidthDegrees=0.0000625F, LinePattern= "20|0", LayerId = 50 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "7", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 7, Name ="city",  StyleSet = "adminBounds", //dash
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "0F34BA", FillOrStroke = "stroke", LineWidthDegrees=0.00005F, LinePattern= "20|0", LayerId = 40 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "8", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 8, Name ="ward",  StyleSet = "adminBounds", //dot
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "A46DFC", FillOrStroke = "stroke", LineWidthDegrees=0.0000475F, LinePattern= "10|10", LayerId = 30 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "9", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 9, Name ="neighborhood",  StyleSet = "adminBounds", //dot
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "B811B5", FillOrStroke = "stroke", LineWidthDegrees=0.000035F, LinePattern= "10|10", LayerId = 20 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "10", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 10, Name ="admin11", StyleSet = "adminBounds", //not rendered
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00FF2020", FillOrStroke = "stroke", LineWidthDegrees=0.0000225F, LinePattern= "solid", LayerId = 10 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "11", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 9999, Name ="background",  StyleSet = "adminBounds",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00F2EFE9", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "*", Value = "*", MatchType = "none" }} },

            new StyleEntry() { MatchOrder = 10000, Name ="unmatched",  StyleSet = "adminBounds",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "*", Value = "*", MatchType = "default" }}
            },
        
            //Team Colors now part of the same default list.
            new StyleEntry() { MatchOrder = 1, Name ="1",  StyleSet = "teamColor",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88FF0000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "FF0000", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "team", Value = "red", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 2, Name ="2",   StyleSet = "teamColor",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "8800FF00", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "00FF00", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "team", Value = "green", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 3, Name ="3",  StyleSet = "teamColor",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "880000FF", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "0000FF", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "team", Value = "blue", MatchType = "equals"},
            }},

            //background is a mandatory style entry name, but its transparent here..
            new StyleEntry() { MatchOrder = 10000, Name ="background",  StyleSet = "teamColor",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "*", Value = "*", MatchType = "default"},
            }},

            //
            //Paint the Town style, uses fromTag.
            //
            new StyleEntry() { MatchOrder = 1, Name ="tag",  StyleSet = "paintTown",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "01000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, FromTag=true }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "*", Value = "*", MatchType = "default"},
            }},
            //background is a mandatory style entry name, but its transparent here..
            new StyleEntry() { MatchOrder = 10000, Name ="background",  StyleSet = "paintTown",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "*", Value = "*", MatchType = "default"},
            }},

            //
            //new style to allow only outlines of ALL entries.
            //
            new StyleEntry() { MatchOrder = 1, Name ="1",  StyleSet = "outlines",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "000000", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "*", Value = "*", MatchType = "default"},
            }},
            //background is a mandatory style entry name, but its transparent here..
            new StyleEntry() { MatchOrder = 10000, Name ="background",  StyleSet = "outlines",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "bg", Value = "bg", MatchType = "equals"}, //this one only gets called by name anyways.
            }},
            //this name needs to exist because of the default style using this name.
            new StyleEntry() { MatchOrder = 10001, Name ="unmatched",  StyleSet = "outlines",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "a", Value = "s", MatchType = "equals" }}
            },

            //
            // Overlay style to show areas that PraxisMapper thinks are good places for gameplay.
            // Includes generated areas.
            //
            new StyleEntry() { IsGameElement = true,  MatchOrder = 1, Name ="water", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC0062FF", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC0000FF", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "natural", Value = "water|strait|bay", MatchType = "or"},
                    new StyleMatchRule() {Key = "waterway", Value ="*", MatchType="or" },
                    new StyleMatchRule() {Key = "landuse", Value ="basin", MatchType="or" },
                    new StyleMatchRule() {Key = "leisure", Value ="swimming_pool", MatchType="or" },
                    new StyleMatchRule() {Key = "place", Value ="sea", MatchType="or" }, //stupid Labrador sea value.
                }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 2, Name ="wetland", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC609124", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC289124", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                 StyleMatchRules = new List<StyleMatchRule>() {
                     new StyleMatchRule() { Key = "natural", Value = "wetland", MatchType = "equals" }
                 }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 3, Name ="park", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {

                    new StylePaint() { HtmlColorCode = "CC93FF61", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CCB2FF8F", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "leisure", Value = "park", MatchType = "or" },
                    new StyleMatchRule() { Key = "leisure", Value = "playground", MatchType = "or" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 4, Name ="beach", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CCF9FF8F", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CCFFEA8F", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "natural", Value = "beach|shoal", MatchType = "or" },
                    new StyleMatchRule() {Key = "leisure", Value="beach_resort", MatchType="or"}
            } },
            new StyleEntry() { IsGameElement = true, MatchOrder = 5, Name ="university", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CCFFFFE5", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CCF5EED3", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "university|college", MatchType = "any" }}
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 6, Name ="natureReserve", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC124504", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC027021", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "leisure", Value = "nature_reserve", MatchType = "equals" }}
            },
            new StyleEntry() {IsGameElement = true, MatchOrder = 7, Name ="cemetery", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CCAACBAF", FillOrStroke = "fill", FileName="Landuse-cemetery.png", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC404040", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "cemetery", MatchType = "or" },
                    new StyleMatchRule() {Key="amenity", Value="grave_yard", MatchType="or" } }
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 8, Name ="retail", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CCF2BBE9", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CCF595E5", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "retail|commercial", MatchType = "or"},
                    new StyleMatchRule() {Key="building", Value="retail|commercial", MatchType="or" },
                    new StyleMatchRule() {Key="shop", Value="*", MatchType="or" }
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 9, Name ="tourism", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC660033", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CCFF0066", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "*", MatchType = "equals" }}
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 10, Name ="historical", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CCB3B3B3", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC9D9D9D", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "historic", Value = "*", MatchType = "equals" }}
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 12, Name ="trail", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC7A5206", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key="highway", Value="path|bridleway|cycleway|footway|living_street", MatchType="any"},
                    new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"}
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 13, Name ="serverGenerated", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC76E3E1", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC5CB5B4", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key="generated", Value="praxisMapper", MatchType="equals"},
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 14, Name ="artsCulture", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "theatre|concert hall|arts centre|planetarium", MatchType = "or" }} //TODO: expand this. Might need to swap order with tourism to catch several other entries.
            },
            //background is a mandatory style entry name, but its transparent here..
            new StyleEntry() { MatchOrder = 10000, Name ="background",  StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "bg", Value = "bg", MatchType = "equals"}, //this one only gets called by name anyways.
            }},
            //this name needs to exist because of the default style using this name.
            new StyleEntry() { MatchOrder = 10001, Name ="unmatched",  StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "a", Value = "s", MatchType = "equals" }}
            },

            //SuggestedMini is set up to allow areas to be tagged with a single value to identify them. Used by the minimize process in Larry.
            new StyleEntry() { IsGameElement = true,  MatchOrder = 1, Name ="water", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC0062FF", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC0000FF", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "suggestedmini", Value = "water", MatchType = "equals"},
                }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 2, Name ="wetland", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC609124", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC289124", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                 StyleMatchRules = new List<StyleMatchRule>() {
                     new StyleMatchRule() {Key = "suggestedmini", Value = "wetland", MatchType = "equals"},
                 }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 3, Name ="park", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {

                    new StylePaint() { HtmlColorCode = "CC93FF61", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CCB2FF8F", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "suggestedmini", Value = "park", MatchType = "equals"},
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 4, Name ="beach", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CCF9FF8F", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CCFFEA8F", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "suggestedmini", Value = "beach", MatchType = "equals"},
            } },
            new StyleEntry() { IsGameElement = true, MatchOrder = 5, Name ="university", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CCFFFFE5", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CCF5EED3", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "suggestedmini", Value = "university", MatchType = "equals"} },
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 6, Name ="natureReserve", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC124504", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC027021", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "suggestedmini", Value = "natureReserve", MatchType = "equals"},
            } },
            new StyleEntry() {IsGameElement = true, MatchOrder = 7, Name ="cemetery", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CCAACBAF", FillOrStroke = "fill", FileName="Landuse-cemetery.png", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC404040", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "suggestedmini", Value = "cemetery", MatchType = "equals"},
            } },
            new StyleEntry() { IsGameElement = true, MatchOrder = 8, Name ="retail", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CCF2BBE9", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CCF595E5", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "suggestedmini", Value = "retail", MatchType = "equals"},
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 9, Name ="tourism", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC660033", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CCFF0066", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "suggestedmini", Value = "tourism", MatchType = "equals"} },
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 10, Name ="historical", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CCB3B3B3", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC9D9D9D", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "suggestedmini", Value = "historical", MatchType = "equals"} },
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 12, Name ="trail", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC7A5206", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "suggestedmini", Value = "trail", MatchType = "equals"},
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 13, Name ="serverGenerated", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC76E3E1", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC5CB5B4", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key="suggstedmini", Value="generated", MatchType="equals"},
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 14, Name ="artsCulture", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key="suggstedmini", Value="artsCulture", MatchType="equals"},
            }},
            //background is a mandatory style entry name, but its transparent here..
            new StyleEntry() { MatchOrder = 10000, Name ="background",  StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "bg", Value = "bg", MatchType = "equals"}, //this one only gets called by name anyways.
            }},
            //this name needs to exist because of the default style using this name.
            new StyleEntry() { MatchOrder = 10001, Name ="unmatched",  StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "a", Value = "s", MatchType = "equals" }}
            },
        };
    };
}
