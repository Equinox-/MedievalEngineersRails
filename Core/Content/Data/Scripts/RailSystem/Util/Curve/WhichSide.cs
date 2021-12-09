using System;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util.Curve
{
    public enum WhichSideResult
    {
        Concave,
        OnCurve,
        Convex
    }

    // Based on https://www.sciencedirect.com/science/article/abs/pii/016783969190042A, Appendix
    public static class WhichSide
    {
        private const float Epsilon = 1e-6f;

        private readonly struct ReflectedCurve<T> : ICurve2 where T : ICurve2
        {
            private readonly T _curve;

            public ReflectedCurve(T curve)
            {
                _curve = curve;
            }

            public float MinT => _curve.MinT;
            public float MaxT => _curve.MaxT;

            public Vector2 Position(float t)
            {
                var pt = _curve.Position(t);
                pt.X = -pt.X;
                return pt;
            }

            public Vector2 Tangent(float t)
            {
                var pt = _curve.Tangent(t);
                pt.X = -pt.X;
                return pt;
            }
        }

        public static WhichSideResult ComputeCounterClockwise<T>(in T curve, Vector2 test) where T : ICurve2
        {
            var minT = curve.MinT;
            var maxT = curve.MaxT;
            return ComputeCounterClockwise(in curve, test, ref minT, ref maxT);
        }

        public static WhichSideResult ComputeCounterClockwise<T>(in T curve, Vector2 test, ref float minT, ref float maxT) where T : ICurve2
        {
            var reflected = new ReflectedCurve<T>(curve);
            var testReflected = test;
            testReflected.X = -testReflected.X;
            return ComputeClockwise(in reflected, testReflected, ref minT, ref maxT);
        }

        public static WhichSideResult ComputeClockwise<T>(in T curve, Vector2 test) where T : ICurve2
        {
            var minT = curve.MinT;
            var maxT = curve.MaxT;
            return ComputeClockwise(in curve, test, ref minT, ref maxT);
        }

        public static WhichSideResult ComputeClockwise<T>(in T curve, Vector2 test, ref float minT, ref float maxT) where T : ICurve2
        {
            var minPos = curve.Position(minT);
            var minTangent = curve.Tangent(minT);
            var maxPos = curve.Position(maxT);
            var maxTangent = curve.Tangent(maxT);
            var iterations = 0;
            while (iterations < 100 && maxT > minT + Epsilon)
            {
                iterations++;
                var minToTest = test - minPos;
                var maxToTest = test - maxPos;
                // Check if the test point is at an endpoint
                if (minToTest.LengthSquared() < Epsilon)
                {
                    maxT = minT;
                    return WhichSideResult.OnCurve;
                }

                if (maxToTest.LengthSquared() < Epsilon)
                {
                    minT = maxT;
                    return WhichSideResult.OnCurve;
                }

                if (minToTest.Cross(minTangent) > Epsilon) return WhichSideResult.Convex;

                if (maxToTest.Cross(maxTangent) > Epsilon) return WhichSideResult.Convex;

                var minToMax = maxPos - minPos;
                if (minToMax.Cross(minToTest) > Epsilon) return WhichSideResult.Concave;

                var midT = (minT + maxT) / 2;
                var midPos = curve.Position(midT);

                // Check if the test point is at the midpoint
                if (Vector2.DistanceSquared(midPos, test) < Epsilon)
                {
                    minT = maxT = midT;
                    return WhichSideResult.OnCurve;
                }

                var midTangent = curve.Tangent(midT);
                var midToTest = test - midPos;

                // Check if the test point is to the right of the middle tangent
                if (midToTest.Cross(midTangent) > Epsilon) return WhichSideResult.Convex;

                // Check if the resulting triangle (min, mid, max) is small enough to consider the test point on the curve
                if ((midPos - minPos).Cross(minToMax) < Epsilon) return WhichSideResult.OnCurve;

                // Check if the test point is to the left of the (min to mid) and (mid to max) lines
                if ((maxPos - midPos).Cross(midToTest) > Epsilon && midToTest.Cross(minPos - midPos) > Epsilon) return WhichSideResult.Concave;

                if (Vector2.Dot(midToTest, midTangent) > 0)
                {
                    minT = midT;
                    minPos = midPos;
                    minTangent = midTangent;
                    continue;
                }

                maxT = midT;
                maxPos = midPos;
                maxTangent = midTangent;
            }
            throw new Exception("Failed to determine which side the point is on");
        }
    }
}