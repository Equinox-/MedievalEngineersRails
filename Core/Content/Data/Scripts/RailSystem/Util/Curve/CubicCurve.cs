using System;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util.Curve
{
    public readonly struct CubicCurve : ICurve
    {
        public readonly Vector3D P0, P1, P2, P3;

        public CubicCurve(MatrixD a, MatrixD b)
        {
            var ext = Math.Max((a.Translation - b.Translation).Length() / 3, 1f);
            var d1 = a.Forward * ext;
            var d2 = b.Forward * ext;

            P1 = a.Translation + d1;
            P2 = b.Translation - d2;

            P0 = a.Translation;
            P3 = b.Translation;
        }

        public CubicCurve(Vector3D p0, Vector3D p1, Vector3D p2, Vector3D p3)
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
}