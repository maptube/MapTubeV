using Microsoft.VisualStudio.TestTools.UnitTesting;
using MapTubeV;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapTubeV.Tests
{
    [TestClass()]
    public class MapCacheTests
    {
        [TestMethod()]
        public void GetMapDescriptorTest()
        {
            Assert.Fail();
        }

        [TestMethod()]
        public void BuildCacheKeyDirTest()
        {
            //This is what we're going to test with a number of different cache keys to see how it performs: string BuildCacheKeyDir(string CacheKey, string TimeTag)

            string CacheDir = ConfigurationManager.AppSettings["GeometryCacheDirectory"];
            
            MapCache Cache = MapCache.GetInstance;
            PrivateObject target = new PrivateObject(Cache);
            string actual;
            string expected;

            //test 1
            //http://localhost/shapefiles/TM_WORLD_BORDERS-0.3.shp maps to localhost\\shapefiles\\TM_WORLD_BORDERS-0\\3\\shp\\00000000\\
            expected = Path.Combine(CacheDir, "localhost\\shapefiles\\TM_WORLD_BORDERS-0\\3\\shp\\00000000\\");
            actual = (string)target.Invoke("BuildCacheKeyDir", new object[] { "http://localhost/shapefiles/TM_WORLD_BORDERS-0.3.shp", null });
            Assert.AreEqual(expected,actual);

            //test 2
            //http://www.maptube.org/shapefiles/MSOA_WGS84.shp maps to www\\maptube\\org\\shapefiles\\MSOA_WGS84\\shp\\00000000\\
            expected = Path.Combine(CacheDir, "org\\maptube\\www\\shapefiles\\MSOA_WGS84\\shp\\00000000\\");
            actual = (string)target.Invoke("BuildCacheKeyDir", new object[] { "http://www.maptube.org/shapefiles/MSOA_WGS84.shp", null });
            Assert.AreEqual(expected, actual);

            //test 3 - what if you don't pass a .shp extension and pass .bin instead? Apparently the cache key will still have .bin in it.
            //http://www.maptube.org/shapefiles/MSOA_WGS84.bin maps to www\\maptube\\org\\shapefiles\\MSOA_WGS84\\shp\\00000000\\
            expected = Path.Combine(CacheDir, "org\\maptube\\www\\shapefiles\\MSOA_WGS84\\bin\\00000000\\");
            actual = (string)target.Invoke("BuildCacheKeyDir", new object[] { "http://www.maptube.org/shapefiles/MSOA_WGS84.bin", null });
            Assert.AreEqual(expected, actual);
        }
    }
}