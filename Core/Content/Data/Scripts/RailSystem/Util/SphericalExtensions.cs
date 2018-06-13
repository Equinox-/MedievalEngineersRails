using System;
using Sandbox.Game.Entities;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util
{
    public static class SphericalExtensions
    {
        public static double GetElevation(this Vector3D v)
        {
            var planet = MyGamePruningStructure.GetClosestPlanet(v);
            return planet != null ? Vector3D.Distance(v, planet.PositionComp.WorldVolume.Center) : v.Length();
        }

        // r, theta (inc), phi (az)
        public static Vector3D ToSpherical(Vector3D world)
        {
            var r = world.Length();
            if (r <= 1e-3f)
                return new Vector3D(0, 0, r);
            var theta = Math.Acos(world.Z / r);
            var phi = Math.Atan2(world.Y, world.X);
            return new Vector3D(r, theta, phi);
        }

        public static Vector3D FromSpherical(Vector3D spherical)
        {
            var sinTheta = spherical.X * Math.Sin(spherical.Y);
            return new Vector3D(sinTheta * Math.Cos(spherical.Z), sinTheta * Math.Sin(spherical.Z),
                spherical.X * Math.Cos(spherical.Y));
        }

        public static Vector3D DifferentialFromSpherical(Vector3D sphericalCoords, Vector3D sphericalDiff)
        {
            // d/dt[x*sin(y)] = 
            //X = x*sin(y)*cos(z) = cos(z)*(cos(y)*x*y' + sin(y)*x') - sin(z)*sin(y)*x*z'
            //Y = x*sin(y)*sin(z) = sin(z)*(cos(y)*x*y' + sin(y)*x') + cos(z)*sin(y)*x*z'
            //Z = x*cos(y) = cos(y)*x' - sin(y)*x*y'

            var cosY = Math.Cos(sphericalCoords.Y);
            var sinY = Math.Sin(sphericalCoords.Y);
            var cosZ = Math.Cos(sphericalCoords.Z);
            var sinZ = Math.Sin(sphericalCoords.Z);

            var frag1 = cosY * sphericalCoords.X * sphericalDiff.Y + sinY * sphericalDiff.X;
            var frag2 = sinY * sphericalCoords.X * sphericalDiff.Z;

            var cX = frag1 * cosZ - frag2 * sinZ;
            var cY = frag1 * sinZ + frag2 * cosZ;
            var cZ = cosY * sphericalDiff.X - sinY * sphericalCoords.X * sphericalDiff.Y;

            return new Vector3D(cX, cY, cZ);
        }
    }
}