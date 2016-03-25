using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Web.Hosting;
using System.Text;

using Google.ProtocolBuffers;

using GeoAPI.Geometries;
using GeoAPI.Geometries.Prepared;
using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;
using NetTopologySuite.Features;
using NetTopologySuite.Index.Quadtree;

//using SharpMap.Data;
//using SharpMap.Data.Providers;

using vector_tile;

namespace Demo2.ws
{
    
    // NOTE: You can use the "Rename" command on the "Refactor" menu to change the class name "MapBoxVectorService" in code, svc and config file together.
    // NOTE: In order to launch WCF Test Client for testing this service, please select MapBoxVectorService.svc or MapBoxVectorService.svc.cs at the Solution Explorer and start debugging.
    public class MapBoxVectorTileService : IMapBoxVectorTileService
    {
        //const string WGS84ShpFilename = "~/App_Data/MSOA_2011_EW_BGC/WGS84_MSOA_2011_EW_BGC.shp"; //NOTE This is the full resolution - SIMP was thinned with a tolerance of 0.0005
        ////const string EPSG3857ShpFilename = "~/App_Data/MSOA_2011_EW_BGC/EPSG3857_MSOA_2011_EW_BGC.shp"; //this is a pre-projected one for Google Maps
        //const string EPSG3857ShpFilename = "~/App_Data/EPSG3857_MSOA_2011_EWS.shp"; //this is a pre-projected one for Google Maps - CONTAINS SCOTLAND

        /// <summary>
        /// 
        /// see: https://github.com/mapbox/vector-tile-spec/tree/master/1.0.0
        /// https://github.com/mapbox/geojson-vt
        /// ***This is a js debug console for geojson split into vector tiles: http://mapbox.github.io/geojson-vt/debug/
        /// https://api.mapbox.com/v4/{mapid}/{z}/{x}/{y}.{format}?access_token=your access token
        /// 
        /// You can get a vector tile from here:
        /// https://a.tiles.mapbox.com/v4/mapbox.mapbox-streets-v6/6/30/20.vector.pbf?access_token=pk.eyJ1IjoicndtaWx0b24iLCJhIjoiSmZINmZDWSJ9.u748qYexge5Txkq4iuUV2Q
        /// 
        /// Import this from NuGet:
        /// Nuget:  Install-Package Google.ProtocolBuffers
        /// </summary>
        /// <param name="Z"></param>
        /// <param name="X"></param>
        /// <param name="Y"></param>
        public Stream GetTile(string Z, string X, string Y)
        {
            //TODO: the maths here is horrible - need to tidy it all up (merc tiles are projected to WGS84, which are then projected back to merc pixels to match the coordinates)

            string EPSG3857ShpFilename = ConfigurationManager.AppSettings["MSOAShapefile"];

            //DEBUG
            Random rand = new Random();

            //8,125,83
            //6,31,21
            //WGS84 datum (longitude/latitude): -5.625 48.922499263758254, 0 52.482780222078205
            //Spherical Mercator (meters): -626172.1357121654 6261721.357121639, 0 6887893.492833804
            //see: http://www.maptiler.org/google-maps-coordinates-tile-bounds-projection/
            //work out tile extents
            int TileZ = Convert.ToInt32(Z);
            int TileX = Convert.ToInt32(X);
            int TileY = Convert.ToInt32(Y);
            int Size = (int)Math.Pow(2, TileZ); //number of tiles along one side

            double Deg_x = 360.0 / Size * TileX - 180.0;
            double Deg_x2 = 360.0 / Size * (TileX + 1) - 180.0;
            //positive latitude is in the Northern Hemisphere, but tile origin is top left, so y is reversed, but still need min and max the right way around
            double Deg_y = 180.0 - 360.0 / Size * TileY; // 360.0 / Size * (Size - TileY) - 180.0;
            double Deg_y2 = 180.0 - 360.0 / Size * (TileY + 1); // 360.0 / Size * (Size - TileY + 1) - 180.0;
            //double Deg_y = 360.0 / Size * TileY - 180.0;
            //double Deg_y2 = 360.0 / Size * (TileY+1) - 180.0;
            double[] sw = MercatorProjection.toDegLonLat(Deg_x, Deg_y);
            double[] ne = MercatorProjection.toDegLonLat(Deg_x2, Deg_y2);
            //Envelope WGS84TileExtents = new Envelope(Deg_x, Deg_x2, Deg_y, Deg_y2);
            Envelope WGS84TileExtents = new Envelope(sw[0], ne[0], sw[1], ne[1]);
            //double lat_rad = Math.Atan(Math.Sinh(Math.PI * (1.0f - 2.0f * TileY / Size)));
            //double lat_deg = lat_rad * 180.0 / Math.PI;
            //double lat_deg_wgs84 = MapBoxPBTestWebsite.MercatorProjection.yDegToLatDeg(Deg_y);

            //project Mercator extents into WGS84 (this is a metres extents box to match the projected data)
            //double minLon = MapBoxPBTestWebsite.MercatorProjection.xToLon(WGS84TileExtents.MinX * Math.PI / 180.0);
            //double minLat = MapBoxPBTestWebsite.MercatorProjection.yToLat(WGS84TileExtents.MinY * Math.PI / 180.0);
            //double maxLon = MapBoxPBTestWebsite.MercatorProjection.xToLon(WGS84TileExtents.MaxX * Math.PI / 180.0);
            //double maxLat = MapBoxPBTestWebsite.MercatorProjection.yToLat(WGS84TileExtents.MaxY * Math.PI / 180.0);
            //Envelope MercTileExtents = new Envelope(minLon, maxLon, minLat, maxLat);
            //convert Mercator tile coordinates into a tile extents envelope in metres
            //double[] mercSW = MapBoxPBTestWebsite.MercatorProjection.toPixel(Deg_x, Deg_y);
            //double[] mercNE = MapBoxPBTestWebsite.MercatorProjection.toPixel(Deg_x2, Deg_y2);
            double[] mercSW = MercatorProjection.toPixel(sw[0], sw[1]);
            double[] mercNE = MercatorProjection.toPixel(ne[0], ne[1]);
            Envelope MercTileExtents = new Envelope(mercSW[0], mercNE[0], mercSW[1], mercNE[1]);

            //debug
            System.Diagnostics.Debug.WriteLine("WGS84TileExtents: " + WGS84TileExtents);
            System.Diagnostics.Debug.WriteLine("MercTileExtents: " + MercTileExtents);

            VectorTileHelper VTHelper = new VectorTileHelper("mytilepoints");

            //now get the features for this tile's extents
            //ShapeFile sf = new ShapeFile(HostingEnvironment.MapPath(/*WGS84ShpFilename*/EPSG3857ShpFilename));
            List<Feature> All_features = ShapeUtils.LoadShapefile(HostingEnvironment.MapPath(/*WGS84ShpFilename*/EPSG3857ShpFilename));
            Quadtree<Feature> findex = new Quadtree<Feature>();
            foreach (Feature f in All_features) findex.Insert(f.Geometry.EnvelopeInternal, f);

            MemoryStream ms = new MemoryStream();
            //StreamWriter writer = new StreamWriter(ms);
            try
            {
                //sf.Open();
                //FeatureDataSet fds = new FeatureDataSet();
                //sf.ExecuteIntersectionQuery(/*WGS84TileExtents*/MercTileExtents, fds); //big hit on time - note the extents must follow the shapefile data projection
                //FeatureDataTable table = fds.Tables[0] as FeatureDataTable;
                IList<Feature> features = findex.Query(/*WGS84TileExtents*/MercTileExtents);

                //get all the column names and add them to the layer using the vector tile helper
                List<string> names = new List<string>();
                //foreach (System.Data.DataColumn col in table.Columns)
                foreach (string ColumnName in All_features[0].Attributes.GetNames())
                {
                    names.Add(ColumnName);
                    VTHelper.AddKeyTag(ColumnName); //so we add the keys to the helper which allows us to get the key tag back later
                }
                //DEBUG - added a value field
                names.Add("value");
                VTHelper.AddKeyTag("value");

                System.Diagnostics.Debug.WriteLine("Found: " + features.Count + " rows of data");

                GeometryFactory gf = new GeometryFactory();
                //Geometry TileClipBox = (Geometry)gf.ToGeometry(WGS84TileExtents); //why is an envelope not part of the geometry family?
                Geometry MercTileClipBox = (Geometry)gf.ToGeometry(MercTileExtents);
                MercTileClipBox = (Geometry)MercTileClipBox.Buffer(MercTileExtents.Height * 0.1); //make it 10% bigger to lose the tile edges
                //IPreparedGeometry prepClipPolygon = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(TileClipBox);

                //presumably, do something with the data here?
                foreach (Feature f in features)
                {
                    //need to convert geometry to Mercator (from WGS84)
                    Geometry geom = (Geometry)f.Geometry;
                    //if (!geom.IsValid) continue;
                    //if (!geom.Intersects(TileClipBox)) continue; //passed spatial index test, but not actually on this tile
                    //Geometry clippedgeom = (Geometry)geom.Intersection(WGS84TileExtents);
                    //Geometry clippedgeom = null;
                    //try
                    //{
                    //    clippedgeom = (Geometry)geom.Intersection(TileClipBox);
                    //}
                    //catch (Exception ex)
                    //{
                    //    clippedgeom = null;
                    //}
                    //if (clippedgeom == null) continue;
                    //Geometry clippedgeom = (Geometry)TileClipBox.Intersection(geom);
                    //Geometry clippedgeom = (Geometry)geom.Intersection(prepClipPolygon);
                    //if (clippedgeom.NumGeometries == 0) continue;
                    //if (clippedgeom.NumPoints == 0) continue;

                    //transform (and TODO: simplify) geometry
                    ////Geometry transGeom = (Geometry)GeometryTransform.TransformGeometry(geom.Factory, geom, mt);
                    //why do you still have to reproject the coordinates manually? I just get a not implemented error any other way. Box projection works though.
                    //removed projection when I switched to EPSG3857 coords
                    //for (int i = 0; i < geom.Coordinates.Length; i++)
                    //{
                    //    //TODO: can you decimate at this point if there are doubles in the rings?
                    //    //double[] pts = mt.Transform(new double[] { geom.Coordinates[i].X, geom.Coordinates[i].Y });
                    //    //System.Diagnostics.Debug.WriteLine("coord:" + pts[0]+","+pts[1]);
                    //    geom.Coordinates[i].X = MercatorProjection.lonToX(geom.Coordinates[i].X /** Math.PI / 180.0*/); //looking at the lonToX formula, these are pixels!
                    //    geom.Coordinates[i].Y = MercatorProjection.latToY(geom.Coordinates[i].Y /** Math.PI / 180.0*/);
                    //    //System.Diagnostics.Debug.WriteLine("Projected: " + geom.Coordinates[i].X + "," + geom.Coordinates[i].Y);
                    //    //if (MercTileExtents.Contains(geom.Coordinates[i])) System.Diagnostics.Debug.WriteLine("Covered point");
                    //}


                    //simplify here
                    DouglasPeuckerSimplifier DSimp = new DouglasPeuckerSimplifier(geom);
                    //DSimp.DistanceTolerance = 0.01;
                    DSimp.DistanceTolerance = MercTileExtents.Height / 1024.0;
                    DSimp.EnsureValidTopology = true;
                    Geometry simplegeom = (Geometry)DSimp.GetResultGeometry();
                    //TopologyPreservingSimplifier TSimp = new TopologyPreservingSimplifier(geom);
                    //TSimp.DistanceTolerance = 0.01;
                    //Geometry simplegeom = (Geometry)TSimp.GetResultGeometry();


                    //clip here
                    Geometry clippedgeom = (Geometry)simplegeom.Intersection(MercTileClipBox);

                    if (clippedgeom.NumPoints < 3) continue;

                    ////VTHelper.AddGeometry(MercTileExtents, geom);
                    //object[] debugItemArray = new object[fdr.ItemArray.Length + 1];
                    //for (int i = 0; i < fdr.ItemArray.Length; i++) debugItemArray[i] = fdr.ItemArray[i];
                    //debugItemArray[debugItemArray.Length - 1] = (float)rand.NextDouble(); //value field - random 0..1
                    //VTHelper.AddGeographicFeature(MercTileExtents, clippedgeom/*geom*/, names, debugItemArray/*fdr.ItemArray*/);

                    object[] debugItemArray = new Object[f.Attributes.Count + 1];
                    for (int i = 0; i < f.Attributes.Count; i++) debugItemArray[i] = f.Attributes[names[i]];
                    debugItemArray[debugItemArray.Length - 1] = (float)rand.NextDouble(); //value field - random 0..1
                    VTHelper.AddGeographicFeature(MercTileExtents, clippedgeom, names, debugItemArray);
                }


                Tile MyTile = VTHelper.BuildTile();
                MyTile.WriteTo(ms);
            }
            finally
            {
                //writer.Flush();
                ms.Position = 0;
            }
            //TODO: return a .pbf file
            //prevent output from being cached
            WebOperationContext.Current.OutgoingResponse.Headers.Add("Cache-Control", "no-cache");
            WebOperationContext.Current.OutgoingResponse.ContentType = "text/json";
            System.Diagnostics.Debug.WriteLine("Completed tile " + Z + " " + X + " " + Y);
            return ms;
        }
    }
}
