using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;

using NetTopologySuite.Geometries;

//NOT USED
namespace MapTubeV
{
    //todo: this changes to an nts feature
    public class CustomFeature
    {
        public Geometry the_geom;
        //public Hashtable Attributes; //never used - you could tie the attribute data into the geometry cache though
    }

    //cache of feature collection keyed on dataset uri - owns a circular buffer of features
    public class GeometryCache
    {
        private static object LockGetInstance = new object(); //thread lock object for GetInstance
        //NOT USED private object LockGetDescriptor = new object(); //thread lock for GetGeometry
        private static object LockGeometryFileCache = new object(); //thread lock used when copying geometry files from remote location to local cache

        //These are set in Initialise.
        private string LocalCacheRoot = ""; //Local location where remote geometry sets are copied to on demand
        private int MaxLocalFileLimit = 10; //Number of geometry sets to cache locally. Oldest gets deleted, but will load again on demand.


        private static GeometryCache Instance; //singleton

        //private GeoDataSources GeoDataSourceInfo;
        private CircularBuffer<CustomFeature> Cache = new CircularBuffer<CustomFeature>();

        /// <summary>
        /// Static property to return the current instance of the GeometryCache singleton object, or
        /// create it if this is the first time it has been called.
        /// </summary>
        public static GeometryCache GetInstance
        {
            get
            {
                lock (LockGetInstance)
                {
                    //if the line below is split across threads, you can end up with two DataCaches
                    if (Instance == null) Instance = new GeometryCache();
                }
                return Instance;
            }
        }

        protected GeometryCache() : base()
        {
#if (DEBUG)
            System.Diagnostics.Debug.WriteLine("CONSTRUCTOR: GeometryCache");
#endif
        }

        /// <summary>
        /// Initialisation procedure to set the geometry cache directory and memory cache size. This is
        /// separate from the constructor as the constructor must be parameter-less.
        /// </summary>
        /// <param name="ResourceFile">The geodatasources.xml file containing the remote custom geometry locations</param>
        /// <param name="MaxCacheSize">The size of the geometry cache circular buffer (feature based cache)</param>
        /// <param name="LocalCacheRoot">The location on disk of the local file cache where remote data is copied to before loading into the feature cache</param>
        /// <param name="MaxLocalFileLimit">The maximum number of remote file sets to cache locally. Oldest gets deleted, but can load again on demand.</param>
        public void Initialise(/*string ResourceFile,*/ int MaxCacheSize, string LocalCacheRoot, int MaxLocalFileLimit)
        {
            //GeoDataSourceInfo = GeoDataSources.GetInstance(@ResourceFile);
            Cache.BufferSize = MaxCacheSize;
            this.LocalCacheRoot = LocalCacheRoot;
            this.MaxLocalFileLimit = MaxLocalFileLimit;
        }

        /// <summary>
        /// Move geometry file from remote location to local file system for performance reasons. Geometry is loaded
        /// one feature at a time rather than all in one go, so many (>200,000) separate requests to read from
        /// a network share would take a huge amount of time. Introduce another level of caching for geometry so that
        /// the whole geometry file is first loaded onto the local file system and then seek is used to pull out
        /// the requested features into the fine-grained geometry cache.
        /// NOTE: you only need to do this for the geometry file as the attributes and spatial index are loaded in
        /// a single operation. Only geometry is loaded piecewise.
        /// </summary>
        /// <param name="CacheKey">The path and filename relative to the root of the geometry cache
        /// e.g. www.maptube.org/userid/guid/basename</param>
        public void CacheGeometryFile(string CacheKey)
        {
/*            if (string.IsNullOrEmpty(LocalCacheRoot)) return; //not using a local cache, so just return

            string LocalBase = GetLocalBaseFilename(CacheKey); //includes basename, but no extension on local system
            string LocalGeomFilename = LocalBase + GeometryFileDB.GeomFileExt;
            string LocalParentDir = Path.GetDirectoryName(LocalGeomFilename);
            //I'm using a critical section lock here as access to the geometry files is multi-threaded. Many tiles being drawn on separate threads
            //simultaneously test the cache, but must wait for the first request to complete the file copy from the remote location to the local
            //file system. This will lock out all access to geometry, even if a request for a different geometry set comes in, but the remote location
            //will be in the local server group, so the copy is unlikely to take very long. In the meantime, access to geometry in the memory cache
            //is not affected, so any features already cached there can be used without waiting.
            lock (LockGeometryFileCache)
            {
                //this is a better test than just !Directory.Exists as it will retry if a copy failure occurred
                if (!Directory.Exists(LocalParentDir) || (Directory.GetFiles(LocalParentDir).Length < 4))
                {
                    //make directory in local cache
                    Directory.CreateDirectory(LocalParentDir);
                    //copy files from remote location to local location
                    string RemoteRoot, RemoteUsername, RemotePassword; //UNC and share name on remote, username and password to access the resource
                    string RemoteBase = GetRemoteBaseFilename(CacheKey, out RemoteRoot, out RemoteUsername, out RemotePassword); //RemoteBase includes basename, but no extension
                    try
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(RemoteUsername))
                            {
                                string Error = MapTubeD.Windows.WindowsNetworking.ConnectToRemote(RemoteRoot, RemoteUsername, RemotePassword);
                                if (!string.IsNullOrEmpty(Error))
                                    throw new TileException("Error loading from " + RemoteRoot + " Error is: " + Error);
                            }
                            //now copy all four geometry files to the local cache location
                            File.Copy(RemoteBase + GeometryFileDB.GeomFileExt, LocalGeomFilename);
                            File.Copy(RemoteBase + GeometryFileDB.AttrFileExt, LocalBase + GeometryFileDB.AttrFileExt);
                            File.Copy(RemoteBase + GeometryFileDB.IdxFileExt, LocalBase + GeometryFileDB.IdxFileExt);
                            File.Copy(RemoteBase + GeometryFileDB.SpatialIdxFileExt, LocalBase + GeometryFileDB.SpatialIdxFileExt);
                        }
                        catch (Exception ex)
                        {
                            throw new TileException("Error accessing remote geometry source: " + ex.Message);
                        }
                    }
                    finally
                    {
                        if (!string.IsNullOrEmpty(RemoteUsername))
                            MapTubeD.Windows.WindowsNetworking.DisconnectRemote(RemoteRoot);
                    }
                    //Delete oldest file if more than MaxFileLimit
                    DirectoryInfo di = new DirectoryInfo(LocalCacheRoot);
                    FileInfo[] files = di.GetFiles("*.geom", SearchOption.AllDirectories);
                    if (files.Length > this.MaxLocalFileLimit)
                    {
                        //sort files by creation time, oldest first
                        var sorted = from s in files
                                     orderby s.CreationTime ascending
                                     select s;
                        int count = files.Length - MaxLocalFileLimit;
                        //delete top "count" files i.e. the oldest to get below our max limit
                        for (int i = 0; i < count; i++)
                        {
                            string FullName = sorted.ElementAt(i).FullName;
                            string ParentDir = Path.GetDirectoryName(FullName);
                            string BaseName = Path.GetFileNameWithoutExtension(FullName);
                            File.Delete(Path.Combine(ParentDir, BaseName + GeometryFileDB.GeomFileExt));
                            File.Delete(Path.Combine(ParentDir, BaseName + GeometryFileDB.AttrFileExt));
                            File.Delete(Path.Combine(ParentDir, BaseName + GeometryFileDB.IdxFileExt));
                            File.Delete(Path.Combine(ParentDir, BaseName + GeometryFileDB.SpatialIdxFileExt));
                            //and prune the directory...
                            DirectoryInfo dir = Directory.GetParent(FullName);
                            do
                            {
                                if (dir.GetFiles().Length > 0) break; //not empty
                                if (dir.GetDirectories().Length > 0) break; //not empty
                                dir.Delete(); //delete empty directory
                                dir = Directory.GetParent(ParentDir);
                            } while (Path.GetFullPath(di.FullName) != Path.GetFullPath(dir.FullName)); //until we get back to the root
                        }
                    }
                }
            }
*/
        }

        /// <summary>
        /// Get the remote directory and basename for a cache key containing the identifier as element 0.
        /// The format is:
        /// www.maptube.org/0/f9fcae90-d524-4d02-bb0b-233d0f8a018b/TM_WORLD_BORDERS-0.2
        /// where www.maptube.org is the key to look up the datasource in GeoDataSources object,
        /// /0/f9f...018b/TM_WORLD_BORDERS-0.2 is the user id, guid and base filename for the data drop.
        /// </summary>
        /// <param name="Key">The key as above</param>
        /// <param name="Root">The UNC path and share name used to access the network resource</param>
        /// <param name="Username">The username for the Root network resource, or null</param>
        /// <param name="Password">The password for the Root network resource, or null</param>
        /// <returns>The full path to the GeometryDB data files, including the basename of the actual data files but no extension</returns>
        public string GetRemoteBaseFilename(string Key, out string Root, out string Username, out string Password)
        {
            /*
                        string[] Dirs = Key.Split(new char[] { '/', '\\' });
                        //string Root = GeoDataSourceInfo.GetGeometrySource(Dirs[0]); //e.g. www.maptube.org
                        GeometrySource source = GeoDataSourceInfo.GetGeometrySourceRecord(Dirs[0]); //e.g. www.maptube.org
                        Root = source.Directory; //need to keep an unmodified version in addition to BaseDir
                        Username = source.Username;
                        Password = source.Password;
                        //now add the relative path to the file extracted from the key to the root directory for the datasource
                        string BaseDir = Root;
                        for (int i = 1; i < Dirs.Length; i++)
                            BaseDir = Path.Combine(BaseDir, Dirs[i]);
                        return BaseDir;
            */
            Root = ""; Username = ""; Password = "";
            return "";
        }

        /// <summary>
        /// Get the base filename in the local cache for the file identified by the cache key. If the local cache location is null or empty, then
        /// we're not using a local cache (only one tile renderer?) and so the local and remote caches point to the same location (i.e. the remote)
        /// </summary>
        /// <param name="Key">The cache key (see GetBaseFilename)</param>
        /// <returns></returns>
        public string GetLocalBaseFilename(string Key)
        {
            /*
                        string[] Dirs = Key.Split(new char[] { '/', '\\' });
                        string BaseDir = LocalCacheRoot;
                        if (string.IsNullOrEmpty(BaseDir))
                            BaseDir = GeoDataSourceInfo.GetGeometrySource(Dirs[0]);
                        //now add the relative path to the file extracted from the key to the root directory for the datasource
                        for (int i = 1; i < Dirs.Length; i++)
                            BaseDir = Path.Combine(BaseDir, Dirs[i]);

                        return BaseDir;
            */
            return "";
        }

        /// <summary>
        /// Fill delegate for the geometry cache.
        /// </summary>
        /// <param name="Key">The base filename which uniquely identifies this dataset, including an additional "/f0" on
        /// the end to identify the feature number being requested. The format is:
        /// www.maptube.org/0/f9fcae90-d524-4d02-bb0b-233d0f8a018b/TM_WORLD_BORDERS-0.2/f0
        /// where www.maptube.org is the key to look up the datasource in GeoDataSources object,
        /// /0/f9f...018b/TM_WORLD_BORDERS-0.2 is the user id, guid and base filename for the data drop
        /// and f0 is requesting feature zero.</param>
        /// <returns>A custom feature</returns>
        public CustomFeature LoadData(string Key)
        {
            /*
                        CustomFeature f = new CustomFeature();

                        //this is a copy of GetCacheDir, but with extra code to cope with the feature id on the end
                        string[] Dirs = Key.Split(new char[] { '/', '\\' });
                        //string Root = GeoDataSourceInfo.GetGeometrySource(Dirs[0]); //e.g. www.maptube.org
                        //GeometrySource GeomSource = GeoDataSourceInfo.GetGeometrySourceRecord(Dirs[0]); //e.g. www.maptube.org

                        string strFID = Dirs[Dirs.Length - 1].Substring(1); //strip leading f
                        int FID = Convert.ToInt32(strFID);
                        //now add the relative path to the file extracted from the key to the root directory for the datasource
                        //string BaseDir = Root;
                        //string BaseDir = GeomSource.Directory;
                        string BaseDir = LocalCacheRoot; //geometry file already cached locally
                        for (int i = 1; i < Dirs.Length - 1; i++)
                            BaseDir = Path.Combine(BaseDir, Dirs[i]);

                        f.the_geom = GeometryFileDB.GetGeometry(BaseDir, FID);

                        //TODO: need to do attributes as well
                        //and do a join with a csv file if necessary
                        //NO? This is only a single feature, so you don't need to de-serialize the whole attribute data
                        //The datatable is part of the regular descriptor
                        //DataTable dt
                        //string AttrFilename = Path.Combine(this.CacheDir, DestDir + "/" + BaseFileName + "/" + BaseFileName + ".attr";
                        //using (FileStream fsAttr = new FileStream(AttrFilename, FileMode.Create))
                        //{
                        //    binFormat.Serialize(fsAttr, dt);
                        //}

                        return f;
            */
            return null;
        }

        /// <summary>
        /// Return the feature (geometry and attributes) given the dataset ID (CacheKey)
        /// and feature number (FID).
        /// First, the in-memory cache is checked for the data, then the data is loaded
        /// from the disk store.
        /// </summary>
        /// <param name="CacheKey">The path and filename relative to the root of the geometry cache. This includes
        /// an additional "/f0" on the base filename to indicate the feature number and make the keys unique over
        /// datasets and individual features</param>
        /// <param name="FID">The feature id number to load</param>
        public CustomFeature GetFeature(string CacheKey, int FID)
        {
            CacheGeometryFile(CacheKey); //move files from remote location to local cache for speed
            //adding /f0 onto the key to identify the FID number being requested
            CustomFeature f = Cache.Get(CacheKey + "/f" + Convert.ToString(FID), this.LoadData);
            return f;
        }



    }
}