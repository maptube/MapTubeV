using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.IO;
using System.Data;
using System.Threading;

//using System.Configuration;

//using System.Diagnostics;
//using System.Web.ProcessInfo;

//This is good on file locking in IIS7
//http://mvolo.com/blogs/serverside/archive/2009/03/01/File-Locking-and-Conditional-Delete.aspx

namespace MapTubeV
{
    /// <summary>
    /// TODO: this file was stolen from MapTubeD, so needs to be altered for the different cache file types...
    /// 
    /// Handle all tile cache management e.g. current cache status for an admin web page and
    /// an expiry process that runs in the background.
    /// This expects cache tiles to be in the format of z_x_y.png
    /// TODO: you could build a map of the most requested areas of the world based on the tiles in the cache
    /// TODO: sort out what happens if CacheDir is null - should just run without a cache
    /// TODO: the path to a uri containing a dot is wrong e.g. www.portal.ac.uk/richard.milton@ucl should not split a dir on second dot
    /// </summary>
    public class CacheManager
    {
        private const string EVENT_LOG_NAME = "Application";
        private static System.Diagnostics.EventLog log = new System.Diagnostics.EventLog();

        protected string CacheDir;
        //TODO: these should all come from some sort of configuration file
        private long MaxCacheSizeBytes = 1024 * 1024 * 1024; //Max size of cache in bytes (guideline - cache can still exceed this)

        //TimeSpan OldestDir = new TimeSpan(1, 0, 0, 0); //days, hours, minutes, seconds
        //int MinKeepZoom = 1; //any zoom level <= this is kept unless it fails OldestDir
        //abolished min file size as it's better to keep a small frequently used file than to have to re-render it
        //long MinFileSize = 3 * 1024; //files less than this size in bytes get expired

        private DateTime ExpireTime=DateTime.Now; //this is the base time that the interval is calculated from
        private TimeSpan ExpireInterval=new TimeSpan(0,23,0,0); //default to 1 day
        private TimeSpan OldestAccessTime=new TimeSpan(7,0,0,0); //default to a week
        private DateTime LastExpireTime; //for the web page, last time expire ran
        private DateTime NextExpireTime = DateTime.Now+new TimeSpan(1,0,0,0); //next run 1 day from now
        private bool ExpireRunning = false; //true if the expire process is running

        //const string ImageFileExt = ".png";
        const string ImageFileExt = "(.shp|.prj|.dbf)";

        /// <summary>
        /// Max size of the cache in GB. This is only a guide for the expiry so the cache can exceed this.
        /// </summary>
        public float MaxCacheSizeGB
        {
            get { return MaxCacheSizeBytes/(1024*1024*1024); }
            set { MaxCacheSizeBytes = (int)(value*1024.0f*1024.0f*1024.0f); }
        }

        public DateTime ExpirePreviousRun
        {
            get { return LastExpireTime; }
        }

        public DateTime ExpireNextRun
        {
            get { return NextExpireTime; }
        }

        public TimeSpan ExpireRunInterval
        {
            get { return ExpireInterval; }
        }

        public string ExpireProcessStatus
        {
            get { return (ExpireRunning) ? "Running" : "Idle"; }
        }

        /// <summary>
        /// Get the creation time of the given Cache. This is just the datestamp on the root directory.
        /// This is NOT the descriptor, but must be the directory obtained from the cached tile requestor
        /// as the cache manager does not deal with descriptors.
        /// The returned time is in UTC.
        /// </summary>
        /// <param name="CacheDir">The directory of the cache to get the creation time for. This is NOT the descriptor Uri.</param>
        /// <returns>The cache creation time</returns>
        public static DateTime GetCacheCreationTimeUTC(string CacheDir)
        {
            DirectoryInfo di = new DirectoryInfo(CacheDir);
            return di.CreationTimeUtc;
        }

        private Thread ExpireBackgroundThread; //This runs the cache expiry in the background

        /// <summary>
        /// Constructor for a CacheManager. By creating this object, the expiry will run automatically in the
        /// background. Cache query and display functions are all static.
        /// </summary>
        /// <param name="CacheDirectory">The directory that this cache manager is managing. This can be null
        /// and it will run without a cache.</param>
        public CacheManager(string CacheDirectory)
        {
            log.Source = EVENT_LOG_NAME;

            CacheDir = CacheDirectory;
            if (CacheDir != null)
            {
                if (CacheDir.Length == 0)
                    CacheDir = null;
                else
                    //strip any trailing \ on the cache directory path
                    if (CacheDir.EndsWith("\\")) CacheDir = CacheDir.Substring(0, CacheDir.Length - 1);
            }

            if (CacheDir!=null)
            {
                //Create the expire thread that's going to run in the background
                ExpireBackgroundThread = new Thread(new ThreadStart(ExpireCacheForever));
                ExpireBackgroundThread.IsBackground = true;
                ExpireBackgroundThread.Start();
            }
        }

        /// <summary>
        /// Initialise all the information controlling how frequently the expiry process runs and which
        /// files it will delete.
        /// </summary>
        /// <param name="ExpireTime"></param>
        /// <param name="ExpireInterval"></param>
        /// <param name="OldestAccessTime"></param>
        public void InitialiseExpiry(DateTime ExpireTime, TimeSpan ExpireInterval, TimeSpan OldestAccessTime)
        {
            if (ExpireBackgroundThread != null) //if it's not defined, we're running without a cache
            {
                System.Diagnostics.Debug.WriteLine("InitialiseExpiry: " + ExpireInterval.ToString());
                this.ExpireTime = ExpireTime;
                this.ExpireInterval = ExpireInterval;
                this.OldestAccessTime = OldestAccessTime;
                this.NextExpireTime = this.ExpireTime;
                //work out next expire time from the initial time, adding the interval until we exceed the current time (now)
                DateTime now = DateTime.Now;
                while (this.NextExpireTime < now) this.NextExpireTime += this.ExpireInterval;
                ExpireBackgroundThread.Interrupt(); //this causes it to wake from sleep and recalculate the next wake time
            }
        }

        /// <summary>
        /// Get an enumeration of all the tile renderers currently active - is this possible?
        /// </summary>
        /// <returns></returns>
        //public Enumerator GetTileRenderers()
        //{
            /*Process proc=Process.GetCurrentProcess();
            ProcessThreadCollection threads = proc.Threads;
            foreach (ProcessThread thread in threads)
            {
                if (thread.
            }*/
            //system.web...
            //ProcessInfo pi = ProcessModelInfo.GetCurrentProcessInfo();
        //}

        /// <summary>
        /// Return a list of all the cache end points, plus the number of tiles and physical size on disk
        /// and age?
        /// Designed to be viewable on a web page e.g. returns a dataset that can be the datasource for a datagrid
        /// </summary>
        public static DataSet EnumerateCache(string CacheDir)
        {
            //System.Diagnostics.Debug.WriteLine("Running EnumerateCache");
            if (string.IsNullOrEmpty(CacheDir)) return null; //no cache

            //strip any trailing \ on the cache directory path
            if (CacheDir.EndsWith("\\")) CacheDir = CacheDir.Substring(0, CacheDir.Length - 1);
            DataTable dt = EnumerateCache(new DirectoryInfo(CacheDir));
            DataSet ds = new DataSet();
            ds.Tables.Add(dt);
            return ds;
        }

        /// <summary>
        /// Scan all the cache directories recursively for ones that contain files (the cache endpoints)
        /// and return a list.
        /// </summary>
        /// <param name="di">Root directory</param>
        /// <returns>List of directories, file counts and sizes</returns>
        private static DataTable EnumerateCache(DirectoryInfo di)
        {
            DataTable result = new DataTable("Results");
            result.Columns.Add(new DataColumn("path", Type.GetType("System.String")));
            result.Columns.Add(new DataColumn("count", Type.GetType("System.Int32")));
            result.Columns.Add(new DataColumn("bytes", Type.GetType("System.Int64")));

            if (di.GetDirectories().Length==0)
            {
                //it's an endpoint as no child dirs (a tile cache), so get its size and add it to the return data
                long size = 0;
                foreach (FileInfo file in di.GetFiles())
                {
                    size += file.Length;
                }
                DataRow row = result.NewRow();
                row["path"] = di.FullName;
                row["count"] = di.GetFiles().Length; //number of files in this dir
                row["bytes"] = size;
                result.Rows.Add(row);
                //System.Diagnostics.Debug.WriteLine(string.Format("{0} count={1} size={2}", di.FullName, count, size));
            }

            //now recurse child directories
            foreach (DirectoryInfo child in di.GetDirectories())
            {
                DataTable dt=EnumerateCache(child);
                foreach (DataRow row in dt.Rows)
                {
                    //a concat function would be good
                    DataRow newrow = result.NewRow();
                    newrow.ItemArray = row.ItemArray;
                    result.Rows.Add(newrow);
                }
            }
            return result;
        }

        /// <summary>
        /// Flush all the image files in the cache but keep the directories
        /// </summary>
        public void FlushFiles()
        {
            if (string.IsNullOrEmpty(CacheDir)) return; //no cache to flush

            //System.Diagnostics.Debug.WriteLine("FlushFiles");
            FlushFiles(new DirectoryInfo(CacheDir));
        }

        /// <summary>
        /// Flush files from a directory string pointing to a part of the cache. Used to flush the cache used by
        /// a particular CacheKey using the string returned by EnumerateCache.
        /// </summary>
        /// <param name="dir">The directory to flush as a full path</param>
        public static void FlushFiles(string dir)
        {
            FlushFiles(new DirectoryInfo(dir));
        }

        /// <summary>
        /// Overloaded Flush Files to only flush the image files from given directory e.g. pass it the whole cache dir
        /// or only one uri end points to only flush that part of the cache directory tree.
        /// </summary>
        /// <param name="di">The top of the directory tree to flush all the files from</param>
        private static void FlushFiles(DirectoryInfo di)
        {
            //TODO: check how this copes with 1,000,000 files in a directory!
            //TODO: allow a retry if a file couldn't be deleted so that if a file is being read when
            //we try to delete it we have another go at the end of the cycle. At the moment, this causes
            //a cache flush to fail.

            //delete all the image files in this directory
            foreach (FileInfo file in di.GetFiles("*" + ImageFileExt)) //*.png so we can't delete anything else accidentally
                //file.Delete();
                SafeDelete(file); //returns true if file was actually deleted

            //and then recursively do the same for all the sub directories
            foreach (DirectoryInfo child in di.GetDirectories())
                FlushFiles(child);
        }

        /// <summary>
        /// Delete all the files from the path and the directory immediately above them. This leaves a hanging
        /// directory structure which might have nothing below it, but this gets pruned during the expire
        /// phase. If the delete was the result of a cache flush then it doesn't make sense to get rid of the
        /// whole structure anyway.
        /// </summary>
        /// <param name="path">The directory path to delete</param>
        public static void Delete(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path,true); //this only deletes the final dir on the path containing the files
        }

        /// <summary>
        /// Only flush the files from a specific key
        /// </summary>
        /// <param name="CacheKey">The cache key to flush</param>
        /// <param name="ContextPath">The web context path if cache key is relative, or null</param>
        //This is going to get removed and be replaced with a call to the cachedtilerequestor to get the
        //specific directory for the cachekey and a call to flush that specific dir
        /*public void FlushFiles(string CacheKey, string ContextPath)
        {
            CacheKey = GetAbsoluteUri(CacheKey, ContextPath);
            string path = BuildCacheKeyDir(CacheKey);
            if (Directory.Exists(path))
                FlushFiles(new DirectoryInfo(path));
        }*/

        /// <summary>
        /// This deletes the directory as well as everything in it
        /// </summary>
        /// <param name="CacheKey">The cache key directory to delete</param>
        /// <param name="ContextPath">The web context path if cache key is relative, or null</param>
        //Same as FlushFiles(string CacheKey, string ContextPath) above
        /*public void Delete(string CacheKey, string ContextPath)
        {
            CacheKey = GetAbsoluteUri(CacheKey, ContextPath);
            string path = BuildCacheKeyDir(CacheKey);
            if (Directory.Exists(path))
                Directory.Delete(path); //TODO: check that this will delete a non-empty directory
        }*/

        /// <summary>
        /// Return the size of a directory and all its children in bytes
        /// </summary>
        /// <param name="di">Root directory</param>
        /// <returns>Size in byes</returns>
        private static long DirectorySize(DirectoryInfo di)
        {
            long size = 0;
            foreach (FileInfo file in di.GetFiles())
            {
                size += file.Length;
            }
            //child directories
            foreach (DirectoryInfo child in di.GetDirectories())
            {
                size += DirectorySize(child);
            }
            return size;
        }

        /// <summary>
        /// Expire files in the cache based on some criteria.
        /// This is the method run in the background by the ExpireBackgroundThread to do the cache expiry.
        /// </summary>
        public void ExpireCache()
        {
            try
            {
                ExpireRunning = true;
                log.WriteEntry("MapTubeD: Expire process starting at " + DateTime.Now, System.Diagnostics.EventLogEntryType.Information);
                //System.Diagnostics.Debug.WriteLine("MapTubeD: Expire process starting at " + DateTime.Now);
                if (CacheDir == null)
                {
                    log.WriteEntry("MapTubeD: No cache to expire", System.Diagnostics.EventLogEntryType.Information);
                }
                else
                {
                    DirectoryInfo di = new DirectoryInfo(CacheDir);
                    long size = DirectorySize(di); //how long does this take on a big directory cache?
                    //System.Diagnostics.Debug.WriteLine("MapTubeD: Expire Cache size=" + size + " bytes " + DateTime.Now);
                    log.WriteEntry("MapTubeD: Expire Cache size=" + size + " bytes", System.Diagnostics.EventLogEntryType.Information);
                    if (size > MaxCacheSizeBytes)
                        ExpireCache(di);
                }
                log.WriteEntry("MapTubeD: Expire process finished at " + DateTime.Now, System.Diagnostics.EventLogEntryType.Information);
                //System.Diagnostics.Debug.WriteLine("MapTubeD: Expire process finished at " + DateTime.Now);
            }
            finally
            {
                ExpireRunning = false;
            }
        }

        /// <summary>
        /// Method called by the ExpireBackgroundThread to run the expire process continuously in an infinite loop
        /// </summary>
        private void ExpireCacheForever()
        {
            while (true)
            {
                if (DateTime.Now >= this.NextExpireTime)
                {
                    this.LastExpireTime = DateTime.Now;
                    ExpireCache();

                    //just to make sure the expire won't run again immediately, make sure the next run is
                    //in the future
                    DateTime now = DateTime.Now;
                    while (this.NextExpireTime <= now) this.NextExpireTime += this.ExpireInterval;
                }
                
                //sleep until the next time, but you might get woken up before this
                TimeSpan sleep = this.NextExpireTime - DateTime.Now;
                if (sleep > TimeSpan.Zero)  //bad things happen if you try to sleep for a negative time
                {
                    try
                    {
                        Thread.Sleep(sleep);
                    }
                    catch (Exception ex) { } //interrupted exception
                }
            }
        }

        /// <summary>
        /// Delete a file by obtaining an exclusive lock on it to make sure nobody else is accessing it,
        /// then deleting it in one indivisible operation.
        /// </summary>
        /// <param name="file">The file to delete</param>
        /// <returns>true if the file was deleted, false if it was in use and couldn't be deleted</returns>
        private static bool SafeDelete(FileInfo file)
        {
            bool Deleted = false;
            //FileShare.Delete allows file to be deleted by someone else e.g. us inside the block
            try
            {
                using (FileStream lockFile = new FileStream(file.FullName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Delete))
                {
                    File.Delete(file.FullName); //calls Win32 DeleteFile()
                    Deleted = true;
                }
            }
            catch { }
            return Deleted;
        }

        /// <summary>
        /// The actual expiry of the cache directory files.
        /// TODO: you might want to sleep at some point?
        /// </summary>
        private void ExpireCache(DirectoryInfo di)
        {
            //TODO: An expire log might be useful?
            //TODO: add an empty directory delete so that directories containing no tiles don't get left behind
            //e.g. from a Delete operation.

            //check if dir is read only and always leave it if it is - allows you to lock parts of the cache
            //TODO: check that the cachedrequestor can create tiles in a read only dir?
            //TODO: also check, as these dirs seem to be readonly by default for some reason i.e. never expire!
            if ((di.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly) return;

            if ((di.LastAccessTime - DateTime.Now) > this.OldestAccessTime)
            {
                //TODO: I don't have a safe delete for whole directories, but as these are expired on last access time
                //being a long time in the past, the probability of a cache tile being requested from this directory
                //at the exact moment we delete it is very unlikely. They'll just get a missing tile on the map.
                di.Delete(true); //delete this node and everything below it
            }
            else
            {
                //recurse down dirs and prune old ones
                foreach (DirectoryInfo child in di.GetDirectories())
                    ExpireCache(child);

                //now move on to files in this dir - might not be any if it's not a root node
                //Step 1: Get rid of anything that is zoom level 20 or deeper (shouldn't really happen? sat goes to 21?)
                foreach (FileInfo file in di.GetFiles("2?_*" + ImageFileExt))
                    SafeDelete(file);

                //Step 2: Start going backwards through the zoom levels
                for (int z = 19; z >= 0; z--)
                {
                    foreach (FileInfo file in di.GetFiles(string.Format("{0}_*{1}", z, ImageFileExt))) //z_*.png
                    {
                        long size = file.Length;

                        //I was assuming there would be another criteria to base the decision to expire on. Originally
                        //this included file size (e.g. small=delete on basis that they are quick to render), but
                        //this proved to be a waste of time.
                        bool timecriteria = (file.LastAccessTime - DateTime.Now) > this.OldestAccessTime;
                        if (timecriteria)
                        {
                            SafeDelete(file);
                        }
                    }
                }
            }
        }


    }
}
