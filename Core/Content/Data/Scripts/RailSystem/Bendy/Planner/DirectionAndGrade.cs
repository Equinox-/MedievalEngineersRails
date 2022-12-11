using System;
using Sandbox.Game.Entities;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy.Planner
{
    public static class DirectionAndGrade
    {
        public static Matrix ComputeSurfaceMatrix(Vector3D location)
        {
            var worldMatrix = MyGamePruningStructureSandbox.GetClosestPlanet(location).PositionComp.WorldMatrix;
            var northPole = Vector3D.Transform(new Vector3D(0, 1e9, 0), ref worldMatrix);
            var northDirection = northPole - location;
            northDirection.Normalize();
            var worldUp = (Vector3)Vector3D.Normalize(location - worldMatrix.Translation);
            var westDirection = Vector3.Cross(worldUp, (Vector3)northDirection);
            westDirection.Normalize();
            return Matrix.CreateWorld(Vector3.Zero, westDirection, worldUp);
        }

        public static void DecomposeTangent(Matrix surfaceMatrix, Vector3 tangent, out float direction, out float grade)
        {
            surfaceMatrix.TransposeRotationInPlace();
            var localTangent = Vector3.TransformNormal(tangent, ref surfaceMatrix);
            direction = ((float)Math.Atan2(localTangent.Z, localTangent.X) + MathHelper.TwoPi) % MathHelper.TwoPi;
            grade = localTangent.Y / (float)Math.Sqrt(localTangent.X * localTangent.X + localTangent.Z * localTangent.Z);
        }

        public static Vector3 ComposeTangent(in Matrix surfaceMatrix, float direction, float grade)
        {
            var localTangent = new Vector3((float)Math.Cos(direction), 0, (float)Math.Sin(direction));
            var localTangentLength = localTangent.Length();
            localTangent.Y = localTangentLength * grade;
            localTangent.Normalize();
            return Vector3.TransformNormal(localTangent, surfaceMatrix);
        }
    }
}