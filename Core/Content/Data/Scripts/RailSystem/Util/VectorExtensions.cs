﻿using VRageMath;

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
    }
}