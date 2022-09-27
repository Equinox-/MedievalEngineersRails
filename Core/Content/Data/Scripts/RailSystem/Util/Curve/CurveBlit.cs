using System;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util.Curve
{
    public struct CurveBlit
    {
        public Vector3D P0;
        public Vector3 P1, P2, P3;

        public CurveBlit(Vector3D p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            P0 = p0;
            P1 = p1;
            P2 = p2;
            P3 = p3;
        }

        public ICurve Convert()
        {
            if (P3.LengthSquared() > 0)
                return new CubicCurve(P0, P0 + P1, P0 + P2, P0 + P3);
            if (P2.LengthSquared() > 0)
                return new QuadraticCurve(P0, P0 + P1, P0 + P2);
            return new LinearCurve(P0, P0 + P1);
        }

        public static CurveBlit Convert(ICurve bez)
        {
            if (bez is LinearCurve)
            {
                var lc = (LinearCurve) bez;
                return new CurveBlit(lc.P0, (Vector3)(lc.P1 - lc.P0), Vector3.Zero, Vector3.Zero);
            }

            if (bez is QuadraticCurve)
            {
                var qb = (QuadraticCurve) bez;
                return new CurveBlit(qb.P0, (Vector3)(qb.P1 - qb.P0), (Vector3)(qb.P2 - qb.P0), Vector3.Zero);
            }

            if (bez is CubicCurve)
            {
                var cb = (CubicCurve) bez;
                return new CurveBlit(cb.P0, (Vector3)(cb.P1 - cb.P0), (Vector3)(cb.P2 - cb.P0), (Vector3)(cb.P3 - cb.P0));
            }

            throw new Exception($"Failed to blit {bez}");
        }
    }
}