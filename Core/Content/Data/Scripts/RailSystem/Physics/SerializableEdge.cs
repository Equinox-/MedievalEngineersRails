using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Bendy;
using VRage.Serialization;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Physics
{
    public sealed class SerializableEdge
    {
        private Vector3D? _from;
        private Vector3D? _to;
        private Edge _edgeCache;

        public void SetEdge(Vector3D from, Vector3D to)
        {
            _from = from;
            _to = to;
            _edgeCache = null;
        }

        public Edge GetEdge(BendyLayer layer)
        {
            if (_from == null || _to == null)
                return null;
            if (_edgeCache != null && _edgeCache.Graph == layer && _edgeCache.InScene)
                return _edgeCache;
            var fromNode = layer.GetNode(_from.Value);
            if (fromNode == null) return null;
            var toNode = layer.GetNode(_to.Value);
            if (toNode == null) return null;
            return _edgeCache = layer.GetEdge(fromNode, toNode);
        }

        public void SetEdge(Edge edge)
        {
            _edgeCache = edge;
            _from = edge?.From.Position;
            _to = edge?.To.Position;
        }
    }
}