using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;

using GeoAPI;
using GeoAPI.Geometries;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;

using vector_tile;

namespace MapTubeV
{
    public class TileRequestorZXY
    {
        //protected tilerenderer?

        /// <summary>
        /// This is the singleton instance of the circular buffer which holds map data currently loaded into memory
        /// </summary>
        protected MapCache Cache = MapCache.GetInstance; //singleton


        /// <summary>
        /// check whether CacheKey is a relative URL and convert if necessary
        /// NOTE: CacheKeys MUST ALWAYS be converted to full URLs otherwise you could have two mydata.csv files
        /// on different servers. URIs referenced inside the descriptor file are relative to that file.
        /// </summary>
        /// <param name="CacheKey">The Uri of the descriptor.xml file that uniquely identifies the data. This could be
        /// either an absolute path or a relative one.</param>
        /// <param name="ContextPath">The absolute path of where this web server is serving requests from. Can be
        /// null if none exists. Used when converting a relative CacheKey into an absolute one.</param>
        /// <returns>An absolute Uri for the CacheKey</returns>
        /// TODO: do you need this as ALL requests are going to need to be fully qualified i.e. no render style relative references
        //public static string GetAbsoluteUri(string CacheKey, string ContextPath)
        //{
        //    Uri DescriptorUri;
        //    if (!Uri.IsWellFormedUriString(CacheKey, UriKind.Absolute))
        //    {
        //        if (ContextPath == null) throw new Exception("Cannot resolve relative URI as no ContextPath provided");
        //        DescriptorUri = new Uri(new Uri(ContextPath), CacheKey);
        //    }
        //    else
        //    {
        //        DescriptorUri = new Uri(CacheKey);
        //    }
        //    return DescriptorUri.AbsoluteUri;
        //}
        //NOW IN UTILS!!!!!

        public bool RequestTile(string CacheKey, string TimeTag, int TileZ, int TileX, int TileY, string ContextPath, out Tile VTile)
        {
            try {
                CacheKey = Utils.GetAbsoluteUri(CacheKey, ContextPath);

                //8,125,83
                //6,31,21
                //WGS84 datum (longitude/latitude): -5.625 48.922499263758254, 0 52.482780222078205
                //Spherical Mercator (meters): -626172.1357121654 6261721.357121639, 0 6887893.492833804
                //see: http://www.maptiler.org/google-maps-coordinates-tile-bounds-projection/

                //work out tile extents
                int Size = (int)Math.Pow(2, TileZ); //number of tiles along one side

                double Deg_x = 360.0 / Size * TileX - 180.0;
                double Deg_x2 = 360.0 / Size * (TileX + 1) - 180.0;
                //positive latitude is in the Northern Hemisphere, but tile origin is top left, so y is reversed, but still need min and max the right way around
                double Deg_y = 180.0 - 360.0 / Size * TileY; // 360.0 / Size * (Size - TileY) - 180.0;
                double Deg_y2 = 180.0 - 360.0 / Size * (TileY + 1); // 360.0 / Size * (Size - TileY + 1) - 180.0;
                double[] sw = MercatorProjection.toDegLonLat(Deg_x, Deg_y);
                double[] ne = MercatorProjection.toDegLonLat(Deg_x2, Deg_y2);
                Envelope WGS84TileExtents = new Envelope(sw[0], ne[0], sw[1], ne[1]);

                //project Mercator extents into WGS84 (this is a metres extents box to match the projected data)
                double[] mercSW = MercatorProjection.toPixel(sw[0], sw[1]);
                double[] mercNE = MercatorProjection.toPixel(ne[0], ne[1]);
                Envelope MercTileExtents = new Envelope(mercSW[0], mercNE[0], mercSW[1], mercNE[1]);

                //debug
                System.Diagnostics.Debug.WriteLine("WGS84TileExtents: " + WGS84TileExtents);
                System.Diagnostics.Debug.WriteLine("MercTileExtents: " + MercTileExtents);

                VectorTileHelper VTHelper = new VectorTileHelper("mytilepoints"); //TODO: need a unique name based on the shapefile name here

                //TODO: pull the shapefile data from the geometry cache
                //and draw onto the vector tile

                return DrawTile(CacheKey,TimeTag,out VTile,WGS84TileExtents,MercTileExtents);
            }
            catch (Exception ex)
            {
                //TODO: you can't draw the error onto the tile if it's not an image, but you could create some clickable geometry with the error in the attribute data

            }

            //Failed...
            VTile = null;
            return false;
        }

        /// <summary>
        /// Write the tile data onto the tile. This takes the place of the TileRenderer in MapTubeD, which is now redundant as rendering of vector tiles is done on the client.
        /// In the event that multiple formats of vector tile need to be supported, then this can be split from the tile requestor and made into a class of its own.
        /// </summary>
        /// <returns></returns>
        private bool DrawTile(string CacheKey, string TimeTag, out Tile VTile, Envelope WGS84TileExtents, Envelope MercTileExtents)
        {
            //todo: probably don't need the wgs84 tile extents?
            MapDescriptor md = Cache.GetMapDescriptor(CacheKey, TimeTag);
            string LayerName = Path.GetFileNameWithoutExtension(md.DescriptorUri); //creates a source layer based on the filename referenced by the URI
            VectorTileHelper VTHelper = new VectorTileHelper(LayerName);

            IList<Feature> features = md.Features.Query(MercTileExtents);

            //get all the column names and add them to the layer using the vector tile helper
            List<string> names = new List<string>();
            //TODO: you have a problem here if the quadtree doesn't return any features on the tile
            foreach (string ColumnName in features[0].Attributes.GetNames())
            {
                names.Add(ColumnName);
                VTHelper.AddKeyTag(ColumnName); //so we add the keys to the helper which allows us to get the key tag back later
            }
            //Add an identifier field so we can track where our tiles end up
            names.Add("MapTubeV");
            VTHelper.AddKeyTag("true");

            System.Diagnostics.Debug.WriteLine("Found: " + features.Count + " rows of data");

            GeometryFactory gf = new GeometryFactory();
            Geometry MercTileClipBox = (Geometry)gf.ToGeometry(MercTileExtents);
            MercTileClipBox = (Geometry)MercTileClipBox.Buffer(MercTileExtents.Height * 0.1); //make it 10% bigger to lose the tile edges

            foreach (Feature f in features)
            {
                Geometry geom = (Geometry)f.Geometry;

                //simplify geometry to a level appropriate for the tile
                DouglasPeuckerSimplifier DSimp = new DouglasPeuckerSimplifier(geom);
                DSimp.DistanceTolerance = MercTileExtents.Height / 1024.0;
                DSimp.EnsureValidTopology = true;
                Geometry simplegeom = (Geometry)DSimp.GetResultGeometry();

                //clip geometry to tile
                Geometry clippedgeom = (Geometry)simplegeom.Intersection(MercTileClipBox);

                if (clippedgeom.NumPoints < 3) continue; //sanity check as you might end up with nothing - TODO: need to include point features in here to fill holes

                object[] attrItemArray = new Object[f.Attributes.Count];
                for (int i = 0; i < f.Attributes.Count; i++) attrItemArray[i] = f.Attributes[names[i]];
                VTHelper.AddGeographicFeature(MercTileExtents, clippedgeom, names, attrItemArray);
            }

            VTile = VTHelper.BuildTile();
            return true;

        }
    }
}