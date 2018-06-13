using System;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util.Curve
{
    public struct QuadraticCurve : ICurve
    {
        public readonly Vector3D P0, P1, P2;

        public QuadraticCurve(MatrixD a, Vector3D b)
        {
            var ext = Math.Max((a.Translation - b).Length() / 3, 1f);
            var d1 = a.Forward * ext;


            P0 = a.Translation;
            P1 = a.Translation + d1;
            P2 = b;
        }

        public QuadraticCurve(Vector3D a, MatrixD b)
        {
            var ext = Math.Max((a - b.Translation).Length() / 3, 1f);
            var d2 = b.Forward * ext;

            P0 = a;
            P1 = b.Translation - d2;
            P2 = b.Translation;
        }

        public QuadraticCurve(Vector3D p0, Vector3D p1, Vector3D p2)
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
}