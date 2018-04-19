using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Text;
using System.Web;

using NetTopologySuite.Features;
using NetTopologySuite.Index.Quadtree;

namespace MapTubeV
{

    //OK, so the descriptor in this case is the URI of the shapefile. The descriptor for MapTubeD was the xml file itself with the URI to it unique

    /// <summary>
    /// Information stored in the circular buffer about this map. Once it has loaded successfully, IsLoading=true and features will containt the data in the shapefile
    /// referenced by the DescriptorUri.
    /// </summary>
    public class MapDescriptor
    {
        //needed so that all the cache objects derive from this
        public bool IsLoading = false; //lock object on this element of the cache to prevent synchronous thread access
        public string DescriptorUri; //key field
        public string TimeTag; //secondary key field - seconds since 1 Jan 2000 in hex
        public DateTime LoadTime; //time this data was last loaded from the descriptor uri
        //public FeatureCollection features;
        public Quadtree<Feature> Features;
    }


    /// <summary>
    /// Singleton which holds the shapefile features using the URI as the primary key.
    /// This is a circular buffer which loads shapefile data from the URI the first time the data is requested, reprojects it and stores it in the local disk geometry store, but keeps a
    /// copy in memory until it drops out the other end of the circular buffer. Then a request for the same data a second time will find it already reprojected in the geometry store.
    /// The geometry store is flushed by a cache manager when it gets too big.
    /// TODO: need to implement the time tags for data that changes
    /// The reason for not using the generic circular buffer is the thread locking problems when loading from remote uris - need a better thread lock
    /// TODO: implement a daily log file that contains all the work being done downloading and reprojecting shapefiles, where they're coming from, wire time and how long reprojection is
    /// taking, plus any errors so we can look into the failures. Simplest would be to add a daily text file.
    /// </summary>
    public class MapCache
    {
        private static object LockGetInstance = new object(); //thread lock object for GetInstance

        private static MapCache Instance; //singleton

        private object LockGetDescriptor = new object(); //thread lock for GetDescriptor

        #region properties

        protected int CacheSize = 100; //default circular buffer size of 100
        private MapDescriptor[] Cache; //TODO: why isn't this a circular buffer with a fill delegate? - probably due to the delay loading?
        private int CachePos = 0;

        public int MaxCacheSize
        {
            get { return CacheSize; }
        }

        #endregion properties


        /// <summary>
        /// Static property to return the current instance of the MapCache singleton object, or
        /// create it if this is the first time it has been called.
        /// </summary>
        public static MapCache GetInstance
        {
            get
            {
                lock (LockGetInstance)
                {
                    //if the line below is split across threads, you can end up with two MapCaches
                    if (Instance == null) Instance = new MapCache();
                }
                return Instance;
            }
        }

        /// <summary>
        /// Private constructor
        /// </summary>
        private MapCache()
        {
            Cache = new MapDescriptor[CacheSize];
        }

        /// <summary>
        /// Internal function used to find the descriptor in the cache. Either returns the
        /// index of the descriptor entry, or -1 if this descriptor isn't currently in the cache.
        /// If the TimeTag is null or empty, then it returns the first descriptor uri that matches, otherwise
        /// it only returns a valid index if both the descriptor uri and the time tag in the cache match.
        /// </summary>
        /// <param name="DescriptorUri">The Uri of the map descriptor file to find</param>
        /// <param name="TimeTag">Seconds since 00:00:00 1 Jan 2000 in hex</param>
        /// <returns>The index of the MapDescriptor object, or -1 if not found. This does not look up the
        /// MapDataDescriptor if it finds a CompositeDescriptor</returns>
        private int FindDescriptorIndex(string DescriptorUri, string TimeTag)
        {
            //lock (LockGetDescriptor)
            //{
            int i = CachePos;
            do
            {
                i = (i + CacheSize - 1) % CacheSize;
                if (Cache[i] == null) break; //if you find a null one, stop as there won't be any more non-null
                if (Cache[i].DescriptorUri == DescriptorUri)
                {
                    if ((string.IsNullOrEmpty(TimeTag)) || (Cache[i].TimeTag == TimeTag))
                        return i;
                }
            } while (i != CachePos);
            //can't find in memory so return not found
            return -1;
            //}
        }

        /// <summary>
        /// Check the cache for the data belonging to DescriptorUri to see if it has already been loaded.
        /// If it hasn't, then load the csv file data and anything else required from the descriptor file
        /// and place it into the cache. This has been made thread safe as concurrent calls to get the same
        /// data could have caused the same descriptor to be loaded twice.
        /// </summary>
        /// <param name="DescriptorUri">The Uri of the map descriptor file</param>
        /// <param name="TimeTag">Seconds since 00:00:00 1 Jan 2000 in hex</param>
        /// <returns>A MapDataDescriptor which contains the csv file data, a settings file and a render style
        /// which is everything you need to draw a tile from (after you use it to query the geometry from the
        /// database).</returns>
        public MapDescriptor GetMapDescriptor(string DescriptorUri, string TimeTag)
        {
            //TODO: If we have to load a new descriptor, it might be worth checking the available memory first and
            //release all the data if memory is running low. This would prevent the system from locking up if 100x
            //map descriptors exceed the available memory.

            int index = -1, NumTries = 100; //NumTries is the maximum time we're going to wait for a load (100*200ms)
            do
            {
                lock (LockGetDescriptor)
                {
                    index = FindDescriptorIndex(DescriptorUri, TimeTag);
                    if (index >= 0)
                    {
                        if (!Cache[index].IsLoading)
                            return Cache[index];
                        else
                            System.Threading.Thread.Sleep(200); //wait on another thread loading
                    }
                    else
                    {
                        //descriptor not found in cache, so write a temporary descriptor marked as locked
                        MapDescriptor rec = new MapDescriptor();
                        rec.IsLoading = true; //prevent access by other threads until loaded
                        rec.DescriptorUri = DescriptorUri;
                        rec.TimeTag = TimeTag;
                        Cache[CachePos] = rec;
                        CachePos = (CachePos + 1) % CacheSize;
                    }

                }
                --NumTries;
            } while ((index >= 0) && (NumTries > 0));

            if (NumTries < 0) return null; //timed out waiting to load

            //Phase 2 - Descriptor Load
            //at this point, we have released the lock and a temporary descriptor object is in the cache and locked to prevent
            //access by other threads until we have loaded it here
            MapDescriptor loading_rec = LoadFeatureCollection(DescriptorUri); //was ParseMapDescriptor(DescriptorUri)
            if (loading_rec != null)
            {
                loading_rec.DescriptorUri = DescriptorUri;
                loading_rec.IsLoading = false;
                loading_rec.TimeTag = TimeTag; //only field not set in ParseMapDescriptor

                //we find the index again in case somebody's been moving things around in the meantime
                lock (LockGetDescriptor)
                {
                    index = FindDescriptorIndex(DescriptorUri, TimeTag);
                    if (index >= 0)
                        Cache[index] = loading_rec;
                    else
                    {
                        //it could happen - somebody could clear the cache before the load has finished
                        loading_rec.IsLoading = false;
                        loading_rec.DescriptorUri = DescriptorUri;
                        loading_rec.TimeTag = TimeTag;
                        Cache[CachePos] = loading_rec;
                        CachePos = (CachePos + 1) % CacheSize;
                    }
                }
            }
            else
            {
                //it failed to load, so we need to get its descriptor out of the cache - can't just write null as another
                //descriptor could have been loaded above this
                loading_rec = null;
                RemoveDescriptor(DescriptorUri, TimeTag);
            }
            return loading_rec;
        }

        /// <summary>
        /// Remove a descriptor from the cache and shuffle all the cache items to get rid of the gap
        /// </summary>
        /// <param name="DescriptorUri"></param>
        /// <param name="TimeTag"></param>
        /// <returns>True if the descriptor was successfully removed</returns>
        public bool RemoveDescriptor(string DescriptorUri, string TimeTag)
        {
            bool Success = false;
            lock (LockGetDescriptor)
            {
                int index = FindDescriptorIndex(DescriptorUri, TimeTag);
                if (index >= 0)
                {
                    //found descriptor in cache, so reload it
                    Cache[index] = null;
                    int i = index, j = (index + 1) % CacheSize;
                    while ((j != index) && (Cache[j] != null))
                    {
                        Cache[i] = Cache[j];
                        i = (i + 1) % CacheSize;
                        j = (j + 1) % CacheSize;
                    }
                    Success = true;
                }
            }
            return Success;
        }

        /// <summary>
        /// Load the data from the shapefile. Populate local geometry cache, reproject and store collection in circular memory buffer.
        /// At this point, a GetDescriptor request has been made and it's not in the circular memory buffer, so need to look in the local cache
        /// and load from remote URI only if necessary.
        /// TODO: it might be interesting to have a logfile of what is being downloaded and reprojected and how long it was taking
        /// </summary>
        private MapDescriptor LoadFeatureCollection(string DescriptorUri)
        {
            //TODO: check it's an http uri
            //TODO: shapefile load features from uri and cache
            //fill descriptor object and return it for inclusion into the circular buffer
            MapDescriptor data_rec = new MapDescriptor();
            data_rec.DescriptorUri = DescriptorUri;
            data_rec.IsLoading = true;

            List<Feature> features = null;

            //look for it in the local geometry cache
            string path = BuildCacheKeyDir(DescriptorUri, null);
            string GeomCacheDir = ConfigurationManager.AppSettings["GeometryCacheDirectory"];
            path = Path.Combine(GeomCacheDir, path); //path is the directory in the local geom cache dir that the shapefile will be downloaded and reprojected into
            //If the descriptor is http://www.maptube.org/shapefiles/world.shp
            //then path is something like c:\geomcache\www\maptube\org\shapefiles\world\shp
            string RootFilename = Path.GetFileNameWithoutExtension(DescriptorUri); //i.e. world - this is the root name for all files stored e.g. .shp, .prj, .dbf
            string ReprojectedFilename = Path.Combine(path,RootFilename+"_reprojected.shp"); //i.e. c:\geomcache\www\maptube\org\shapefiles\world\shp\world_reprojected.shp
            if (File.Exists(ReprojectedFilename)) //if reprojected exists then load it
            {
                features = ShapeUtils.LoadShapefile(ReprojectedFilename);
            }
            else
            {
                //create directory and load data into it from remote URI
                //NOTE: this is only going to work if the URI identifies a file i.e. www.maptube.org/shapefiles/world.shp as it finds the dbf and prj by changing the extension.
                //In order to make it work with web services, you would need some other method for doing this - maybe REST? www.maptube.org/shapefiles/world/shp ?
                Directory.CreateDirectory(path);
                //load remote
                using (WebClient wc = new WebClient())
                {
                    RequestCachePolicy policy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
                    wc.CachePolicy = policy;
                    string SHPFilename = Path.Combine(path, RootFilename + ".shp"); //full filename of shapefile in local geometry cache
                    string DBFFilename = Path.Combine(path, RootFilename + ".dbf"); //and dbf
                    string PRJFilename = Path.Combine(path, RootFilename + ".prj"); //and prj
                    //download the shapefile
                    wc.DownloadFile(Path.ChangeExtension(DescriptorUri,"shp"), SHPFilename);
                    //then the dbf file
                    wc.DownloadFile(Path.ChangeExtension(DescriptorUri,"dbf"), DBFFilename);
                    //then the prj file
                    wc.DownloadFile(Path.ChangeExtension(DescriptorUri, "prj"), PRJFilename);
                    
                    //now reproject
                    features = ShapeUtils.LoadShapefile(SHPFilename);
                    string prj = ShapeUtils.GetPRJ(SHPFilename);
                    //MercatorProjection.ReprojectOldAndSlow(ref features, prj); //old and slow
                    MercatorProjection.Reproject(ref features, prj); //new and fast (I hope)
                    //and save a new shapefile under the reprojected filename
                    ShapeUtils.WriteShapefile(ReprojectedFilename, features);
                }
            }

            //OK, at this point we've either got a shapefile in the directory and reprojected, or we've failed.
            //Build the spatial index
            data_rec.Features = new Quadtree<Feature>();
            foreach (Feature f in features) data_rec.Features.Insert(f.Geometry.EnvelopeInternal, f);

            return data_rec;
        }

        /// <summary>
        /// Take the CacheKey and build the path to the directory in the cache where its tiles are stored.
        /// Some things worth knowing:
        /// NTFS Reserved Characters: ? " / \ < > * | :
        /// (NOTE: Vista tells you this if you try and create a new folder and type one of these)
        /// URL Reserved Characters: $ & + , / : ; = ? @
        /// URL Unsafe Characters: SPACE " < > # (£ maybe?) % { } | \ ^ ~ [ ] `(grave not ')
        /// </summary>
        /// <param name="CacheKey">The Uri of the descriptor file which uniquely identifies the data.
        /// This is assumed to be an absolute uri, not a relative one.</param>
        /// <param name="TimeTag">Seconds since 00:00:00 1 Jan 2000 in hex. This is used as an additional
        /// directory under the CacheKey so that multiple copied of tile sets can be maintained. The primary
        /// use of the TimeTag is in load balancing so that the http request contains the valid time of the
        /// data (TimeTag). If the TimeTag in the url indicates that later data is available then it can
        /// be loaded. If no TimeTag is specified (null or empty) then "00000000" is used as the directory
        /// by default.</param>
        /// <returns>The path to the directory in the cache containing this CacheKey's tiles</returns>
        protected string BuildCacheKeyDir(string CacheKey, string TimeTag)
        {
            string CacheDir = ConfigurationManager.AppSettings["GeometryCacheDirectory"];

            //Use either method 1 or method 2, not sure which is best

            //Method 1: get a unique directory from the CacheKey by base 64 encoding the full URI
            //This results in a flat directory structure of directories containing all the cache tiles.
            //Using this format might cause problems when Explorer scans the directory due to the millions
            //of files stored one level below.
            //ASCIIEncoding enc = new ASCIIEncoding();
            //string hashcode = Convert.ToBase64String(enc.GetBytes(CacheKey));
            //string path = m_CacheDir + "\\" + hashcode;

            //Method 2: use a hierarchial directory structure
            //e.g. uk/ac/ucl/casa/www/maps/descriptor/xml
            //REMEMBER: 192.168.1.2 needs to appear in this order, not reversed
            //You need to include the name and extension of the descriptor file.
            //NOTE: need to be careful about special chars in path e.g. .., ~ and c:\
            //Descriptors containing parameters get split by parameter and each parameter becomes a cache directory
            //e.g. http://localhost/APIService.svc/descriptor/Area%20Code/Persons2001/ONSDistricts?u=http://www.maptube.org/desc.xml
            //becomes localhost/APIService.svc/descriptor/Area%20Code/Persons2001/ONSDistricts/u%3dhttp%3a%2f%2fwww.maptube.org%2fdesc.xml/[any other params here...]
            Uri uri = new Uri(CacheKey);
            string[] dirs = uri.Host.Split(new char[] { '.' });
            string path = "";
            //e.g. www.maptube.org needs to be org.maptube.www but 128.40.111.193 is not reversed
            if (uri.HostNameType == UriHostNameType.IPv4) //IPV4 not reversed
                for (int i = 0; i < dirs.Length; i++) path += dirs[i] + "\\";
            else //DNS and IPV6 are reversed (although IPV6 doesn't used dot notation)
                for (int i = dirs.Length - 1; i >= 0; i--) path += dirs[i] + "\\";

            //NOTE: add additional dir for port number here (MapTubeD doesn't have this)
            path = path + uri.Port + "\\";

            //First, take care of everything up to the query string part of the uri
            //split dirs on forward and backslash just in case and on the query part or a . so the file
            //extension is a separate folder
            string UriPathOnly = uri.PathAndQuery; //is there an easier way of extracting the path without the query?
            int QPos = UriPathOnly.IndexOf("?");
            if (QPos >= 0) UriPathOnly = UriPathOnly.Substring(0, QPos);
            dirs = UriPathOnly.Split(new char[] { '/', '\\', '.' });
            for (int i = 0; i < dirs.Length; i++) if (dirs[i].Length > 0) path += dirs[i] + "\\";

            //NOTE: I'm completely ignoring the fragment (#frag) part which comes before the query string.

            //Next, take each parameter in the query string and make a dir for each one e.g.
            // ?u=abd&t=xyz becomes \u%3dabd\t%3dxyz (single u= root dir with one sub dir containing t=)
            //NOTE: we keep the leading ? on the first parameter but encode it so we can see it's a query.
            //An = in a url is illegal anyway, so you should never get the situation where a non-query and
            //a query url would share a cache dir e.g. /abc?a=1 and /abc/a=1
            dirs = uri.Query.Split(new char[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < dirs.Length; i++) if (dirs.Length > 0) path += HttpUtility.UrlEncode(dirs[i]) + "\\";

            //Finally, check for any reserved NTFS characters that will have got through
            //? " / \ < > * | : 
            //TODO: use better method e.g. build another string and check inclusion rather than exclusion
            string allowed = @" #$%&'()+,-./0123456789;=@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\]^_`abcdefghijklmnopqrstuvwxyz";
            StringBuilder builder = new StringBuilder(path.Length + 32); //allow some extra length capacity for efficiency
            for (int i = 0; i < path.Length; i++)
            {
                if (allowed.IndexOf(path[i]) < 0)
                {
                    builder.Append(String.Format("%{0:X2}", path[i])); //% followed by 2 digit ascii in hex
                }
                else builder.Append(path[i]);
            }
            path = builder.ToString();

            //A possible method 3 would be to split the server path into dirs and base 64 encode the path and query

            //TODO: check the returned path for .., ~ and c:\
            //This is probably impossible as the CacheKey is a uri and must be visible to the server.
            //Also, it's only going to come out as cachedir\com\server\www\c:\..\ etc, which won't give you access
            //to anything you shouldn't have.

            //Now handle the TimeTag. We're lower casing the hex and limiting to [0-9a-f]
            //Leading zeros shouldn't be a problem as 2011 uses the full 8 characters.
            //TODO: really must find a better way of doing this
            string TimeTagDir = "00000000"; //default directory if no TimeTag supplied
            if (!string.IsNullOrEmpty(TimeTag))
            {
                const string SafeTimeTag = "0123456789abcdef"; //whitelist
                char[] TimeTagChars = TimeTag.ToLower().ToCharArray();
                for (int i = 0; i < TimeTagChars.Length; i++)
                {
                    if (SafeTimeTag.IndexOf(TimeTagChars[i]) < 0)
                        TimeTagChars[i] = '0';
                }
                TimeTagDir = new String(TimeTagChars);
            }

            //path always contains a trailing \\ at this point, and we must exit with a trailing \\
            return CacheDir + "\\" + path + TimeTagDir + "\\";
        }

    }
}