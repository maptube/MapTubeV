using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Google.ProtocolBuffers;
using vector_tile;

using GeoAPI.Geometries;
using NetTopologySuite.Geometries;

namespace Demo2.ws
{
    /// <summary>
    /// Helper for creating vector tiles from GeoAPI geometry
    /// </summary>
    public class VectorTileHelper
    {
        //create a vector tile - from what? feature or geometry?

        Tile.Types.Layer.Builder LayerBuilder;
        //Tile.Types.Feature.Builder FeatureBuilder;

        protected Dictionary<string, uint> KeyTagLookup = new Dictionary<string, uint>(); //lookup between key names and tag numbers


        public enum CommandCode { MoveTo=1, LineTo=2, ClosePath=7 }

        public VectorTileHelper(string LayerName)
        {
            LayerBuilder = new Tile.Types.Layer.Builder();
            LayerBuilder.SetName(LayerName);
        }

        /// <summary>
        /// Add a key and define its tag number. If the key already exists then it returns the existing one rather than creating a new one.
        /// </summary>
        /// <param name="KeyName"></param>
        /// <returns>The tag number just added (or existing one returned).</returns>
        public uint AddKeyTag(string KeyName)
        {
            if (KeyTagLookup.ContainsKey(KeyName)) return KeyTagLookup[KeyName];
            uint Tag = (uint)KeyTagLookup.Count;
            KeyTagLookup.Add(KeyName, Tag);
            LayerBuilder.AddKeys(KeyName); //actually create the key tag using the vector tile builder
            return Tag;
        }

        /// <summary>
        /// Query an exisiting tag number for a key name - throws exception if it doesn't exist
        /// </summary>
        /// <param name="KeyName"></param>
        /// <returns></returns>
        public uint GetKeyTag(string KeyName)
        {
            return KeyTagLookup[KeyName]; //or you could just return AddKeyTag()
        }

        /// <summary>
        /// Add a new value. All values are assumed to be unique, so we always create a new one.
        /// TODO: you could conceivably have duplicated string values i.e. areakeys?
        /// </summary>
        /// <param name="Value"></param>
        /// <returns></returns>
        public uint AddValueTag(object Value)
        {
            Tile.Types.Value.Builder B = new Tile.Types.Value.Builder();
            //B.DefaultInstanceForType()????
            //string ValType = Value.GetType().ToString();
            //System.Diagnostics.Debug.WriteLine(ValType + " " + Value);
            //this is horrible, this can't be the way they expect you to do it surely?
            if (Value is String)
                B.SetStringValue((string)Value);
            else if (Value is int)
                B.SetIntValue((int)Value);
            else if (Value is Int64)
                B.SetIntValue((Int64)Value);
            else if (Value is uint)
                B.SetUintValue((uint)Value);
            else if (Value is double)
                B.SetDoubleValue((double)Value);
            else
                B.SetFloatValue((float)Value);
            Tile.Types.Value Val = B.Build();
            uint Count = (uint)LayerBuilder.ValuesCount;
            LayerBuilder.AddValues(Val);
            return Count;
        }

        /// <summary>
        /// Make a command code repeat number from the command code and the repeat
        /// </summary>
        /// <param name="Command"></param>
        /// <param name="Repeat"></param>
        /// <returns></returns>
        protected static uint CommandCodeRepeat(CommandCode Command, uint Repeat)
        {
            //Command is 1=MoveTo (2 params follow), 2=LineTo (2 params follow), 7=ClosePath (no params)
            //Repeat is then shifted 3
            return ((uint)Command) | (Repeat << 3);
        }

        /// <summary>
        /// ZigZag encode a signed integer so that 0=0, 1=2, 2=4, -1=1, -2=3
        /// </summary>
        /// <param name="n"></param>
        /// <returns></returns>
        protected static uint ZigZag32(int n)
        {
            //https://developers.google.com/protocol-buffers/docs/encoding#types
            uint x = ((uint)(n << 1)) ^ ((uint)(n >> 31)); //it's 63 for a uint64 - NOTE: you have to shift the signed type for it to be a sign extension shift right
            //System.Diagnostics.Debug.WriteLine("ZigZag " + n + " = " + x + " "+x.ToString("X"));
            return x;
        }

        public Tile BuildTile()
        {
            Tile MyTile;
            LayerBuilder.SetExtent(4096);
            LayerBuilder.SetVersion(2);
            Tile.Builder builder = Tile.CreateBuilder();
            builder.AddLayers(LayerBuilder.Build());

            MyTile = builder.Build();
            return MyTile;
        }

        //add geographic feature because there is a Tile.Types.Feature
        //public void AddGeographicFeature(Envelope TileExtents, NetTopologySuite.Features.Feature f)
        //{
        //    //so, add the key value feature pairs to the layer and pass the tags on to the tile feature
        //    string [] Names = f.Attributes.GetNames();
        //    object [] Values = f.Attributes.GetValues();
        //    for (int i=0; i<Names.Length; i++)
        //    {
        //        LayerBuilder.AddKeys(Names[i]);
        //        Tile.Types.Value.Builder B = new Tile.Types.Value.Builder();
        //        //B.DefaultInstanceForType()????
        //        //this is horrible, this can't be the way they expect you to do it surely?
        //        if (Values[i] is String)
        //            B.SetStringValue((string)Values[i]);
        //        else
        //            B.SetFloatValue((float)Values[i]);
        //        Tile.Types.Value Val = B.Build();
        //        LayerBuilder.AddValues(Val);
        //    }
        //}

        /// <summary>
        /// Add a feature with geometry and a list of key value pairs for data.
        /// </summary>
        /// <param name="TileExtents"></param>
        /// <param name="geom"></param>
        /// <param name="Names">The column names, which should be the same for every feature and have already had their tags created using AddTag()</param>
        /// <param name="Values">The values for the features, which are assumed to be unique for each feature in the layer (TODO: strings could be duplicated)</param>
        public void AddGeographicFeature(Envelope TileExtents, Geometry geom, List<string> Names, object [] Values)
        {
            Tile.Types.Feature.Builder FeatureBuilder = new Tile.Types.Feature.Builder();

            //add all the key and value tags on to the new feature
            for (int i = 0; i < Names.Count; i++)
            {
                uint KeyTag = GetKeyTag(Names[i]);
                uint ValueTag = AddValueTag(Values[i]);
                FeatureBuilder.AddTags(KeyTag);
                FeatureBuilder.AddTags(ValueTag);
            }
            
            //now add the geometry to the feature
            AddGeometryToFeature(TileExtents, FeatureBuilder, geom);

            Tile.Types.Feature f = FeatureBuilder.Build();
            LayerBuilder.AddFeatures(f);
        }

        //public void AddGeometry(Envelope TileExtents, Geometry geom)
        //{
        //    Tile.Types.Feature f = GetFeatureFromGeometry(TileExtents, geom);
        //    LayerBuilder.AddFeatures(f);
        //}


        /// <summary>
        /// Adds the geometry to an existing FeatureBuilder.
        /// NOTE: if you call this twice on the same feature, the X and Y deltas will probably be wrong
        /// </summary>
        /// <param name="TileExtents"></param>
        /// <param name="FeatureBuilder"></param>
        /// <param name="geom"></param>
        /// <returns></returns>
        public void AddGeometryToFeature(Envelope TileExtents, Tile.Types.Feature.Builder FeatureBuilder, Geometry geom)
        {
            int X = 0, Y = 0; //these are the coordinates that we need to track for the delta encoding
            //Tile.Types.Feature.Builder FeatureBuilder = new Tile.Types.Feature.Builder();

            //FeatureBuilder.SetId((ulong)0); //?????? What does this do?

            //now create the geometry paths
            for (int N = 0; N < geom.NumGeometries; N++)
            {
                Geometry geomN = (Geometry)geom.GetGeometryN(N);
                switch (geomN.OgcGeometryType)
                {
                    case OgcGeometryType.Point:
                        //TODO:
                        break;
                    case OgcGeometryType.MultiPoint:
                        //TODO:
                        break;
                    case OgcGeometryType.LineString:
                        //TODO:
                        break;
                    case OgcGeometryType.MultiLineString:
                        //TODO:
                        break;
                    case OgcGeometryType.MultiPolygon:
                        FeatureBuilder.SetType(Tile.Types.GeomType.POLYGON); //you have to set the type to a geometry other than UNKNOWN, otherwise things don't seem to work
                        MultiPolygon mp = (MultiPolygon)geomN;
                        for (int i = 0; i < mp.NumGeometries; i++)
                        {
                            AddPolygon((Polygon)mp.GetGeometryN(i), TileExtents, ref FeatureBuilder, ref X, ref Y);
                        }
                        break;
                    case OgcGeometryType.Polygon:
                        FeatureBuilder.SetType(Tile.Types.GeomType.POLYGON); //you have to set the type to a geometry other than UNKNOWN, otherwise things don't seem to work
                        AddPolygon((Polygon)geomN, TileExtents, ref FeatureBuilder, ref X, ref Y);
                        break;
                }
            }
            //Tile.Types.Feature MyFeature = FeatureBuilder.Build();
            //return MyFeature;
        }

        //this needs to be changed to use get feature from geometry
        //or delete completely?
        //public static Tile FromGeometry(string LayerName, Envelope TileExtents, Geometry geom)
        //{
        //    Tile MyTile;
        //    int X = 0, Y = 0; //these are the coordinates that we need to track for the delta encoding

        //    Tile.Types.Layer.Builder LayerBuilder = new Tile.Types.Layer.Builder();
        //    LayerBuilder.SetName(LayerName);
        //    Tile.Types.Feature.Builder FeatureBuilder = new Tile.Types.Feature.Builder();

        //    //now create the geometry paths
        //    for (int N = 0; N < geom.NumGeometries; N++)
        //    {
        //        Geometry geomN = (Geometry)geom.GetGeometryN(N);
        //        switch (geomN.OgcGeometryType) {
        //            case OgcGeometryType.Point:
        //                //TODO:
        //                break;
        //            case OgcGeometryType.MultiPoint:
        //                //TODO:
        //                break;
        //            case OgcGeometryType.LineString:
        //                //TODO:
        //                break;
        //            case OgcGeometryType.MultiLineString:
        //                //TODO:
        //                break;
        //            case OgcGeometryType.MultiPolygon :
        //                MultiPolygon mp = (MultiPolygon)geomN;
        //                for (int i = 0; i < mp.NumGeometries; i++)
        //                {
        //                    AddPolygon((Polygon)mp.GetGeometryN(i), TileExtents, ref FeatureBuilder, ref X, ref Y);
        //                }
        //                break;
        //            case OgcGeometryType.Polygon:
        //                AddPolygon((Polygon)geomN, TileExtents, ref FeatureBuilder, ref X, ref Y);
        //                break;
        //        }
        //    }
        //    Tile.Types.Feature MyFeature = FeatureBuilder.Build();
        //    LayerBuilder.AddFeatures(MyFeature);
        //    //now add the features from the data....
        //    //todo: I don't have that data here yet
        //    //Feature.AddTags, then
        //    //LayerBuilder.AddKeys and AddValues
        //    LayerBuilder.SetExtent(4096);
        //    LayerBuilder.SetVersion(2);

        //    Tile.Builder builder = Tile.CreateBuilder();
        //    builder.AddLayers(LayerBuilder.Build());

        //    MyTile = builder.Build();

        //    return MyTile;
        //}

        protected static void AddPoint(Point point, Envelope TileExtents, ref Tile.Types.Feature.Builder Builder, ref int X, ref int Y)
        {
            //TODO:
        }

        protected static void AddLine(LineString linestring, Envelope TileExtents, ref Tile.Types.Feature.Builder Builder, ref int X, ref int Y)
        {
            //TODO:
        }

        //public static IGeometry BuildGeometry(List<IGeometry> geoms, IGeometry parentGeom)
        //{
        //    if (geoms.Count <= 0) return null;
        //    if (geoms.Count == 1) return geoms[0];

        //    // if parent was a GC, ensure returning a GC
        //    if (parentGeom.OgcGeometryType == OgcGeometryType.GeometryCollection)
        //        return parentGeom.Factory.CreateGeometryCollection(GeometryFactory.ToGeometryArray(geoms));
            
        //    // otherwise return MultiGeom
        //    return parentGeom.Factory.BuildGeometry(geoms);
        //}

        //public static IGeometry clip(IGeometry a, IGeometry mask)
        //{
        //    var geoms = new List<IGeometry>();
        //    for (var i = 0; i < a.NumGeometries; i++)
        //    {
        //        var clip = a.GetGeometryN(i).Intersection(mask);
        //        geoms.Add(clip);
        //    }
        //    return BuildGeometry(geoms, a);
        //}

        protected static void AddPolygon(Polygon poly, Envelope TileExtents, ref Tile.Types.Feature.Builder Builder, ref int X, ref int Y)
        {
            if (poly.IsEmpty) return; //guard against null polygons

            //if (poly.NumInteriorRings > 0) System.Diagnostics.Debug.WriteLine("Poly has holes: " + poly.NumInteriorRings);
            //else return; //doesn't work as leads to null geometry above add poly

            //GeometryFactory gf = new GeometryFactory();
            ////Envelope clipenv = new Envelope(0, 0, 2048, 2048);
            //Geometry clipbox = (Geometry)gf.ToGeometry(TileExtents/*clipenv*/);
            //Geometry clippedpoly = (Geometry)clip(poly, clipbox);
            //if (clippedpoly == null) return;
            //if (clippedpoly.GetGeometryN(0).OgcGeometryType != OgcGeometryType.Polygon) return;
            //poly = (Polygon)clippedpoly.GetGeometryN(0); //really big hack!! you can get more than one polygon AND the result might not be a poly!
            //if (poly.IsEmpty) return;

            //TODO: this needs to triangulate! NO?
            //AND use the holes!
            //AND do the decimation correctly
            //AND clip to the tile
            int NumRings = 1 + poly.NumInteriorRings;
            for (int r = 0; r < NumRings; r++)
            {
                LineString ring;
                if (r == 0) ring = (LineString)poly.ExteriorRing;
                else ring = (LineString)poly.GetInteriorRingN(r - 1);
                Coordinate[] coords = ring.Coordinates;
                //if (r > 0)
                //{
                //    Coordinate[] coords2 = new Coordinate[coords.Length];
                //    for (int i = 0; i < coords.Length; i++)
                //        coords2[i] = coords[coords.Length - 1 - i];
                //    coords = coords2;
                //}

                //transform coordinates into pixels
                int[] transX = new int[coords.Length];
                int[] transY = new int[coords.Length];
                for (int i = 0; i < coords.Length; i++)
                {
                    //round?
                    transX[i] = (int)((coords[i].X - TileExtents.MinX) / TileExtents.Width * 4096);
                    //transY[i] = (int)((coords[i].Y - TileExtents.MinY)/TileExtents.Height*4096);
                    transY[i] = (int)((TileExtents.MaxY - coords[i].Y) / TileExtents.Height * 4096); //inverted Y with origin top left
                    //System.Diagnostics.Debug.WriteLine("Polygon: " + transX[i] + "," + transY[i]);
                }

                //add a move to the first point
                Builder.AddGeometry(CommandCodeRepeat(CommandCode.MoveTo, 1));
                Builder.AddGeometry(ZigZag32(transX[0] - X));
                Builder.AddGeometry(ZigZag32(transY[0] - Y));
                X = transX[0]; Y = transY[0];

                //then line to for all the other points
                for (int i = 1; i < coords.Length; i++)
                {
                    if (((transX[i] - X) * (transX[i] - X) + (transY[i] - Y) * (transY[i] - Y)) < 1.4) continue; //skip if the delta isn't big enough (HACK?)
                    Builder.AddGeometry(CommandCodeRepeat(CommandCode.LineTo, 1));
                    Builder.AddGeometry(ZigZag32(transX[i] - X));
                    Builder.AddGeometry(ZigZag32(transY[i] - Y));
                    X = transX[i]; Y = transY[i];
                }

                //and finally close the path
                Builder.AddGeometry(CommandCodeRepeat(CommandCode.ClosePath, 1));
            }
        }
    }
}
