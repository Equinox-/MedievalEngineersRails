using System;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util.Curve
{
    public readonly struct ArcCurve2 : ICurve2
    {
        public readonly Vector2 Center;
        public readonly float Radius;
        public readonly float ThetaStart;
        public readonly float ThetaEnd;
        private readonly bool _reversed;

        public ArcCurve2(Vector2 center, float radius, float thetaStart, float thetaEnd)
        {
            Center = center;
            Radius = radius;
            ThetaStart = thetaStart;
            ThetaEnd = thetaEnd;
            _reversed = ThetaEnd < ThetaStart;
        }

        public float MinT => 0;
        public float MaxT => Radius * Math.Abs(ThetaEnd - ThetaStart);

        private float Theta(float t)
        {
            var th = t / Radius;
            return _reversed ? ThetaStart - th : ThetaStart + th;
        }
        
        public Vector2 Position(float t)
        {
            return Center + Radius * VectorExtensions.RotatedX(Theta(t));
        }

        public Vector2 Tangent(float t)
        {
            return VectorExtensions.RotatedY(Theta(t) + (_reversed ? MathHelper.Pi : 0));
        }
    }
}