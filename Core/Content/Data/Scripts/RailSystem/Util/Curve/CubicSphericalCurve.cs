using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util.Curve
{
    public readonly struct CubicSphericalCurve : ICurve
    {
        private readonly MatrixD _matrix;
        private readonly CubicCurve _curve;

        public CubicSphericalCurve(Vector3D center, MatrixD from, MatrixD to, 
            float smoothnessFrom = CubicCurve.DefaultSmoothness, 
            float smoothnessTo = CubicCurve.DefaultSmoothness)
        {
            var tmp = new CubicCurve(from, to, smoothnessFrom, smoothnessTo);
            var avgSpherical = Vector3D.Normalize((from.Translation + to.Translation) / 2 - center);
            var quat = QuaternionD.CreateFromTwoVectors(avgSpherical, new Vector3D(1, 0, 0));
            var m = MatrixD.CreateFromQuaternion(quat);
            m = MatrixD.CreateTranslation(-center) * m;
            _matrix = MatrixD.Invert(m);
            _curve = new CubicCurve(SphericalExtensions.ToSpherical(Vector3D.Transform(tmp.P0, m)),
                SphericalExtensions.ToSpherical(Vector3D.Transform(tmp.P1, m)),
                SphericalExtensions.ToSpherical(Vector3D.Transform(tmp.P2, m)),
                SphericalExtensions.ToSpherical(Vector3D.Transform(tmp.P3, m)));
        }

        public Vector3D Sample(float t)
        {
            return Vector3D.Transform(SphericalExtensions.FromSpherical(_curve.Sample(t)), _matrix);
        }

        public Vector3D SampleDerivative(float t)
        {
            return Vector3D.TransformNormal(
                SphericalExtensions.DifferentialFromSpherical(_curve.Sample(t), _curve.SampleDerivative(t)), _matrix);
        }

        public Vector3D SampleSecondDerivative(float t)
        {
            // Not going to bother to write this out.
            const float dt = 1e-3f;
            var prev = SampleDerivative(t - dt);
            var next = SampleDerivative(t + dt);
            return (next - prev) / (2 * dt);
        }
    }
}