using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util
{
    public static class TriangleUtil
    {
        public struct Triangle
        {
            public readonly Vector3 Origin;
            public readonly Vector3 Edge1, Edge2;

            public Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3? desiredNorm = null)
            {
                Origin = a;
                var tmp1 = b - a;
                var tmp2 = c - a;
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
            public bool Intersects(ref Ray ray, out float t)
            {
                t = float.NaN;
                const float epsilon = 0.0000001f;
                var h = ray.Direction.Cross(Edge2);
                float a = h.Dot(Edge1);
                if (a > -epsilon && a < epsilon)
                    return false;
                var f = 1 / a;
                var s = ray.Position - Origin;
                var u = f * (s.Dot(h));
                if (u < 0f || u > 1f)
                    return false;
                var q = s.Cross(Edge1);
                var v = f * ray.Direction.Dot(q);
                if (v < 0f || u + v > 1f)
                    return false;
                t = f * q.Dot(Edge2);
                return t > epsilon;
            }
        }
    }
}