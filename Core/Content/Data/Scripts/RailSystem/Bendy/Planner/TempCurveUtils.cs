using System;
using Equinox76561198048419394.RailSystem.Util.Curve;
using Sandbox.Game.Entities;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy.Planner
{
    public readonly struct IntermediateCurve
    {
        private const int BoundingBoxSteps = 5;

        public readonly ICurve Curve;

        public readonly MinMaxAverage Grade;

        public readonly MinMaxAverage Curvature;
        public readonly BoundingBoxD Bounds;

        public float SignedGradeMax => -Grade.Min < Grade.Max ? Grade.Max : Grade.Min;
        public float UnsignedGradeMax => Math.Abs(SignedGradeMax);

        public readonly float Length;

        public EdgePlacerSystem.JointParameters JointParameters => new EdgePlacerSystem.JointParameters
        {
            Grade = Grade.Average,
            Length = Length,
            // curvature = 1 / radius of curve
            // length / radius of curve = bend radians
            BendRadians = Length * Curvature.Average,
        };

        public IntermediateCurve(ICurve curve)
        {
            var planet = MyGamePruningStructureSandbox.GetClosestPlanet(curve.Sample(.5f));
            const int steps = 10;
            const float timeStep = 1f / steps;
            Curve = curve;
            Curvature = MinMaxAverage.Uninitialized;
            Grade = MinMaxAverage.Uninitialized;
            Length = 0;
            var firstHeight = 0.0;
            var prevPos = Vector3D.Zero;
            var prevHeight = 0.0;
            var prevDerivative = Vector3.Zero;

            for (var i = 0; i <= steps; i++)
            {
                var t = i * timeStep;
                var pos = curve.Sample(t);
                var derivative = (Vector3)curve.SampleDerivative(t);
                var height = planet != null ? Vector3D.Distance(pos, planet.GetPosition()) : 0;
                if (i > 0)
                {
                    var len = (float)Vector3D.Distance(pos, prevPos);
                    var midTime = (i - 0.5f) * timeStep;
                    var firstDerivative = (derivative + prevDerivative) / 2;
                    var secondDerivative = (derivative - prevDerivative) / timeStep;
                    var curvature = CurveExtensions.Curvature(firstDerivative, secondDerivative);
                    var deltaHeight = height - prevHeight;
                    // grade = tan(angle).
                    // deltaHeight / length = sin(angle)
                    // cos(angle) = sqrt(1 - sin^2(angle))
                    var sinOfAngle = deltaHeight / len;
                    var cosOfAngle = Math.Sqrt(1 - sinOfAngle * sinOfAngle);
                    var grade = (float) (sinOfAngle / cosOfAngle);
                    Curvature.Submit(midTime, curvature);
                    Curvature.Average += curvature * len;
                    Grade.Submit(midTime, grade);
                    Length += len;
                }

                if (i == 0)
                    firstHeight = height;
                prevPos = pos;
                prevHeight = height;
                prevDerivative = derivative;
            }

            Curvature.Average /= Length;
            Grade.Average = (float)(prevHeight - firstHeight) / Length;

            var bounds = BoundingBoxD.CreateInvalid();
            for (var i = 0; i <= BoundingBoxSteps; i++)
                bounds.Include(curve.Sample(i / (float)BoundingBoxSteps));
            Bounds = bounds;
        }
    }

    public struct MinMaxAverage
    {
        public float Min, Max, Average;
        public float MinAtTime, MaxAtTime;

        public static readonly MinMaxAverage Uninitialized = new MinMaxAverage
        {
            Min = float.PositiveInfinity,
            Max = float.NegativeInfinity,
            Average = 0
        };

        public void Submit(float time, float sample)
        {
            if (sample < Min)
            {
                Min = sample;
                MinAtTime = time;
            }

            if (sample > Max)
            {
                Max = sample;
                MaxAtTime = time;
            }
        }
    }
}