using System;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util
{
    public static class FresnelIntegral
    {
        private const double FpMin = 1e-30;
        private static readonly double SqrtFpMin = Math.Sqrt(FpMin);
        private const double XMin = 1.5;
        private const double Eps = 6e-8;
        private const int MaxIt = 100;

        private const int InvTableScale = 100;
        private const double TableScale = 1.0 / InvTableScale;
        private const double MaxTable = 32;

        // Packed values (dirFromCenterX, dirFromCenterY, radius)
        private static readonly float[] Table = new float[(int) (MaxTable * InvTableScale * 3)];

        public static float CurvatureAtTime(float t) => MathHelper.Pi * t;

        public static float TimeForCurvature(float curvature) => curvature / MathHelper.Pi;

        public static Vector2 DerivativeAtTime(float t)
        {
            DerivativeAtTime(t, out var dx, out var dy);
            return new Vector2(dx, dy);
        }

        public static void DerivativeAtTime(float t, out float dx, out float dy)
        {
            var angle = AngleAtTime(t);
            dx = (float) Math.Cos(angle);
            dy = (float) Math.Sin(angle);
        }

        public static Vector2 IntegralAtTime(float t)
        {
            var core = PositionAtTime(t);
            var angle = AngleAtTime(t); 
            return new Vector2(
                t * core.X - (float)Math.Sin(angle) / MathHelper.Pi,
                t * core.Y + ((float)Math.Cos(angle) - 1) / MathHelper.Pi
            );
        }

        /// <summary>
        /// Computes the angle of the Euler spiral's tangent vector relative to the +S axis.  The resulting angle is positive, but not modulo two pi. 
        /// </summary>
        public static float AngleAtTime(float t) => MathHelper.PiOver2 * t * t;

        public static Vector2 CenterPointAtTime(float t)
        {
            var curve = PositionAtTime(t);
            var tangent = DerivativeAtTime(t);
            var normal = tangent.Rotate90();
            return curve + normal / CurvatureAtTime(t);
        }

        public static void CalculatePositionAtTime(double t, out double x, out double y)
        {
            // http://www.foo.be/docs-free/Numerical_Recipe_In_C/c6-9.pdf
            var absX = Math.Abs(t);
            if (absX < SqrtFpMin)
            {
                x = absX;
                y = 0;
                return;
            }

            if (absX <= XMin)
            {
                // Evaluate using series
                var sum = 0.0;
                var sumS = 0.0;
                var sumC = absX;
                var sign = 1.0;
                var fact = MathHelper.PiOver2 * absX * absX;
                var odd = true;
                var term = absX;
                var n = 3;
                int k;
                for (k = 1; k <= MaxIt; k++)
                {
                    term *= fact / k;
                    sum += sign * term / n;
                    var test = Math.Abs(sum) * Eps;
                    if (odd)
                    {
                        sign = -sign;
                        sumS = sum;
                        sum = sumC;
                    }
                    else
                    {
                        sumC = sum;
                        sum = sumS;
                    }

                    if (term < test) break;
                    odd = !odd;
                    n += 2;
                }

                // if (k > MaxIt) Console.WriteLine("Failed to converge");
                x = sumC;
                y = sumS;
            }
            else
            {
                // Evaluate continued fraction
                var piX2 = Math.PI * absX * absX;
                var b = new Complex(1, -piX2);
                var cc = new Complex(1.0 / FpMin, 0);
                var d = b.Inverse();
                var h = d;
                var n = -1;
                int k;
                for (k = 2; k <= MaxIt; k++)
                {
                    n += 2;
                    var a = -n * (n + 1);
                    b += new Complex(4, 0);
                    d = (d * a + b).Inverse();
                    cc = b + cc.Inverse(a);
                    var del = cc * d;
                    h *= del;

                    if (Math.Abs(del.Real - 1.0) + Math.Abs(del.Imaginary) < Eps) break;
                }

                // if (k > MaxIt) Console.WriteLine("cf failed to converge");
                h = new Complex(-absX, absX) * h;
                var cs = new Complex(0.5, 0.5) * (Complex.RealOne - Complex.Euler(0.5 * piX2) * h);
                x = 1 - cs.Real;
                y = 1 - cs.Imaginary;
            }
        }

        public static Vector2 PositionAtTime(float t)
        {
            PositionAtTime(t, out var x, out var y);
            return new Vector2(x, y);
        }

        public static void PositionAtTime(float t, out float x, out float y)
        {
            if (t > MaxTable - TableScale)
            {
                CalculatePositionAtTime(t, out var tmpX, out var tmpY);
                x = (float) tmpX;
                y = (float) tmpY;
                return;
            }

            var slotFloat = t * InvTableScale;
            var slotI = (int) Math.Floor(slotFloat);
            var lerpAmount = slotFloat - slotI;

            var offset = slotI * 3;
            var dirX = MathHelper.Lerp(Table[offset], Table[offset + 3], lerpAmount);
            var dirY = MathHelper.Lerp(Table[offset + 1], Table[offset + 4], lerpAmount);
            var length = MathHelper.Lerp(Table[offset + 2], Table[offset + 5], lerpAmount);
            // normalize direction
            length /= (float) Math.Sqrt(dirX * dirX + dirY * dirY);
            x = dirX * length + 0.5f;
            y = dirY * length + 0.5f;
        }

        static FresnelIntegral()
        {
            for (var i = 0; i < Table.Length; i += 3)
            {
                var time = i * TableScale / 3;
                CalculatePositionAtTime(time, out var x, out var y);
                var xF = (float) (x - 0.5);
                var yF = (float) (y - 0.5);
                var length = (float) Math.Sqrt(xF * xF + yF * yF);
                Table[i] = xF / length;
                Table[i + 1] = yF / length;
                Table[i + 2] = length;
            }
        }
    }
}