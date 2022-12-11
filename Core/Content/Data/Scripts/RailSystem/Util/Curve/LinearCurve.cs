using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util.Curve
{
    public readonly struct LinearCurve : ICurve
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

        public Vector3D SampleSecondDerivative(float t) => Vector3D.Zero;
    }
    public readonly struct LinearCurve2 : ICurve2
    {
        public readonly Vector2 P0;
        public readonly Vector2 ConstantTangent;
        public readonly float Length;
        public Vector2 P1 => Position(Length);

        public LinearCurve2(Vector2 a, Vector2 b)
        {
            P0 = a;
            var dir = b - a;
            Length = dir.Length();
            ConstantTangent = dir / Length;
        }

        public float MinT => 0;
        public float MaxT => Length;

        public Vector2 Position(float t) => P0 + ConstantTangent * t;

        public Vector2 Tangent(float t) => ConstantTangent;
    }
}