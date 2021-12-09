using System;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util.Curve
{
    // Based on https://www.ams.org/journals/mcom/1992-59-199/S0025-5718-1992-1134736-8/S0025-5718-1992-1134736-8.pdf
    public static class SpiralSolver
    {
        private const float Epsilon = 1e-5f;

        public interface ICurveCollector
        {
            void AddStraightLine(Vector2 a, Vector2 b);
            void AddCircularArc(Vector2 center, Vector2 from, Vector2 to);
            void AddSpiralArc(in Vec2Transform transform, float tStart, float tEnd);

            void AddDebug(string msg);

            void AddDebugCurve<T>(in T curve) where T : ICurve2;
        }

        public static bool Solve<T>(in ControlPoint from, in ControlPoint to, ref T collector) where T : ICurveCollector
        {
            if (Math.Abs(from.Curvature - to.Curvature) < Epsilon)
                return false;
            ControlPoint lowCurvature, highCurvature;
            if (Math.Abs(from.Curvature) > Math.Abs(to.Curvature))
            {
                lowCurvature = new ControlPoint(to.Position, -to.Tangent, -to.Curvature);
                highCurvature = new ControlPoint(from.Position, -from.Tangent, -from.Curvature);
            }
            else
            {
                lowCurvature = from;
                highCurvature = to;
            }

            var crs = CreateStandardForm(lowCurvature, highCurvature.Curvature, out var flipY);
            var localQ = new ControlPoint(
                crs.WorldToLocalPosition(highCurvature.Position),
                crs.WorldToLocalNormal(highCurvature.Tangent),
                flipY * highCurvature.Curvature);
            var transformedCollector = new TransformedCollector<T>(collector, crs);
            var result = SolveStandardForm(flipY * lowCurvature.Curvature, in localQ, ref transformedCollector);
            collector = transformedCollector.Backing;
            return result;
        }

        public static Suggestion? Suggest(in ControlPoint from, float toCurvature, Vector2 toTangent)
        {
            if (from.Curvature < 0 && toCurvature > 0) return null;
            if (from.Curvature > 0 && toCurvature < 0) return null;

            ControlPoint lowCurvature;
            float highCurvature;
            Vector2 highTangent;
            var flipped = Math.Abs(from.Curvature) > Math.Abs(toCurvature);

            if (flipped)
            {
                lowCurvature = new ControlPoint(from.Position, -toTangent, -toCurvature);
                highCurvature = -from.Curvature;
                highTangent = -from.Tangent;
            }
            else
            {
                lowCurvature = from;
                highCurvature = toCurvature;
                highTangent = toTangent;
            }

            var crs = CreateStandardForm(in lowCurvature, highCurvature, out var flipY);
            var std = SuggestStandardForm(lowCurvature.Curvature * flipY,
                highCurvature * flipY, crs.WorldToLocalNormal(highTangent));

            if (flipped)
                crs = new Vec2Transform(crs.Translation, -crs.AxisX, -crs.AxisY);

            return new Suggestion(in std, in crs);
        }

        private static Vec2Transform CreateStandardForm(in ControlPoint origin, float otherCurvature, out int flipY)
        {
            flipY = Math.Sign(origin.Curvature);
            if (flipY == 0) flipY = Math.Sign(otherCurvature);
            if (flipY == 0) flipY = 1;
            return new Vec2Transform(origin.Position, origin.Tangent, flipY);
        }

        public readonly struct Suggestion
        {
            public readonly float? CircleRadiusSq;
            public readonly Vector2 R;
            public readonly Vector2 RTangent;
            public readonly Vector2 CircleCenterOrClipTangent;
            public readonly float? TDistance;

            public Suggestion(in Suggestion other, in Vec2Transform transform)
            {
                CircleRadiusSq = other.CircleRadiusSq;
                R = transform.LocalToWorldPosition(other.R);
                RTangent = transform.LocalToWorldNormal(other.RTangent);
                CircleCenterOrClipTangent = other.CircleRadiusSq.HasValue
                    ? transform.LocalToWorldPosition(other.CircleCenterOrClipTangent)
                    : transform.LocalToWorldNormal(other.CircleCenterOrClipTangent);
                TDistance = other.TDistance;
            }

            public Suggestion(bool usesCircle, Vector2 r, Vector2 rTangent, Vector2 circleCenterOrClipTangent)
            {
                R = r;
                RTangent = rTangent;
                CircleCenterOrClipTangent = circleCenterOrClipTangent;
                if (usesCircle)
                {
                    var rToCenter = circleCenterOrClipTangent - r;
                    CircleRadiusSq = rToCenter.LengthSquared();
                    // 2 * cos(angleBetween(rToCenter, rTangent)) * radius
                    TDistance = 2 * Vector2.Dot(rToCenter, rTangent);
                }
                else
                {
                    CircleRadiusSq = null;
                    TDistance = null;
                }
            }

            public Vector2 Snap(Vector2 other, float margin = Epsilon)
            {
                // Snap to circle or Y=R.X plane
                if (CircleRadiusSq.HasValue)
                {
                    var relative = other - CircleCenterOrClipTangent;
                    var distSq = relative.LengthSquared();
                    if (distSq > CircleRadiusSq.Value - margin)
                        other = CircleCenterOrClipTangent + relative * (float)((Math.Sqrt(CircleRadiusSq.Value) - margin) / Math.Sqrt(distSq));
                }
                else
                {
                    var relative = other - R;
                    var normalDist = Math.Max(margin, -relative.Cross(CircleCenterOrClipTangent));
                    var collinearDist = Math.Max(margin, Vector2.Dot(relative, CircleCenterOrClipTangent));
                    other = R + collinearDist * CircleCenterOrClipTangent + normalDist * CircleCenterOrClipTangent.Rotate90();
                }

                // Snap to R plane
                {
                    var relative = other - R;
                    var normalDist = relative.Cross(RTangent);
                    var collinearDist = Math.Max(margin, Vector2.Dot(relative, RTangent));
                    if (TDistance.HasValue)
                        collinearDist = Math.Min(collinearDist, TDistance.Value - margin);
                    return R + RTangent * collinearDist - Math.Max(margin, normalDist) * RTangent.Rotate90();
                }
            }
        }

        public readonly struct StandardFormParams
        {
            public readonly bool ZeroCurvatureOrigin;
            public readonly float Kp;
            public readonly float Kq;
            public readonly float WPiOver2;
            public readonly float W;
            public readonly Vector2 RDirection;
            public readonly Vector2 R;

            public StandardFormParams(float curvatureOrigin, float curvatureOther, Vector2 toTangent)
            {
                Kp = curvatureOrigin;
                Kq = curvatureOther;
                ZeroCurvatureOrigin = curvatureOrigin < Epsilon;
                WPiOver2 = toTangent.AngleF();
                if (WPiOver2 < 0)
                    WPiOver2 += MathHelper.TwoPi;
                WPiOver2 = Math.Min(MathHelper.Pi, WPiOver2);
                W = Math.Min(2, WPiOver2 / MathHelper.PiOver2);
                R = new Vector2((float)Math.Sin(WPiOver2), 1 - (float)Math.Cos(WPiOver2)) / Kq;
                RDirection = Vector2.Normalize(R);
            }

            public void CurveParameters(out Vector2 sPoint, out DCurve dCurve, out ECurve eCurve)
            {
                var bParams = new BCurveParams(in this);
                dCurve = new DCurve(in this, in bParams);
                sPoint = dCurve.Position(0);
                eCurve = new ECurve(in this, in bParams, sPoint);
            }

            public float ClipCircleRadius => 1 / Kp - 1 / Kq;
            public Vector2 ClipCircleCenter => new Vector2(R.X, ClipCircleRadius + R.Y);
        }

        public readonly struct BCurveParams
        {
            public readonly float W;
            private readonly float _bMultiplier;

            public BCurveParams(in StandardFormParams sfp)
            {
                W = sfp.W;
                _bMultiplier = 1 / (sfp.Kq * sfp.Kq - sfp.Kp * sfp.Kp);
            }

            public void Get(float time, out float bSquared, out float b)
            {
                bSquared = Math.Max(0, W - time) * _bMultiplier;
                b = (float)Math.Sqrt(bSquared);
            }

            public void GetDerivative(float time, out float bSquared, out float b, out float bSquaredDerivative, out float bDerivative)
            {
                Get(time, out bSquared, out b);
                bSquaredDerivative = -_bMultiplier;
                bDerivative = 0.5f / b * bSquaredDerivative;
            }
        }

        /// <summary>
        /// pi*b*(fresnel(kQ*b) - fresnel(kP*b)).Rotate(rotInner).
        /// </summary>
        private static Vector2 CurvePoint(float b, float kQ, float kP, float rotationInner)
        {
            var curvePt = FresnelIntegral.PositionAtTime(kQ * b) - FresnelIntegral.PositionAtTime(kP * b);
            curvePt.Rotate(rotationInner);
            curvePt *= MathHelper.Pi * b;
            return curvePt;
        }

        /// <summary>
        /// Computes the derivative of the CurvePoint function.
        /// </summary>
        private static Vector2 CurvePointDerivative(
            float b, float bDerivative,
            float bSquared, float bSquaredDerivative,
            float kQ, float kP,
            float rotationInner, float rotationInnerDerivative)
        {
            var curvePt = FresnelIntegral.PositionAtTime(kQ * b) - FresnelIntegral.PositionAtTime(kP * b);
            var curvePtDerivative = (FresnelIntegral.DerivativeAtTime(kQ * b) * kQ - FresnelIntegral.DerivativeAtTime(kP * b) * kP) * bDerivative;

            var cosRotInner = (float)Math.Cos(rotationInner);
            var sinRotInner = (float)Math.Sin(rotationInner);
            var m11 = b * cosRotInner;
            var m11Derivative = bDerivative * cosRotInner - b * sinRotInner * rotationInnerDerivative;
            var m21 = b * sinRotInner;
            var m21Derivative = bDerivative * sinRotInner + b * cosRotInner * rotationInnerDerivative;

            var curveDerivative = curvePt.MultiplyRotationMatrix(m11Derivative, m21Derivative)
                                  + curvePtDerivative.MultiplyRotationMatrix(m11, m21);
            return curveDerivative * MathHelper.Pi;
        }

        public readonly struct DCurve : ICurve2
        {
            private readonly float _kQ, _kP;
            private readonly float _rotScale;
            private readonly BCurveParams _bParams;
            private readonly Vector2 _end;
            private readonly Vector2 _endTangent;
            private readonly bool _zeroCurvatureOrigin;

            public float MinT => 0;
            public float MaxT => _bParams.W;

            public DCurve(in StandardFormParams sfp, in BCurveParams bCurveParams)
            {
                _kQ = sfp.Kq;
                _kP = sfp.Kp;
                _bParams = bCurveParams;
                _zeroCurvatureOrigin = sfp.ZeroCurvatureOrigin;
                if (sfp.ZeroCurvatureOrigin)
                {
                    _end = sfp.R;
                    _rotScale = MathHelper.Pi / _kQ;
                    _endTangent = new Vector2(-1, 0);
                }
                else
                {
                    _end = new Vector2((float)-Math.Sin(sfp.WPiOver2), (float)Math.Cos(sfp.WPiOver2));
                    _rotScale = -MathHelper.PiOver2 * _kP * _kP;
                    var dir = sfp.R - sfp.ClipCircleCenter;
                    _endTangent = -dir.Rotate90();
                }
            }

            private float RotationInner(float bSquared) => _rotScale * bSquared;

            public Vector2 Position(float t)
            {
                if (_zeroCurvatureOrigin)
                    // Clothoid Spline Transition Spirals, Equation 6b 
                    return _end + _rotScale * FresnelIntegral.IntegralAtTime((float)Math.Sqrt(Math.Max(0, _bParams.W - t)));
                // Clothoid Spline Transition Spirals, Equation 6a
                _bParams.Get(t, out var bSquared, out var b);
                var curvePt = CurvePoint(b, _kQ, _kP, RotationInner(bSquared));

                var secondRot = MathHelper.PiOver2 * (_bParams.W - t);
                var offsetPt = _end + new Vector2((float)Math.Sin(secondRot), -(float)Math.Cos(secondRot));
                return curvePt - offsetPt / _kQ;
            }

            public Vector2 Tangent(float t)
            {
                if (_zeroCurvatureOrigin)
                {
                    var fresnelTime = (float)Math.Sqrt(Math.Max(0, _bParams.W - t));
                    return _rotScale * FresnelIntegral.PositionAtTime(fresnelTime) * -0.5f / fresnelTime;
                }

                // Not differentiable at t = MaxT, so pull the tangent from elsewhere
                if (t >= MaxT - Epsilon) return _endTangent;

                _bParams.GetDerivative(t, out var bSquared, out var b, out var bSquaredDerivative, out var bDerivative);

                var rotationInner = RotationInner(bSquared);
                var rotationInnerDerivative = _rotScale * bSquaredDerivative;

                var curveDerivative = CurvePointDerivative(
                    b, bDerivative,
                    bSquared, bSquaredDerivative,
                    _kQ, _kP, rotationInner, rotationInnerDerivative);


                var secondRot = MathHelper.PiOver2 * (_bParams.W - t);
                const float secondRotDerivative = -MathHelper.PiOver2;
                var offsetPtDerivative = new Vector2((float)Math.Cos(secondRot), (float)Math.Sin(secondRot)) * secondRotDerivative;
                return curveDerivative - offsetPtDerivative / _kQ;
            }

            public WhichSideResult DetermineWhichSide(Vector2 test) => WhichSide.ComputeCounterClockwise(in this, test);
        }

        public readonly struct ECurve : ICurve2
        {
            private readonly float _kQ, _kP;
            private readonly BCurveParams _bParams;
            private readonly float _kPSquared;
            private readonly bool _zeroCurvatureOrigin;
            private readonly Vector2 _sIfZero;
            private readonly Vector2 _endTangent;

            public float MinT => 0;
            public float MaxT => _kP < Epsilon ? 1000 : _bParams.W;

            public ECurve(in StandardFormParams sfp, in BCurveParams bCurveParams, Vector2 sPoint)
            {
                _kP = sfp.Kp;
                _zeroCurvatureOrigin = sfp.ZeroCurvatureOrigin;
                if (_zeroCurvatureOrigin)
                {
                    _bParams = default;
                    _kQ = default;
                    _kPSquared = default;
                    _sIfZero = sPoint;
                    _endTangent = Vector2.UnitX;
                }
                else
                {
                    _bParams = bCurveParams;
                    _kQ = sfp.Kq;
                    _kPSquared = _kP * _kP;
                    _sIfZero = default;
                    var clipCenter = sfp.ClipCircleCenter;
                    var projectedCenter = sfp.RDirection * Vector2.Dot(sfp.RDirection, clipCenter);
                    var otherIntersection = 2 * projectedCenter - sfp.R;
                    _endTangent = (otherIntersection - clipCenter).Rotate90();
                }
            }

            private float RotationInner(float t, float bSquared) => MathHelper.PiOver2 * (t - _kPSquared * bSquared);

            public Vector2 Position(float t)
            {
                if (_zeroCurvatureOrigin) return _sIfZero + new Vector2(t, 0);
                _bParams.Get(t, out var bSquared, out var b);
                var curvePt = CurvePoint(b, _kQ, _kP, RotationInner(t, bSquared));

                var secondRot = MathHelper.PiOver2 * t;
                var offsetPt = new Vector2((float)Math.Sin(secondRot), 1 - (float)Math.Cos(secondRot));
                offsetPt /= _kP;
                return curvePt + offsetPt;
            }

            public Vector2 Tangent(float t)
            {
                if (_zeroCurvatureOrigin) return Vector2.UnitX;

                // Not differentiable at t = MaxT, so pull the tangent from elsewhere
                if (t >= MaxT - Epsilon) return _endTangent;

                _bParams.GetDerivative(t, out var bSquared, out var b, out var bSquaredDerivative, out var bDerivative);

                var rotationInner = RotationInner(t, bSquared);
                var rotationInnerDerivative = MathHelper.PiOver2 * (1 - _kPSquared * bSquaredDerivative);

                var curvePtDerivative = CurvePointDerivative(b, bDerivative,
                    bSquared, bSquaredDerivative,
                    _kQ, _kP, rotationInner, rotationInnerDerivative);


                var secondRot = MathHelper.PiOver2 * t;
                const float secondRotDerivative = MathHelper.PiOver2;
                var offsetPtDerivative = new Vector2((float)Math.Cos(secondRot), (float)Math.Sin(secondRot)) * secondRotDerivative;
                offsetPtDerivative /= _kP;
                return curvePtDerivative + offsetPtDerivative;
            }

            public WhichSideResult DetermineWhichSide(Vector2 test)
            {
                if (!_zeroCurvatureOrigin) return WhichSide.ComputeClockwise(in this, test);
                if (Math.Abs(test.Y - _sIfZero.Y) < Epsilon) return WhichSideResult.OnCurve;
                return test.Y > _sIfZero.Y ? WhichSideResult.Concave : WhichSideResult.Convex;
            }
        }

        public static Suggestion SuggestStandardForm(float curvatureOrigin, float curvatureOther, Vector2 toTangent)
        {
            var sfp = new StandardFormParams(curvatureOrigin, curvatureOther, toTangent);
            if (Math.Abs(curvatureOrigin) < Epsilon)
                return new Suggestion(false, sfp.R, sfp.RDirection, Vector2.UnitX);

            return new Suggestion(true, sfp.R, sfp.RDirection, sfp.ClipCircleCenter);
        }

        private readonly struct CircleCurve : ICurve2
        {
            private readonly Vec2Circle _circle;

            public CircleCurve(Vec2Circle circle)
            {
                _circle = circle;
            }

            public float MinT => 0;
            public float MaxT => MathHelper.TwoPi;
            public Vector2 Position(float t) => _circle.Center + _circle.Radius * VectorExtensions.RotatedX(t);

            public Vector2 Tangent(float t) => VectorExtensions.RotatedY(t);
        }

        private readonly struct StraightCurve : ICurve2
        {
            private readonly Vec2Plane _plane;

            public StraightCurve(Vec2Plane plane, float minT = -100, float maxT = 100)
            {
                _plane = plane;
                MinT = minT;
                MaxT = maxT;
            }

            public float MinT { get; }

            public float MaxT { get; }

            public Vector2 Position(float t) => _plane.Origin + _plane.Normal.Rotate90() * t;

            public Vector2 Tangent(float t) => _plane.Normal.Rotate90();
        }

        public static bool SolveStandardForm<T>(float curvatureP, in ControlPoint other, ref T collector) where T : ICurveCollector
        {
            if (curvatureP < 0 || other.Curvature < 0) return false;
            var sfp = new StandardFormParams(curvatureP, other.Curvature, other.Tangent);
            collector.AddDebugCurve(new StraightCurve(Vec2Plane.FromOriginNormal(Vector2.Zero, -sfp.RDirection.Rotate90()), 0, 100));
            if (sfp.R.Cross(other.Position) > Epsilon) return false;
            if (sfp.ZeroCurvatureOrigin)
            {
                collector.AddDebugCurve(new StraightCurve(Vec2Plane.FromOriginNormal(sfp.R, Vector2.UnitY)));
                if (other.Position.Y < sfp.R.Y) return false;
            }
            else
            {
                var radius = sfp.ClipCircleRadius;
                var circle = new Vec2Circle(sfp.ClipCircleCenter, radius * radius);
                collector.AddDebugCurve(new CircleCurve());
                if (!circle.Contains(other.Position)) return false;
            }

            sfp.CurveParameters(out var sPoint, out var dCurve, out var eCurve);

            collector.AddDebugCurve(dCurve);
            collector.AddDebugCurve(eCurve);

            bool isTypeA;
            if (Vector2.Dot(sfp.RDirection, other.Position) < Vector2.Dot(sfp.RDirection, sPoint))
            {
                var result = dCurve.DetermineWhichSide(other.Position);
                collector.AddDebug($"D Result: {result}");
                isTypeA = result != WhichSideResult.Concave;
            }
            else
            {
                var result = eCurve.DetermineWhichSide(other.Position);
                collector.AddDebug($"E Result: {result}");
                isTypeA = result != WhichSideResult.Concave;
            }

            if (isTypeA)
            {
                // ReSharper disable once ConvertIfStatementToReturnStatement
                if (sfp.ZeroCurvatureOrigin)
                    return SolveStraightSpiralArc(in sfp, other.Position, ref collector);
                return SolveArcSpiralArc(in sfp, other.Position, ref collector);
            }

            return SolveSpiralArcSpiral(in sfp, other.Position, ref collector);
        }

        private static bool SolveStraightSpiralArc<T>(in StandardFormParams sfp, Vector2 otherPos, ref T collector) where T : ICurveCollector
        {
            // Algorithm 2: "How to find a clothoid spline spiral of type A with Kp = 0"
            var w = sfp.W;
            var centerPoint = otherPos + VectorExtensions.RotatedY(sfp.WPiOver2) / sfp.Kq;

            // Solve equation 12 for L
            var leftSide = centerPoint.Y;
            leftSide -= 1 / sfp.Kq;
            leftSide *= sfp.Kq / MathHelper.Pi;
            float RootFindingFn(float lInner) => FresnelIntegral.IntegralAtTime((float)Math.Sqrt(w - lInner)).Y - leftSide;
            var root = new BisectingRootFinder(0, RootFindingFn(0), w, RootFindingFn(w));
            while (root.ShouldContinue(out var lInner))
                root.Next(lInner, RootFindingFn(lInner));
            var l = root.Midpoint;

            var spiralEndTime = (float)Math.Sqrt(w - l);
            var spiralScale = FresnelIntegral.CurvatureAtTime(spiralEndTime) / sfp.Kq;

            // Solve equation 13 for X
            var x = centerPoint.X - (MathHelper.Pi / sfp.Kq) * FresnelIntegral.IntegralAtTime(spiralEndTime).X;

            var spiralStartPos = new Vector2(x, 0);
            var spiralEndPos = spiralStartPos + FresnelIntegral.PositionAtTime(spiralEndTime) * spiralScale;
            if (x > Epsilon)
                collector.AddStraightLine(Vector2.Zero, spiralStartPos);
            if (spiralEndTime > Epsilon)
                collector.AddSpiralArc(new Vec2Transform(spiralStartPos, new Vector2(spiralScale, 0)), 0, spiralEndTime);
            if (l > Epsilon)
                collector.AddCircularArc(centerPoint, spiralEndPos, otherPos);
            return true;
        }

        private static bool SolveArcSpiralArc<T>(in StandardFormParams sfp, Vector2 otherPos, ref T collector) where T : ICurveCollector
        {
            // Algorithm 3: "How to find a clothoid spline spiral of type A with Kp > 0"
            var w = sfp.W;
            var centerPointStart = new Vector2(0, 1 / sfp.Kp);
            var centerPointEnd = otherPos + VectorExtensions.RotatedY(sfp.WPiOver2) / sfp.Kq;

            // Solve for scale, "B", that produces the desired center point distance
            var centerPointDistSquared = Vector2.DistanceSquared(centerPointStart, centerPointEnd);
            var kp = sfp.Kp;
            var kq = sfp.Kq;

            float RootFindingFn(float bInner)
            {
                var pt1 = (MathHelper.Pi * FresnelIntegral.IntegralAtTime(kp * bInner) + Vector2.UnitY) / kp;
                var pt2 = (MathHelper.Pi * FresnelIntegral.IntegralAtTime(kq * bInner) + Vector2.UnitY) / kq;
                var testDist = Vector2.DistanceSquared(pt1, pt2);
                return testDist - centerPointDistSquared;
            }

            var kq2MinusKp2 = kq * kq - kp * kp;
            var rootFindingRight = (float)Math.Sqrt(2 / kq2MinusKp2);
            var root = new BisectingRootFinder(
                0, RootFindingFn(0),
                rootFindingRight, RootFindingFn(rootFindingRight));
            while (root.ShouldContinue(out var lInner))
                root.Next(lInner, RootFindingFn(lInner));
            var b = root.Midpoint;

            var spiralStartTime = kp * b;
            var spiralEndTime = kq * b;

            var spiralTransform = Vec2Transform.FromControlPoints(
                FresnelIntegral.CenterPointAtTime(spiralStartTime), centerPointStart,
                FresnelIntegral.CenterPointAtTime(spiralEndTime), centerPointEnd);

            var spiralStart = spiralTransform.LocalToWorldPosition(FresnelIntegral.PositionAtTime(spiralStartTime));
            var spiralEnd = spiralTransform.LocalToWorldPosition(FresnelIntegral.PositionAtTime(spiralEndTime));

            if (spiralStart.X > Epsilon)
                collector.AddCircularArc(centerPointStart, Vector2.Zero, spiralStart);
            if (spiralEndTime > Epsilon)
                collector.AddSpiralArc(spiralTransform, spiralStartTime, spiralEndTime);
            if (spiralEnd.Y + Epsilon < otherPos.Y)
                collector.AddCircularArc(centerPointEnd, spiralEnd, otherPos);
            return true;
        }

        private static bool SolveSpiralArcSpiral<T>(in StandardFormParams sfp, Vector2 otherPos, ref T collector) where T : ICurveCollector
        {
            // Algorithm 4: "How to find a clothoid spline spiral of type B"
            var sfp2 = sfp;

            float RootFindingFn(float lInner)
            {
                var qCurveInner = new QCurve(in sfp2, lInner);
                var minT2 = qCurveInner.MinT;
                var maxT2 = qCurveInner.MaxT;
                var side = WhichSide.ComputeClockwise(in qCurveInner, otherPos, ref minT2, ref maxT2);
                // collectorCopy.AddDebugCurve(in qCurveInner);
                switch (side)
                {
                    case WhichSideResult.Concave:
                        return 1;
                    case WhichSideResult.OnCurve:
                        return 0;
                    case WhichSideResult.Convex:
                        return -1;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            var root = new BisectingRootFinder(
                0, -1,
                sfp.W, 1);
            while (root.ShouldContinue(out var midpoint))
                root.Next(midpoint, RootFindingFn(midpoint));
            var l = root.Midpoint;
            var qCurve = new QCurve(in sfp, l);
            var minT = qCurve.MinT;
            var maxT = qCurve.MaxT;
            //WhichSide.ComputeClockwise(in qCurve, otherPos, ref minT, ref maxT, out _);
            collector.AddDebugCurve(in qCurve);
            var k = RefinePosition(in qCurve, otherPos, minT, maxT);
            var b = qCurve.B;
            var p = qCurve.Position(k);
            collector.AddDebug($"Q at {k} -> {qCurve.Position(k)}");

            var spiralScale = MathHelper.Pi * b;
            var spiral1Start = sfp.Kp * b;
            var spiralMid = k * b;
            var spiral2End = sfp.Kq * b;

            var spiral1Transform = Vec2Transform.FromControlPointAndAxis(
                FresnelIntegral.PositionAtTime(spiral1Start),
                Vector2.Zero,
                spiralScale * VectorExtensions.RotatedX(-FresnelIntegral.AngleAtTime(spiral1Start)));

            var arcStartPos = spiral1Transform.LocalToWorldPosition(FresnelIntegral.PositionAtTime(spiralMid));
            var arcCenterPos = arcStartPos + spiral1Transform.LocalToWorldNormal(
                FresnelIntegral.DerivativeAtTime(spiralMid).Rotate90() /
                FresnelIntegral.CurvatureAtTime(spiralMid));
            var arcAngleRad = MathHelper.PiOver2 * l;
            var arcEndPos = arcStartPos - arcCenterPos;
            arcEndPos.Rotate(arcAngleRad);
            arcEndPos += arcCenterPos;

            var spiral2Transform = Vec2Transform.FromControlPoints(
                FresnelIntegral.PositionAtTime(spiralMid),
                arcEndPos,
                FresnelIntegral.PositionAtTime(spiral2End),
                otherPos);

            collector.AddDebug($"SCS B: {b} K: {k} L: {l} Err: {Vector2.Distance(qCurve.Position(k), otherPos)}");

            if (spiral1Start + Epsilon < spiralMid)
                collector.AddSpiralArc(spiral1Transform, spiral1Start, spiralMid);
            if (l > Epsilon)
                collector.AddCircularArc(arcCenterPos, arcStartPos, arcEndPos);
            if (spiralMid + Epsilon < spiral2End)
                collector.AddSpiralArc(spiral2Transform, spiralMid, spiral2End);
            return true;
        }

        private static float RefinePosition<T>(in T curve, Vector2 pos, float minT, float maxT) where T : ICurve2
        {
            var minV = curve.Position(minT);
            var maxV = curve.Position(maxT);
            while (minT + Epsilon < maxT)
            {
                var tangent = maxV - minV;

                var midT = (minT + maxT) / 2;
                var midV = curve.Position(midT);

                var midDot = Vector2.Dot(tangent, midV - minV);
                var posDot = Vector2.Dot(tangent, pos - minV);

                if (Math.Abs(midDot - posDot) < Epsilon)
                    return midT;
                if (midDot < posDot)
                {
                    minT = midT;
                    minV = midV;
                }
                else
                {
                    maxT = midT;
                    maxV = midV;
                }
            }

            return (minT + maxT) / 2;
        }

        public readonly struct StraightLineCurve : ICurve2
        {
            private readonly Vector2 _origin;
            private readonly Vector2 _tangent;

            public StraightLineCurve(Vector2 origin, Vector2 tangent)
            {
                _origin = origin;
                _tangent = tangent;
            }

            public float MinT => 0;
            public float MaxT => 1;
            public Vector2 Position(float t) => _origin + _tangent * t;

            public Vector2 Tangent(float t) => _tangent;
        }

        public readonly struct QCurve : ICurve2
        {
            private static readonly Vector2 YOverPi = new Vector2(0, 1 / MathHelper.Pi);

            public readonly float B;
            private readonly float _b2;
            private readonly float _sinPiOver4L;
            private readonly float _lOver2;
            private readonly float _kp2;
            private readonly Vector2 _piBFresnelKpB;
            private readonly Vector2 _piBFresnelKqB;
            private readonly VectorExtensions.CompactRotationMatrix2 _rotNegativeKp2B2;
            private readonly VectorExtensions.CompactRotationMatrix2 _rotWMinusKq2B2;

            public QCurve(in StandardFormParams sfp, float l)
            {
                _lOver2 = l / 2;
                _kp2 = sfp.Kp * sfp.Kp;
                _b2 = (sfp.W - l) / (sfp.Kq * sfp.Kq - _kp2);
                B = (float)Math.Sqrt(_b2);
                _sinPiOver4L = (float)Math.Sin(MathHelper.PiOver4 * l);
                _piBFresnelKpB = MathHelper.Pi * B * FresnelIntegral.PositionAtTime(sfp.Kp * B);
                _piBFresnelKqB = MathHelper.Pi * B * FresnelIntegral.PositionAtTime(sfp.Kq * B);
                _rotNegativeKp2B2 = VectorExtensions.CompactRotationMatrix2.Rotation(MathHelper.PiOver2 * -sfp.Kp * sfp.Kp * _b2);
                _rotWMinusKq2B2 = VectorExtensions.CompactRotationMatrix2.Rotation(sfp.WPiOver2 - MathHelper.PiOver2 * sfp.Kq * sfp.Kq * _b2);
                MinT = Math.Max(1e-6f, sfp.Kp);
                MaxT = sfp.Kq;
            }

            public float MinT { get; }
            public float MaxT { get; }

            public Vector2 Position(float k)
            {
                var firstUnRotatedTerm = MathHelper.Pi / k * (FresnelIntegral.IntegralAtTime(k * B) + YOverPi) - _piBFresnelKpB;
                var secondUnRotatedTerm = _piBFresnelKqB - MathHelper.Pi / k * (FresnelIntegral.IntegralAtTime(k * B) + YOverPi);

                return firstUnRotatedTerm.MultiplyRotationMatrix(_rotNegativeKp2B2) + secondUnRotatedTerm.MultiplyRotationMatrix(_rotWMinusKq2B2);
            }

            public string Params() => $"{_b2} {_sinPiOver4L} {_lOver2} {_kp2} {_piBFresnelKpB} {_piBFresnelKqB} {_rotNegativeKp2B2} {_rotWMinusKq2B2}";

            public Vector2 Tangent(float k)
            {
                var k2 = k * k;
                var angle = MathHelper.PiOver2 * ((k2 - _kp2) * _b2 + _lOver2);
                var dir = VectorExtensions.RotatedX(angle);
                return -2 / k2 * _sinPiOver4L * dir;
            }
        }


        private struct BisectingRootFinder
        {
            private readonly bool _ascending;
            private float _left;
            private float _right;

            public BisectingRootFinder(float left, float leftValue, float right, float rightValue)
            {
                _left = left;
                _right = right;
                _ascending = rightValue > leftValue;

                if (Math.Abs(leftValue) < Epsilon) _right = _left;
                else if (Math.Abs(rightValue) < Epsilon) _left = _right;
            }

            public float Midpoint => (_left + _right) / 2;

            public bool ShouldContinue(out float midpoint)
            {
                midpoint = Midpoint;
                return _left + Epsilon < _right;
            }

            public void Next(float x, float y)
            {
                if (Math.Abs(y) < Epsilon)
                {
                    _left = _right = x;
                    return;
                }

                if (y < 0 == _ascending)
                    _left = x;
                else
                    _right = x;
            }
        }

        private struct TransformedCollector<T> : ICurveCollector where T : ICurveCollector
        {
            internal T Backing;
            private readonly Vec2Transform _crs;

            public TransformedCollector(T backing, Vec2Transform crs)
            {
                Backing = backing;
                _crs = crs;
            }

            public void AddStraightLine(Vector2 a, Vector2 b)
            {
                Backing.AddStraightLine(_crs.LocalToWorldPosition(a), _crs.LocalToWorldPosition(b));
            }

            public void AddCircularArc(Vector2 center, Vector2 from, Vector2 to)
            {
                Backing.AddCircularArc(_crs.LocalToWorldPosition(center), _crs.LocalToWorldPosition(from), _crs.LocalToWorldPosition(to));
            }

            public void AddSpiralArc(in Vec2Transform transform, float tStart, float tEnd)
            {
                var newTranslation = _crs.LocalToWorldPosition(transform.Translation);
                var xAxis = _crs.LocalToWorldNormal(transform.AxisX);
                var yAxis = _crs.LocalToWorldNormal(transform.AxisY);
                Backing.AddSpiralArc(new Vec2Transform(newTranslation, xAxis, yAxis), tStart, tEnd);
            }

            public void AddDebug(string msg)
            {
                Backing.AddDebug(msg);
            }

            public void AddDebugCurve<T1>(in T1 curve) where T1 : ICurve2
            {
                Backing.AddDebugCurve(new TransformedCurve<T1>(_crs, curve));
            }
        }

        private readonly struct TransformedCurve<T> : ICurve2 where T : ICurve2
        {
            private readonly Vec2Transform _crs;
            private readonly T _curve;

            public TransformedCurve(Vec2Transform crs, T curve)
            {
                _crs = crs;
                _curve = curve;
            }

            public float MinT => _curve.MinT;
            public float MaxT => _curve.MaxT;
            public Vector2 Position(float t) => _crs.LocalToWorldPosition(_curve.Position(t));

            public Vector2 Tangent(float t) => _crs.LocalToWorldNormal(_curve.Tangent(t));
        }

        public readonly struct ControlPoint
        {
            public readonly Vector2 Position;
            public readonly Vector2 Tangent;
            public readonly float Curvature;

            public float Radius => 1 / Curvature;
            public Vector2 ArcCenter => Position + Radius * Tangent.Rotate90();

            public ControlPoint(Vector2 position, Vector2 tangent, float curvature)
            {
                Position = position;
                Tangent = tangent;
                Curvature = curvature;
            }
        }
    }
}