using System;
using VRage.Library.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Voxel.Shape
{
    public static class GradeShapeHelpers
    {
        public static bool IsInside(this IGradeShape shape, Vector3D position, float margin)
        {
            if (shape.IsInside(position))
                return true;
            if (margin < 0.01f)
                return false;
            const int lenSteps = 2;
            const int dirSteps = 16;
            for (var i = lenSteps; i >= 1; i--)
            for (var j = 0; j < dirSteps; j++)
                if (shape.IsInside(position + (margin * i / lenSteps) * GetRandomVector3Normalized()))
                    return true;
            return false;
        }
        private static float GetRandomFloat(float minValue, float maxValue)
        {
            return MyRandom.Instance.NextFloat() * (maxValue - minValue) + minValue;
        }

        private static float GetRandomRadian()
        {
            return GetRandomFloat(0.0f, 6.283186f);
        }
        private static Vector3 GetRandomVector3Normalized()
        {
            float randomRadian = GetRandomRadian();
            float randomFloat = GetRandomFloat(-1f, 1f);
            float num = (float) Math.Sqrt(1.0 - (double) randomFloat * (double) randomFloat);
            return new Vector3((double) num * Math.Cos((double) randomRadian), (double) num * Math.Sin((double) randomRadian), (double) randomFloat);
        }
    }
}