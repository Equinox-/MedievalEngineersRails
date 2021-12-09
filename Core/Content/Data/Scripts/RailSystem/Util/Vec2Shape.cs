using System;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util
{
    public readonly struct Vec2Plane
    {
        public readonly Vector2 Normal;
        public readonly float Offset;

        public Vector2 Origin => Normal * -Offset;

        public Vec2Plane(Vector2 normal, float offset)
        {
            Normal = normal;
            Offset = offset;
        }

        public static Vec2Plane FromOriginNormal(Vector2 origin, Vector2 normal)
        {
            return new Vec2Plane(normal, -Vector2.Dot(origin, normal));
        }

        public float SignedDistance(Vector2 other)
        {
            return Vector2.Dot(Normal, other) + Offset;
        }
    }

    public readonly struct Vec2Circle
    {
        public readonly Vector2 Center;
        public readonly float RadiusSquared;
        public float Radius => (float)Math.Sqrt(RadiusSquared);

        public Vec2Circle(Vector2 center, float radiusSquared)
        {
            Center = center;
            RadiusSquared = radiusSquared;
        }

        public bool Contains(Vector2 pt) => Vector2.DistanceSquared(Center, pt) <= RadiusSquared;
    }
}