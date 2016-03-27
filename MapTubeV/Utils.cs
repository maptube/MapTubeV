using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace MapTubeV
{
    public static class Utils
    {
        public static string MakeAbsoluteUri(string BasePath, string RelativeUri)
        {
            //from map cache
            //NOTE: there is another copy of this in the TileRequestorBase
            //THIS HAS THE PARAMETERS THE OTHER WAY AROUND TO THE ONE IN TILEREQUESTORBASE!
            //make sure it's not relative
            Uri AbsoluteUri;
            if (!Uri.IsWellFormedUriString(RelativeUri, UriKind.Absolute))
            {
                AbsoluteUri = new Uri(new Uri(BasePath), RelativeUri);
            }
            else
            {
                AbsoluteUri = new Uri(RelativeUri);
            }
            return AbsoluteUri.AbsoluteUri;
        }

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
        public static string GetAbsoluteUri(string CacheKey, string ContextPath)
        {
            //from tile requestor
            Uri DescriptorUri;
            if (!Uri.IsWellFormedUriString(CacheKey, UriKind.Absolute))
            {
                if (ContextPath == null) throw new Exception("Cannot resolve relative URI as no ContextPath provided");
                DescriptorUri = new Uri(new Uri(ContextPath), CacheKey);
            }
            else
            {
                DescriptorUri = new Uri(CacheKey);
            }
            return DescriptorUri.AbsoluteUri;
        }
    }
}