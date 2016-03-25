using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using GeoAPI.Geometries;
using NetTopologySuite;
using NetTopologySuite.CoordinateSystems.Transformations;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.Operation.Polygonize;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;


namespace MapTubeV
{
    public class ShapeUtils
    {
        const int DoubleLength = 18;
        const int DoubleDecimals = 8;
        const int IntLength = 10;
        const int IntDecimals = 0;
        const int StringLength = 254;
        const int StringDecimals = 0;
        const int BoolLength = 1;
        const int BoolDecimals = 0;
        const int DateLength = 8;
        const int DateDecimals = 0;

        public static List<Feature> LoadShapefile(string InFilename)
        {
            GeometryFactory geomFactory = new GeometryFactory();
            ShapefileDataReader shapeFileDataReader = new ShapefileDataReader(InFilename, geomFactory);

            ShapefileHeader shpHeader = shapeFileDataReader.ShapeHeader;

            DbaseFileHeader header = shapeFileDataReader.DbaseHeader;

            List<Feature> features = new List<Feature>();
            while (shapeFileDataReader.Read())
            {
                Feature feature = new Feature();
                AttributesTable attributesTable = new AttributesTable();
                string[] keys = new string[header.NumFields];
                IGeometry geometry = (Geometry)shapeFileDataReader.Geometry;
                for (int i = 0; i < header.NumFields; i++)
                {
                    DbaseFieldDescriptor fldDescriptor = header.Fields[i];
                    keys[i] = fldDescriptor.Name;
                    attributesTable.AddAttribute(fldDescriptor.Name, shapeFileDataReader.GetValue(i));
                }
                feature.Geometry = geometry;
                feature.Attributes = attributesTable;
                features.Add(feature);
            }
            //Close and free up any resources
            shapeFileDataReader.Close();
            shapeFileDataReader.Dispose();

            return features;
        }

        /// <summary>
        /// Write shapefile, with the header columns coming from the first feature's attribute table, which may or may not come out in the right order.
        /// </summary>
        /// <param name="OutFilename"></param>
        /// <param name="features"></param>
        public static void WriteShapefile(string OutFilename, List<Feature> features)
        {
            DbaseFileHeader DBFHeader = ShapefileDataWriter.GetHeader((IFeature)features[0], features.Count);
            WriteShapefile(OutFilename, DBFHeader, features);
        }

        /// <summary>
        /// Write shapefile, passing in a header which the user creates himself.
        /// TODO: doesn't write the prj file
        /// </summary>
        /// <param name="OutFilename"></param>
        /// <param name="DBFHeader"></param>
        /// <param name="features"></param>
        public static void WriteShapefile(string OutFilename, DbaseFileHeader DBFHeader, List<Feature> features)
        {
            ShapefileDataWriter writer = new ShapefileDataWriter(OutFilename);
            writer.Header = DBFHeader;
            List<IFeature> ifs = new List<IFeature>();
            foreach (Feature f in features) ifs.Add(f);
            writer.Write((IList<IFeature>)ifs);
        }

        //add column?

        /// <summary>
        /// Generate a DBF header for when I'm creating a shapefile clone.
        /// </summary>
        /// <param name="columns"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public static DbaseFileHeader GetDBFHeader(DataColumnCollection columns, int count)
        {
            var header = new DbaseFileHeader(Encoding.UTF8);
            header.NumRecords = count;
            foreach (DataColumn col in columns)
            {
                var name = col.ColumnName;
                Type type = col.DataType;
                if (type == typeof(double) || type == typeof(float))
                    header.AddColumn(name, 'N', DoubleLength, DoubleDecimals);
                else if (type == typeof(short) || type == typeof(ushort) ||
                         type == typeof(int) || type == typeof(uint) ||
                         type == typeof(long) || type == typeof(ulong))
                    header.AddColumn(name, 'N', IntLength, IntDecimals);
                else if (type == typeof(string))
                    header.AddColumn(name, 'C', StringLength, StringDecimals);
                else if (type == typeof(bool))
                    header.AddColumn(name, 'L', BoolLength, BoolDecimals);
                else if (type == typeof(DateTime))
                    header.AddColumn(name, 'D', DateLength, DateDecimals);
                else
                    header.AddColumn(name, 'C', StringLength, StringDecimals);
            }
            return header;
        }

        /// <summary>
        /// Pass it the .shp filename, not the prj
        /// </summary>
        /// <param name="InFilename"></param>
        /// <returns></returns>
        public static string GetPRJ(string InFilename)
        {
            string prj = "";
            string shpFile = Path.ChangeExtension(InFilename, "prj");
            if (File.Exists(InFilename))
            {
                prj = File.ReadAllText(InFilename);
            }
            return prj;
        }
    }
}
