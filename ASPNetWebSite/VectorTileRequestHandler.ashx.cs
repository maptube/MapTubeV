using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace ASPNetWebSite
{
    /// <summary>
    /// Summary description for VectorTileRequestHandler
    /// </summary>
    public class VectorTileRequestHandler : IHttpHandler
    {

        public void ProcessRequest(HttpContext context)
        {
            //?r=[t|b|v]&u=[descriptoruri](&s=[timetag])
            //r=t&u=[descriptoruri](&s=[timetag])&t=[tilestring] is a request for a tile - s (seconds) is optional
            //r=b&u=[descriptoruri] is a request for the geographic bounds of this descriptor
            //r=v is a request for the tile renderer version number

            string request = context.Request.QueryString["r"];
            if (request == null) request = "t"; //default to tile request if not specified
            if (request == "t") //?r=t&u=[descriptoruri](&s=[timetag])&t=[tilestring] request for a tile
            {
//                ProcessTileRequest(context);
            }
            else if (request == "b") //?r=b&u=[descriptoruri] request for geographic bounds
            {
                //ProcessBoundsRequest(context);
            }
            else if (request == "v") //?r=v request for version number
            {
//                ProcessVersionNumberRequest(context);
            }
            else
            {
                //unknown request
                context.Response.ContentType = "text/plain";
                context.Response.Write("Unrecognised request: " + context.Request.RawUrl);
            }
        }

        private void ProcessTileRequest()
        {
            //TODO: ?
        }

        public bool IsReusable
        {
            get
            {
                return false;
            }
        }
    }
}