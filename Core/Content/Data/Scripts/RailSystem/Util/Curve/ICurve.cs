using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util.Curve
{
    public interface ICurve
    {
        Vector3D Sample(float t);
        Vector3D SampleDerivative(float t);
    }
}