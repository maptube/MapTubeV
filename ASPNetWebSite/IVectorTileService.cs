﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;

namespace ASPNetWebSite
{
    // NOTE: You can use the "Rename" command on the "Refactor" menu to change the interface name "IVectorTileService" in both code and config file together.
    [ServiceContract]
    public interface IVectorTileService
    {
        [WebGet(UriTemplate = "/{Z}/{X}/{Y}")]
        Stream GetTile(string Z, string X, string Y);
    }
}
