using System;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util.Curve
{
    public static class CurveExtensions
    {
        private static readonly float _goldenRatio = (float) ((Math.Sqrt(5) + 1) / 2);

        // Golden-section search.  Init t0 and t1 before hand!
        public static void NearestPoint<T>(T bez, Vector3D test, int iterations, ref float t0, ref float t1,
            float tolerance = 0.01f) where T : ICurve
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
            var bez = new CubicCurve(a, b);

            var x = bez.Sample(t);
            var fwd = bez.SampleDerivative(t);
            return MatrixD.CreateWorld(x, Vector3D.Normalize(fwd), Vector3D.Normalize(up));
        }

        public static void AlignFwd(ref MatrixD m1, ref MatrixD m2)
        {
            var desiredFwd = m2.Translation - m1.Translation;
            if (desiredFwd.Dot(m1.Forward) < 0)
            {
                m1.Forward *= -1f;
                m1.Right *= -1f;
            }

            // ReSharper disable once InvertIf
            if (desiredFwd.Dot(m2.Forward) < 0)
            {
                m2.Forward *= -1f;
                m2.Right *= -1f;
            }
        }

        public static double Length<T>(this T t, int steps = 20) where T : struct, ICurve
        {
            var c = t.Sample(0);
            double len = 0;
            for (var i = 1; i <= steps; i++)
            {
                var n = t.Sample(i / (float) steps);
                len += Vector3D.Distance(c, n);
                c = n;
            }

            return len;
        }

        public static Vector3D ExpandToCubic(Vector3D p0, Vector3D p0Ctl, Vector3D p1, float controlLimit)
        {
            // dist(p1Ctl, p1) == dist(p0Ctl, p0)
            // angle(p1Ctl, p1, p0) == angle(p0Ctl, p0, p1)
            
            var cBias = p1 - p0;
            var lenC = cBias.Normalize();
            var aBias = p0Ctl - p0;
            if (cBias.Dot(aBias) < 0)
                aBias = -aBias;
            var lenCtl = aBias.Normalize();
            
            var controlAngle = Math.Acos(cBias.Dot(aBias));
            // Isosceles tri.  Define Q as the intersection of the bias vectors.
            // lenC^2 = 2*lenQP0^2*(1-cos(theta))
            var theta = Math.PI - controlAngle * 2;
            var lenQp0 = Math.Sqrt(lenC * lenC / (2 * (1 - Math.Cos(theta))));

            var qPoint = p0 + aBias * Math.Min(lenQp0, controlLimit);
            return p1 + (qPoint - p1) * (lenCtl / lenQp0);
        }
    }
}