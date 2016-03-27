using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

using NetTopologySuite.Features;
using GeoAPI.Geometries;
using GeoAPI.CoordinateSystems;
using GeoAPI.CoordinateSystems.Transformations;
using NetTopologySuite.Geometries;
using NetTopologySuite.CoordinateSystems;
using NetTopologySuite.CoordinateSystems.Transformations;
using NetTopologySuite.IO;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace MapTubeV
{
    /// <summary>
    /// Conversions between WGS84 and Google Mercator and back again. Most of this is from well-published formulas and algorithms. Also most of MapTube.
    /// </summary>
    public static class MercatorProjection
    {
        private static readonly double R_MAJOR = 6378137.0;
        private static readonly double R_MINOR = 6356752.3142;
        private static readonly double RATIO = R_MINOR / R_MAJOR;
        private static readonly double ECCENT = Math.Sqrt(1.0 - (RATIO * RATIO));
        private static readonly double COM = 0.5 * ECCENT;

        private static readonly double DEG2RAD = Math.PI / 180.0;
        private static readonly double RAD2Deg = 180.0 / Math.PI;
        private static readonly double PI_2 = Math.PI / 2.0;

        public static double[] toPixel(double lon, double lat)
        {
            return new double[] { lonToX(lon), latToY(lat) };
        }

        public static double[] toGeoCoord(double x, double y)
        {
            return new double[] { xToLon(x), yToLat(y) };
        }

        //rwm
        public static double[] toDegLonLat(double x, double y)
        {
            return new double[] { xDegToLonDeg(x), yDegToLatDeg(y) };
        }

        //RWM
        public static double xDegToLonDeg(double x)
        {
            return x; //trivial
        }

        //RWM
        public static double yDegToLatDeg(double y)
        {
            double y_rad = y * DEG2RAD;
            double phi = Math.Atan(Math.Sinh(y_rad));
            return phi * RAD2Deg;
        }

        public static double lonToX(double lon)
        {
            return R_MAJOR * DegToRad(lon);
        }

        public static double latToY(double lat)
        {
            lat = Math.Min(89.5, Math.Max(lat, -89.5));
            double phi = DegToRad(lat);
            double sinphi = Math.Sin(phi);
            double con = ECCENT * sinphi;
            con = Math.Pow(((1.0 - con) / (1.0 + con)), COM);
            double ts = Math.Tan(0.5 * ((Math.PI * 0.5) - phi)) / con;
            return 0 - R_MAJOR * Math.Log(ts);
        }

        public static double xToLon(double x)
        {
            return RadToDeg(x) / R_MAJOR;
        }

        public static double yToLat(double y)
        {
            double ts = Math.Exp(-y / R_MAJOR);
            double phi = PI_2 - 2 * Math.Atan(ts);
            double dphi = 1.0;
            int i = 0;
            while ((Math.Abs(dphi) > 0.000000001) && (i < 15))
            {
                double con = ECCENT * Math.Sin(phi);
                dphi = PI_2 - 2 * Math.Atan(ts * Math.Pow((1.0 - con) / (1.0 + con), COM)) - phi;
                phi += dphi;
                i++;
            }
            return RadToDeg(phi);
        }

        private static double RadToDeg(double rad)
        {
            return rad * RAD2Deg;
        }

        private static double DegToRad(double deg)
        {
            return deg * DEG2RAD;
        }

        /// <summary>
        /// Reproject a list of features in the given origin projection system into Google Mercator.
        /// </summary>
        /// <param name="features">The list of features that we're going to transform</param>
        /// <param name="OriginPRJ">The origin prj string that we're reprojecting the coordinates from</param>
        public static void Reproject(ref List<Feature> features, string OriginPRJ)
        {
            CoordinateSystemFactory cf = new CoordinateSystemFactory();
            CoordinateSystem originCS = (CoordinateSystem)cf.CreateFromWkt(OriginPRJ);
            //NOTE: not using EPSG:3857 (new) or EPSG:3587 (old) as they don't work. The method I'm using is to reproject into WGS84
            //and then convert from that to Google using the formulas in this class. This is the only way it will work, but I would
            //like to be able to do it in one go.
            CoordinateSystem destCS = (CoordinateSystem)GeographicCoordinateSystem.WGS84;

            CoordinateTransformationFactory ctFact = new CoordinateTransformationFactory();
            CoordinateTransformation transformation = (CoordinateTransformation)ctFact.CreateFromCoordinateSystems(originCS, destCS);
            MathTransform mt = (MathTransform)transformation.MathTransform;

            GeometryFactory gf = new GeometryFactory();

            foreach (Feature f in features)
            {
                Geometry geom = (Geometry)f.Geometry;
                Coordinate[] coords = geom.Coordinates;
                double[] origpts = new double[] { 0, 0 };
                int N = geom.Coordinates.Length;
                for (int i = 0; i < N; i++)
                {
                    //transform into WGS84
                    origpts[0] = coords[i].X;
                    origpts[1] = coords[i].Y;
                    double[] pts = mt.Transform(origpts);
                    //now transform into Google
                    double mercX = MercatorProjection.lonToX(pts[0]);
                    double mercY = MercatorProjection.latToY(pts[1]);
                    coords[i].X = mercX;
                    coords[i].Y = mercY;
                }
                //now put the coords back into the geometry in one go - otherwise you hit a big performance problem
                //Only setting geom.Coordinates[i] once means that this is now only taking minutes instead of hours.
                for (int i = 0; i < N; i++) geom.Coordinates[i] = coords[i]; //THIS IS THE BIG PERFORMANCE HIT! The only way around this would be to transform the geom recursively.

                //put the new geometry back and we're done with this feature
                geom.GeometryChanged();
                f.Geometry = geom;
            }
            //result is in the ref features parameter so we're not moving big chunks of memory around
        }
    }
}