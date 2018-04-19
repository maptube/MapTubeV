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

        #region reprojection

        /// <summary>
        /// Reproject a list of features in the given origin projection system into Google Mercator.
        /// </summary>
        /// <param name="features">The list of features that we're going to transform</param>
        /// <param name="OriginPRJ">The origin prj string that we're reprojecting the coordinates from</param>
        public static void ReprojectOldAndSlow(ref List<Feature> features, string OriginPRJ)
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

            int Count = 0;
            int Total = features.Count;
            foreach (Feature f in features)
            {
                Geometry geom = (Geometry)f.Geometry;
                int NumGeom = geom.NumGeometries;
                System.Diagnostics.Debug.WriteLine("Feature " + Count + "/" + Total + " NumGeoms=" + NumGeom);
                for (int N = 0; N < NumGeom; N++)
                {
                    Geometry geomN = (Geometry)geom.GetGeometryN(N);
                    Coordinate[] coords = geomN.Coordinates;
                    double[] origpts = new double[] { 0, 0 };
                    int NumPts = geomN.Coordinates.Length;
                    System.Diagnostics.Debug.WriteLine("GeomN=" + N + " NumPts=" + NumPts);
                    for (int i = 0; i < NumPts; i++)
                    {
                        //transform into WGS84
                        origpts[0] = coords[i].X;
                        origpts[1] = coords[i].Y;
                        double[] pts = mt.Transform(origpts);
                        if (pts[1] > 84) pts[1] = 84;
                        else if (pts[1] < -84) pts[1] = -84;
                        //now transform into Google
                        double mercX = MercatorProjection.lonToX(pts[0]);
                        double mercY = MercatorProjection.latToY(pts[1]);
                        coords[i].X = mercX;
                        coords[i].Y = mercY;
                    }
                    //now put the coords back into the geometry in one go - otherwise you hit a big performance problem
                    //Only setting geom.Coordinates[i] once means that this is now only taking minutes instead of hours.
                    for (int i = 0; i < NumPts; i++) geomN.Coordinates[i] = coords[i]; //THIS IS THE BIG PERFORMANCE HIT! The only way around this would be to transform the geom recursively.

                    //put the new geometry back and we're done with this feature
                    geomN.GeometryChanged();
                }
                geom.GeometryChanged();
                f.Geometry = geom;
                ++Count;
            }
            //result is in the ref features parameter so we're not moving big chunks of memory around
        }

        /// <summary>
        /// This is a higher performance reproject using the OGC geometry type correctly, not just manipulating points.
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

            int Count = 0;
            int Total = features.Count;
            foreach (Feature f in features)
            {
                Geometry geom = (Geometry)f.Geometry;
                System.Diagnostics.Debug.WriteLine("Feature " + Count + "/" + Total);
                Geometry projGeom = ReprojectGeometry(geom,mt,gf);
                f.Geometry = projGeom; //RWM was geom!
                ++Count;
            }
        }

        public static Geometry ReprojectGeometry(Geometry geom, MathTransform mt,GeometryFactory gf)
        {
            Geometry projGeom = null;

            string geomType = geom.GeometryType;
            if (geomType=="Point")
            {
                projGeom = ReprojectPoint((Point)geom, mt, gf);
            }
            else if (geomType=="LineString")
            {
                projGeom = ReprojectLineString((LineString)geom, mt, gf);
            }
            else if (geomType=="Polygon")
            {
                projGeom = ReprojectPolygon((Polygon)geom, mt, gf);
            }
            else if (geomType=="MultiPoint")
            {
                IPoint[] pts = new IPoint[geom.NumGeometries];
                for (int N = 0; N < geom.NumGeometries; N++)
                {
                    Geometry geomN = (Geometry)geom.GetGeometryN(N);
                    Point projGeomN = ReprojectPoint((Point)geomN, mt, gf);
                    pts[N] = projGeomN;
                }
                projGeom = new MultiPoint(pts, gf);
            }
            else if (geomType=="MultiLineString")
            {
                ILineString[] lines = new ILineString[geom.NumGeometries];
                for (int N = 0; N < geom.NumGeometries; N++)
                {
                    Geometry geomN = (Geometry)geom.GetGeometryN(N);
                    LineString projGeomN = ReprojectLineString((LineString)geomN, mt, gf);
                    lines[N] = projGeomN;
                }
                projGeom = new MultiLineString(lines, gf);
            }
            else if (geomType=="MultiPolygon")
            {
                IPolygon[] polys = new IPolygon[geom.NumGeometries];
                for (int N = 0; N < geom.NumGeometries; N++)
                {
                    Geometry geomN = (Geometry)geom.GetGeometryN(N);
                    Polygon projGeomN = ReprojectPolygon((Polygon)geomN, mt, gf);
                    polys[N] = projGeomN;
                }
                projGeom = new MultiPolygon(polys, gf);
            }
            //GeometryCollection?
            return projGeom;
        }

        public static Point ReprojectPoint(Point P,MathTransform mt, GeometryFactory gf)
        {
            Coordinate[] coord = P.Coordinates;
            Coordinate[] projCoord = ReprojectCoordinates(coord, mt);
            return new Point(projCoord[0]);
        }

        public static LineString ReprojectLineString(LineString LS, MathTransform mt, GeometryFactory gf)
        {
            Coordinate[] coords = LS.Coordinates;
            Coordinate[] projCoords = ReprojectCoordinates(coords, mt);
            return new LineString(projCoords);
        }

        public static Polygon ReprojectPolygon(Polygon Poly, MathTransform mt, GeometryFactory gf)
        {
            //first the outer ring
            LinearRing outerRing = (LinearRing)Poly.ExteriorRing;
            Coordinate[] coords = outerRing.Coordinates;
            Coordinate[] projCoords = ReprojectCoordinates(coords, mt);
            LinearRing projOuterRing = new LinearRing(projCoords);
            //now the holes
            LinearRing[] projInnerRings = new LinearRing[Poly.NumInteriorRings];
            for (int i=0; i<Poly.NumInteriorRings; i++)
            {
                coords = Poly.InteriorRings[i].Coordinates;
                projCoords = ReprojectCoordinates(coords, mt);
                projInnerRings[i] = new LinearRing(projCoords);
            }
            return new Polygon(projOuterRing, projInnerRings);
        }

        public static Coordinate[] ReprojectCoordinates(Coordinate[] coords, MathTransform mt)
        {
            int NumPts = coords.Length;
            Coordinate[] projCoords = new Coordinate[NumPts];
            double[] origpts = new double[] { 0, 0 };
            for (int i = 0; i < NumPts; i++)
            {
                //transform into WGS84
                origpts[0] = coords[i].X;
                origpts[1] = coords[i].Y;
                double[] pts = mt.Transform(origpts);
                if (pts[1] > 84) pts[1] = 84;
                else if (pts[1] < -84) pts[1] = -84;
                //now transform into Google
                double mercX = MercatorProjection.lonToX(pts[0]);
                double mercY = MercatorProjection.latToY(pts[1]);
                //projCoords[i].X = mercX;
                //projCoords[i].Y = mercY;
                projCoords[i] = new Coordinate(mercX, mercY);
            }
            return projCoords;
        }

        #endregion reprojection

    }
}