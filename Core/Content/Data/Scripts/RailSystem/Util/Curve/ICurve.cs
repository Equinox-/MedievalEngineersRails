using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util.Curve
{
    public interface ICurve
    {
        Vector3D Sample(float t);
        Vector3D SampleDerivative(float t);
    }

    public interface ICurve2
    {
        float MinT { get; }
        float MaxT { get; }
        Vector2 Position(float t);
        Vector2 Tangent(float t);
    }
}