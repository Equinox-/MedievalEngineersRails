using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util
{
    public static class Bezier
    {
        public interface IBezier
        {
            Vector3D Sample(float t);
            Vector3D SampleDerivative(float t);
        }

        public struct CurveBlit
        {
            public Vector3D P0, P1, P2, P3;

            public CurveBlit(Vector3D p0, Vector3D p1, Vector3D p2, Vector3D p3)
            {
                P0 = p0;
                P1 = p1;
                P2 = p2;
                P3 = p3;
            }

            public IBezier Convert()
            {
                if (P3.IsValid())
                    return new CubicBezier(P0, P1, P2, P3);
                if (P2.IsValid())
                    return new QuadraticBezier(P0, P1, P2);
                return new LinearCurve(P0, P1);
            }

            public static CurveBlit Convert(IBezier bez)
            {
                if (bez is LinearCurve)
                {
                    var lc = (LinearCurve) bez;
                    return new CurveBlit(lc.P0, lc.P1, Vector3D.Zero, Vector3D.Zero);
                }

                if (bez is QuadraticBezier)
                {
                    var qb = (QuadraticBezier) bez;
                    return new CurveBlit(qb.P0, qb.P1, qb.P2, Vector3D.Zero);
                }

                if (bez is CubicBezier)
                {
                    var cb = (CubicBezier) bez;
                    return new CurveBlit(cb.P0, cb.P1, cb.P2, cb.P3);
                }
                
                throw new Exception($"Failed to blit {bez}");
            }
        }

        public struct LinearCurve : IBezier
        {
            public readonly Vector3D P0, P1;

            public LinearCurve(Vector3D a, Vector3D b)
            {
                P0 = a;
                P1 = b;
            }

            public Vector3D Sample(float t)
            {
                return Vector3D.Lerp(P0, P1, t);
            }

            public Vector3D SampleDerivative(float t)
            {
                return (P1 - P0);
            }
        }

        public struct QuadraticBezier : IBezier
        {
            public readonly Vector3D P0, P1, P2;

            public QuadraticBezier(MatrixD a, Vector3D b)
            {
                var ext = Math.Max((a.Translation - b).Length() / 3, 1f);
                var d1 = a.Forward * ext;


                P0 = a.Translation;
                P1 = a.Translation + d1;
                P2 = b;
            }

            public QuadraticBezier(Vector3D a, MatrixD b)
            {
                var ext = Math.Max((a - b.Translation).Length() / 3, 1f);
                var d2 = b.Forward * ext;

                P0 = a;
                P1 = b.Translation - d2;
                P2 = b.Translation;
            }

            public QuadraticBezier(Vector3D p0, Vector3D p1, Vector3D p2)
            {
                P0 = p0;
                P1 = p1;
                P2 = p2;
            }

            public Vector3D Sample(float t)
            {
                var mt = 1 - t;
                return mt * mt * P0 + 2 * mt * t * P1 + t * t * P2;
            }

            public Vector3D SampleDerivative(float t)
            {
                var mt = 1 - t;
                return 2 * mt * (P1 - P0) + 2 * t * (P2 - P1);
            }
        }

        public struct CubicBezier : IBezier
        {
            public readonly Vector3D P0, P1, P2, P3;

            public CubicBezier(MatrixD a, MatrixD b)
            {
                var ext = Math.Max((a.Translation - b.Translation).Length() / 3, 1f);
                var d1 = a.Forward * ext;
                var d2 = b.Forward * ext;

                P1 = a.Translation + d1;
                P2 = b.Translation - d2;

                P0 = a.Translation;
                P3 = b.Translation;
            }

            public CubicBezier(Vector3D p0, Vector3D p1, Vector3D p2, Vector3D p3)
            {
                P0 = p0;
                P1 = p1;
                P2 = p2;
                P3 = p3;
            }

            public Vector3D Sample(float t)
            {
                var mt = 1 - t;
                var mt2 = mt * mt;
                var t2 = t * t;
                return mt * mt2 * P0 + 3 * mt2 * t * P1 + 3 * mt * t2 * P2 + t * t2 * P3;
            }

            public Vector3D SampleDerivative(float t)
            {
                var mt = 1 - t;
                return 3 * mt * mt * (P1 - P0) + 6 * mt * t * (P2 - P1) + 3 * t * t * (P3 - P2);
            }
        }

        private static readonly float _goldenRatio = (float) ((Math.Sqrt(5) + 1) / 2);

        // Golden-section search.  Init t0 and t1 before hand!
        public static void BSplineNearest<T>(T bez, Vector3D test, int iterations, ref float t0, ref float t1,
            float tolerance = 0.01f) where T : IBezier
        {
            var i = 0;
            do
            {
                var s = (t1 - t0) / _goldenRatio;
                var n0 = t0 + s;
                var n1 = t1 - s;
                if (Math.Abs(n1 - n0) < tolerance)
                    return;
                var v0 = Vector3D.DistanceSquared(bez.Sample(n0), test);
                var v1 = Vector3D.DistanceSquared(bez.Sample(n1), test);
                if (v1 < v0)
                    t1 = n0;
                else
                    t0 = n1;
            } while (++i < iterations);
        }

        public static MatrixD BSpline(MatrixD a, MatrixD b, float t)
        {
            var up = Vector3D.Lerp(a.Up, b.Up, t);
            var bez = new CubicBezier(a, b);

            var x = bez.Sample(t);
            var fwd = bez.SampleDerivative(t);
            return MatrixD.CreateWorld(x, Vector3D.Normalize(fwd), Vector3D.Normalize(up));
        }

        public static double Length<T>(T t, int steps = 20) where T : struct, IBezier
        {
            var c = t.Sample(0);
            double len = 0;
            for (var i = 1; i <= steps; i++)
            {
                var n = t.Sample(i / (float) steps);
                len += (c - n).Length();
                c = n;
            }

            return len;
        }
    }
}