using System;

namespace Equinox76561198048419394.RailSystem.Util
{
    public readonly struct Complex
    {
        public static readonly Complex RealOne = new Complex(1, 0);
        
        public readonly double Real;
        public readonly double Imaginary;

        public Complex(double real, double imaginary)
        {
            Real = real;
            Imaginary = imaginary;
        }

        public Complex Inverse(double andScale = 1)
        {
            // RealOne / this
            
            var multiplier = andScale / (Real * Real + Imaginary * Imaginary);
            return new Complex(Real * multiplier, -Imaginary * multiplier);
        }

        public static Complex Euler(double theta) => new Complex(Math.Cos(theta), Math.Sin(theta));

        public static Complex operator +(Complex lhs, Complex rhs) => new Complex(lhs.Real + rhs.Real, lhs.Imaginary + rhs.Imaginary);

        public static Complex operator -(Complex lhs, Complex rhs) => new Complex(lhs.Real - rhs.Real, lhs.Imaginary - rhs.Imaginary);

        public static Complex operator *(Complex lhs, Complex rhs) => new Complex(
            lhs.Real * rhs.Real - lhs.Imaginary * rhs.Imaginary,
            lhs.Real * rhs.Imaginary + lhs.Imaginary * rhs.Real);

        public static Complex operator *(Complex lhs, double value) => new Complex(lhs.Real * value, lhs.Imaginary * value);

        public static Complex operator /(Complex numerator, Complex denominator)
        {
            var multiplier = 1 / (denominator.Real * denominator.Real + denominator.Imaginary * denominator.Imaginary);
            return new Complex(
                (numerator.Real * denominator.Real + numerator.Imaginary * denominator.Imaginary) * multiplier,
                (numerator.Imaginary * denominator.Real - numerator.Real * denominator.Imaginary) * multiplier);
        }
    }
}