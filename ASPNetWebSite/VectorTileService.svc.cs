using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;
using System.Web;

using MapTubeV;

namespace ASPNetWebSite
{
    // NOTE: You can use the "Rename" command on the "Refactor" menu to change the class name "VectorTileService" in code, svc and config file together.
    // NOTE: In order to launch WCF Test Client for testing this service, please select VectorTileService.svc or VectorTileService.svc.cs at the Solution Explorer and start debugging.
    /// <summary>
    /// Vector Tile service return MapBox Protocol Buffer Format (PBF) tiles.
    /// This version uses a WCF service with REST for the ZXY, rather than using parameter string. The descriptor uri still has to be passed with a parameter string though, because of
    /// it being a uri.
    /// The original MapTubeD endpoints used simple ashx handlers deliberately for compatibility with mono implementations as mono had never implemented the more complex WCF. The structure
    /// is designed as a MapTubeV vector tiler library wrapped in a lightweight web service, so you can wrap it in any web service you like as long as it can run the .net library.
    /// </summary>
    public class VectorTileService : IVectorTileService
    {

        public Stream GetTile(string Z, string X, string Y)
        {
            int TileZ = Convert.ToInt32(Z);
            int TileX = Convert.ToInt32(X);
            int TileY = Convert.ToInt32(Y);

            //time tag? as part of rest contract? or param string?

            //get descriptor from the ?u=... param string - you have to do it this way as REST won't work
            Uri RawUri = OperationContext.Current.IncomingMessageHeaders.To;
            NameValueCollection Params = HttpUtility.ParseQueryString(RawUri.Query);
            string CacheKey = Params["u"];
            if (string.IsNullOrEmpty(CacheKey)) return null;

            MemoryStream ms = new MemoryStream();
            try
            {
                vector_tile.Tile VTile;
                MapTubeV.TileRequestorZXY requestor = new MapTubeV.TileRequestorZXY(); //there's no overhead to creating one of these, or we could make a factory
                requestor.RequestTile(CacheKey,null,TileZ,TileX,TileY,null,out VTile); //TODO: using time tag and context as both null - REMOVE?
                VTile.WriteTo(ms);
            }
            finally
            {
                ms.Position = 0;
            }
            //return a .pbf file
            //prevent output from being cached
            //WebOperationContext.Current.OutgoingResponse.Headers.Add("Cache-Control", "no-cache");
            //WebOperationContext.Current.OutgoingResponse.ContentType = "text/json";
            System.Diagnostics.Debug.WriteLine("Completed tile " + Z + " " + X + " " + Y);
            return ms;
        }
}
