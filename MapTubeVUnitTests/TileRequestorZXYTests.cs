using Microsoft.VisualStudio.TestTools.UnitTesting;
using MapTubeV;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using GeoAPI.Geometries;

namespace MapTubeV.Tests
{
    [TestClass()]
    public class TileRequestorZXYTests
    {
        /// <summary>
        /// Return mean square difference between the four coordinates of two envelopes i.e. how close are they to identical?
        /// </summary>
        /// <param name="env1"></param>
        /// <param name="env2"></param>
        /// <returns></returns>
        private double DeltaEnvelope(Envelope env1, Envelope env2)
        {
            double delta = Math.Pow(env1.MinX - env2.MinX, 2) + Math.Pow(env1.MaxX - env2.MaxX, 2)
                + Math.Pow(env1.MinY - env2.MinY, 2) + Math.Pow(env1.MaxY - env2.MaxY, 2);
            return Math.Sqrt(delta);
        }

        /// <summary>
        /// This is a test for whether the tile envelope calculation from the tile zxy numbers works.
        /// </summary>
        [TestMethod()]
        public void GetEnvelopeForTileTest()
        {
            //8,125,83
            //6,31,21
            //WGS84 datum (longitude/latitude): -5.625 48.922499263758254, 0 52.482780222078205
            //Spherical Mercator (meters): -626172.1357121654 6261721.357121639, 0 6887893.492833804
            //see: http://www.maptiler.org/google-maps-coordinates-tile-bounds-projection/

            //6,31,21
            //WGS84 datum(longitude / latitude):-5.625 48.922499263758254 0 52.48278022207823
            //Spherical Mercator (meters): -626172.1357121654 6261721.357121639 0 6887893.492833804

            Envelope WGS84TileExtents, MercTileExtents;
            TileRequestorZXY target = new TileRequestorZXY();
            int TileZ = 6, TileX = 31, TileY = 21;
            Envelope targetWGS84TileExtents = new Envelope(-5.625, 0, 48.922499263758254, 52.482780222078205);
            Envelope targetMercTileExtents = new Envelope(-626172.1357121654, 0, 6261721.357121639, 6887893.492833804);
            target.GetEnvelopeForTile(TileZ, TileX, TileY, out WGS84TileExtents, out MercTileExtents);
            double delta = DeltaEnvelope(WGS84TileExtents, targetWGS84TileExtents);
            Assert.IsTrue(delta < 0.01, "WGS84 tile envelope test 0.01 - delta=" + delta);
            delta = DeltaEnvelope(MercTileExtents, targetMercTileExtents);
            Assert.IsTrue(delta < 0.1, "Merc tile envelope test 0.1 - delta=" + delta);
            //TODO: might want to add some more tests here...
        }

        /// <summary>
        /// This is a test for a vector tile loaded from a remote URI - so you need to be connected to the Internet.
        /// TODO: The file comes from localhost for now, but this is going to change.
        /// </summary>
        [TestMethod()]
        public void RequestTileTest()
        {
            TileRequestorZXY target = new TileRequestorZXY();
            int TileZ = 6, TileX = 31, TileY = 21;
            vector_tile.Tile VTile;
            bool Success = target.RequestTile("http://localhost/shapefiles/TM_WORLD_BORDERS-0.3.shp", "", TileZ, TileX, TileY, null, out VTile);
            Assert.IsTrue(Success);
        }
    }
}