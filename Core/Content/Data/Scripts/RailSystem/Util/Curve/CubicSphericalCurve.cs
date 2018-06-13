using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util.Curve
{
    public struct CubicSphericalCurve : ICurve
    {
        private readonly MatrixD _matrix;
        private CubicCurve _curve;

        public CubicSphericalCurve(Vector3D center, MatrixD from, MatrixD to)
        {
            var tmp = new CubicCurve(from, to);
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
    }
}