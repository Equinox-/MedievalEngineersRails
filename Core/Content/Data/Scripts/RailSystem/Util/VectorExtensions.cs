using System;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util
{
    public static class VectorExtensions
    {
        public static Vector3D Shifted(this Vector3D v)
        {
            return new Vector3D(v.Y, v.Z, v.X);
        }

        public static Vector3 Shifted(this Vector3 v)
        {
            return new Vector3(v.Y, v.Z, v.X);
        }

        public static Vector3D SafeNormalized(this Vector3D v)
        {
            var len = v.Length();
            if (len < 1e-3f)
                return Vector3D.Zero;
            return v / len;
        }

        public static Vector3 SafeNormalized(this Vector3 v)
        {
            var len = v.Length();
            if (len < 1e-3f)
                return Vector3.Zero;
            return v / len;
        }

        public static Vector3D NearestPoint(this LineD line, Vector3D test)
        {
            var dist = line.Direction.Dot(test - line.From);
            return line.From + line.Direction * MathHelper.Clamp(dist, 0, line.Length);
        }

        public static float Cross(this Vector2 lhs, Vector2 rhs) => lhs.X * rhs.Y - lhs.Y * rhs.X;

        public static Vector2 Rotate90(this Vector2 vec) => new Vector2(-vec.Y, vec.X);

        public static double Angle(this Vector2 vec) => Math.Atan2(vec.Y, vec.X);
        public static float AngleF(this Vector2 vec) => (float)vec.Angle();

        public static bool HasNaN(this Vector2 vec) => float.IsNaN(vec.X) || float.IsNaN(vec.Y);

        public static Vector2 RotatedX(float radians) => new Vector2((float)Math.Cos(radians), (float)Math.Sin(radians));

        public static Vector2 RotatedY(float radians) => new Vector2(-(float)Math.Sin(radians), (float)Math.Cos(radians));

        public static Vector2 MultiplyRotationMatrix(this Vector2 vec, float m11, float m21) =>
            new Vector2(m11 * vec.X - m21 * vec.Y, m21 * vec.X + m11 * vec.Y);


        public readonly struct CompactRotationMatrix2
        {
            public readonly float M11;
            public readonly float M21;

            public CompactRotationMatrix2(float m11, float m21)
            {
                M11 = m11;
                M21 = m21;
            }

            public static CompactRotationMatrix2 Rotation(float theta) => new CompactRotationMatrix2((float)Math.Cos(theta), (float)Math.Sin(theta));

            public override string ToString() => $"Rot[{M11} {M21}]";
        }

        public static Vector2 MultiplyRotationMatrix(this Vector2 vec, CompactRotationMatrix2 mat) => vec.MultiplyRotationMatrix(mat.M11, mat.M21);

        public static Vector2 SLerp(this Vector2 from, Vector2 to, float time)
        {
            var dot = Vector2.Dot(from, to);
            var theta = Math.Acos(dot);
            var sinTheta = Math.Sqrt(1 - dot * dot);
            return (float)(Math.Sin((1 - time) * theta) / sinTheta) * from + (float)(Math.Sin(time * theta) / sinTheta) * to;
        }
    }
}