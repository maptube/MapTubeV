using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web;
using System.Web.Security;
using System.Web.SessionState;

namespace ASPNetWebSite
{
    public class Global : System.Web.HttpApplication
    {

        protected void Application_Start(object sender, EventArgs e)
        {
            //TODO: these things vvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvv
            string CacheDir = ConfigurationManager.AppSettings["TileCacheDirectory"];
            float TileCacheMaxSizeGB = Convert.ToSingle(ConfigurationManager.AppSettings["TileCacheMaxSizeGB"]);
            TimeSpan TileCacheOldestAccessTime = TimeSpan.Parse(ConfigurationManager.AppSettings["TileCacheOldestAccessTime"]);
            DateTime TileCacheExpireTime = DateTime.Parse(ConfigurationManager.AppSettings["TileCacheExpireTime"]);
            TimeSpan TileCacheExpireInterval = TimeSpan.Parse(ConfigurationManager.AppSettings["TileCacheExpireInterval"]);
            //
            //int GeometryFeatureCacheSize = Convert.ToInt32(ConfigurationManager.AppSettings["GeometryFeatureCacheSize"]); //in-memory feature circular cache
            //string GeometryCacheDir = ConfigurationManager.AppSettings["GeometryCacheDirectory"]; //local cache of original file on disk
            //int GeometryCacheMaxFiles = Convert.ToInt32(ConfigurationManager.AppSettings["GeometryCacheMaxFiles"]); //number of local geometry sets to cache (1 file = 4 geometry files)

            //singleton instance of a MapCache which is shared between all vector tile renderers
            Application["MapCache"] = MapTubeV.MapCache.GetInstance;

            //creating a cache manager object automatically starts a background thread to run the tile cache expiry process
            MapTubeV.CacheManager CM = new MapTubeV.CacheManager(CacheDir);
            CM.MaxCacheSizeGB = TileCacheMaxSizeGB;
            CM.InitialiseExpiry(TileCacheExpireTime, TileCacheExpireInterval, TileCacheOldestAccessTime);
            Application["CacheManager"] = CM;
        }

        protected void Session_Start(object sender, EventArgs e)
        {

        }

        protected void Application_BeginRequest(object sender, EventArgs e)
        {

        }

        protected void Application_AuthenticateRequest(object sender, EventArgs e)
        {

        }

        protected void Application_Error(object sender, EventArgs e)
        {

        }

        protected void Session_End(object sender, EventArgs e)
        {

        }

        protected void Application_End(object sender, EventArgs e)
        {

        }
    }
}