using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util.Curve
{
    public struct LinearCurve : ICurve
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
}