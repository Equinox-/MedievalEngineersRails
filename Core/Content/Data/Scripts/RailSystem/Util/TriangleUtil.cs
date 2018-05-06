using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util
{
    public static class TriangleUtil
    {
        public struct Triangle
        {
            public readonly Vector3D Origin;
            public readonly Vector3 Edge1, Edge2;

            public Triangle(Vector3D a, Vector3D b, Vector3D c, Vector3? desiredNorm = null)
            {
                Origin = a;
                var tmp1 = (Vector3) (b - a);
                var tmp2 = (Vector3) (c - a);
                if (desiredNorm.HasValue)
                {
                    var norm = Vector3.Cross(tmp1, tmp2);
                    if (norm.Dot(desiredNorm.Value) < 0)
                    {
                        Edge2 = tmp1;
                        Edge1 = tmp2;
                        return;
                    }
                }

                Edge1 = tmp1;
                Edge2 = tmp2;
            }

            /// <summary>
            /// Moller-Trumbore Intersection
            /// </summary>
            /// <param name="ray"></param>
            /// <param name="t"></param>
            /// <returns></returns>
            public bool Intersects(ref RayD ray, out double t)
            {
                t = double.NaN;
                const float epsilon = 0.0000001f;
                Vector3 h = ((Vector3) ray.Direction).Cross(Edge2);
                float a = h.Dot(Edge1);
                if (a > -epsilon && a < epsilon)
                    return false;
                var f = 1 / a;
                Vector3D s = ray.Position - Origin;
                var u = f * (s.Dot(h));
                if (u < 0.0 || u > 1.0)
                    return false;
                Vector3D q = s.Cross(Edge1);
                var v = f * ray.Direction.Dot(q);
                if (v < 0.0 || u + v > 1.0)
                    return false;
                t = f * q.Dot(Edge2);
                return t > epsilon;
            }
        }
    }
}