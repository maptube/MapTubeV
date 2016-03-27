using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace MapTubeV
{
    /// <summary>
    /// Helper class to return a version sting which we can use to identify the features present in this version of the vector tiler.
    /// TODO: could add some sort of get capabilities
    /// </summary>
    public class Version
    {
        public static string VersionString
        {
            get
            {
                string s = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
                return s;
            }
        }
    }
}