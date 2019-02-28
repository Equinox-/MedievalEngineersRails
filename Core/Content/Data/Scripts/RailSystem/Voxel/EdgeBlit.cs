using Equinox76561198048419394.RailSystem.Bendy;
using Equinox76561198048419394.RailSystem.Util.Curve;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Voxel
{
    public struct EdgeBlit
    {
        public Vector3 FromUp, ToUp;
        public CurveBlit Curve;

        public EdgeBlit(Edge edge)
        {
            FromUp = (Vector3) edge.FromMatrix.Up;
            ToUp = (Vector3) edge.ToMatrix.Up;
            Curve = CurveBlit.Convert(edge.Curve);
        }
    }
}