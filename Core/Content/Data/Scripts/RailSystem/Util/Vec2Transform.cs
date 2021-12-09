using System;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util
{
    public readonly struct Vec2Transform
    {
        public readonly Vector2 Translation;
        public readonly Vector2 AxisX;
        public readonly Vector2 AxisY;

        public static Vec2Transform FromControlPoints(Vector2 from1, Vector2 to1, Vector2 from2, Vector2 to2)
        {
            var fromDir = from2 - from1;
            var toDir = to2 - to1;
            var fromLen = fromDir.Length();
            var toLen = toDir.Length();
            fromDir /= fromLen;
            toDir /= toLen;
            var scale = toLen / fromLen;

            var cross = fromDir.X * toDir.Y - fromDir.Y * toDir.X;
            var dot = Vector2.Dot(toDir, fromDir);
            var angle = Math.Atan2(cross, dot);
            var rotSin = (float)Math.Sin(angle);
            var rotCos = (float)Math.Cos(angle);

            var xAxis = new Vector2(rotCos, rotSin) * scale;
            return FromControlPointAndAxis(from1, to1, xAxis);
        }

        public static Vec2Transform FromControlPointAndAxis(Vector2 from1, Vector2 to1, Vector2 xAxis)
        {
            var yAxis = xAxis.Rotate90();
            var rotOnly = new Vec2Transform(Vector2.Zero, xAxis, yAxis);
            var translation = to1 - rotOnly.LocalToWorldPosition(from1);
            return new Vec2Transform(translation, xAxis, yAxis);
        }

        public Vec2Transform(Vector2 translation, Vector2 axisX, int flipY = 1)
        {
            Translation = translation;
            AxisX = axisX;
            AxisY = axisX.Rotate90() * flipY;
        }

        public Vec2Transform(Vector2 translation, Vector2 axisX, Vector2 axisY)
        {
            Translation = translation;
            AxisX = axisX;
            AxisY = axisY;
        }

        public Vector2 LocalToWorldNormal(Vector2 normal) => new Vector2(normal.X * AxisX.X + normal.Y * AxisY.X, normal.X * AxisX.Y + normal.Y * AxisY.Y);

        public Vector2 LocalToWorldPosition(Vector2 position) => LocalToWorldNormal(position) + Translation;

        public Vector2 WorldToLocalNormal(Vector2 normal) => new Vector2(normal.X * AxisX.X + normal.Y * AxisX.Y, normal.X * AxisY.X + normal.Y * AxisY.Y);

        public Vector2 WorldToLocalPosition(Vector2 position) => WorldToLocalNormal(position - Translation);
    }
}