using System.Collections.Generic;
using Equinox76561198048419394.RailSystem.Bendy;
using Equinox76561198048419394.RailSystem.Util;
using VRage.Components.Entity.Camera;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Definition
{
    public class SwitchableNodeSide
    {
        private readonly bool _biasNegative;
        private readonly HashSet<Edge> _edges = new HashSet<Edge>();
        private readonly List<Edge> _edgeTable = new List<Edge>();

        private Edge _switchedEdge;

        public bool IsRequired
        {
            get
            {
                EnsureBuild();
                return _edges.Count > 1 && SwitchedEdge != null;
            }
        }

        public Node Junction { get; }

        public Node Target => SwitchedEdge?.Opposition(Junction);

        public Vector3 Tangent => Junction.Tangent * (_biasNegative ? -1 : 1);

        public IReadOnlyList<Edge> Candidates
        {
            get
            {
                EnsureBuild();
                return _edgeTable;
            }
        }

        public Edge SwitchedEdge
        {
            get
            {
                EnsureBuild();
                return _switchedEdge;
            }
            private set
            {
                Assert.True(_edges.Contains(value), $"Switching to invalid edge");
                if (_switchedEdge == value)
                    return;
                _switchedEdge = value;
                SwitchChanged?.Invoke(this, Junction, _switchedEdge?.Opposition(Junction));
            }
        }

        public delegate void SwitchChangedDel(SwitchableNodeSide self, Node junction, Node destination);

        public event SwitchChangedDel SwitchChanged;

        public SwitchableNodeSide(Node n, bool negativeBias)
        {
            _biasNegative = negativeBias;
            Junction = n;
            Junction.NeighborAdded += NeighborAdded;
            Junction.NeighborRemoved += NeighborRemoved;
            n.Graph.NodeMoved += NodeMoved;
            _dirty = _tableDirty = true;
        }

        public void Destroy()
        {
            _edges.Clear();
            _edgeTable.Clear();
            Junction.NeighborAdded -= NeighborAdded;
            Junction.NeighborRemoved -= NeighborRemoved;
            if (Junction.Graph != null)
                Junction.Graph.NodeMoved -= NodeMoved;
        }

        internal void DebugDraw()
        {
            if (!IsRequired)
                return;
            if (Vector3D.DistanceSquared(MyCameraComponent.ActiveCamera.GetPosition(), Junction.Position) > 100 * 100)
                return;

            foreach (var e in _edgeTable)
            {
                var tStart = 0f;
                var tEnd = 0.25f;
                if (e.To == Junction)
                {
                    tStart = 0.75f;
                    tEnd = 1f;
                }

                e.Draw(tStart, tEnd, e == SwitchedEdge ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0, 0, 0.5f), 1);
            }
        }

        private void RegisterEdge(Edge edge)
        {
            if (!_edges.Add(edge))
                return;
            _tableDirty = true;
        }

        private void UnregisterEdge(Edge edge)
        {
            if (!_edges.Remove(edge))
                return;
            _tableDirty = true;
        }

        private bool _dirty;
        private bool _tableDirty;

        private void EnsureBuild()
        {
            if (_dirty)
            {
                _dirty = false;
                FilterEdges();
            }

            if (_tableDirty)
            {
                _tableDirty = false;
                RebuildTable();
            }
        }

        private void RebuildTable()
        {
            // if removing SwitchedEdge we need to change the SwitchedEdge
            var activeRemoved = SwitchedEdge != null && !_edges.Contains(SwitchedEdge);
            var sampleIndex = activeRemoved ? _edgeTable.IndexOf(SwitchedEdge) : -1;
            _edgeTable.Clear();
            foreach (var k in _edges)
                _edgeTable.Add(k);
            _edgeTable.Sort((a, b) =>
            {
                var tangentA = Vector3.Normalize(a.Opposition(Junction).Position - Junction.Position);
                var tangentB = Vector3.Normalize(b.Opposition(Junction).Position - Junction.Position);
                var biasCheck = Vector3.Cross(Junction.Up, Junction.Tangent); // keep this here to avoid capture
                return biasCheck.Dot(tangentA).CompareTo(biasCheck.Dot(tangentB));
            });
            SwitchedEdge = _edgeTable.Count > 0 ? _edgeTable[(sampleIndex + _edgeTable.Count) % _edgeTable.Count] : null;
        }

        private void NodeMoved(Node obj)
        {
            if (obj == Junction || Junction.ConnectionTo(obj) != null)
                _dirty = true;
        }

        private void NeighborAdded(Node self, Node neighbor, Edge via)
        {
            _dirty = true;
        }

        private void NeighborRemoved(Node self, Node neighbor, Edge via)
        {
            UnregisterEdge(via);
        }

        private void FilterEdges()
        {
            if (Junction == null)
                return;
            foreach (var k in Junction.Connections)
                if (IsValidForSwitch(Junction, k.Key, _biasNegative))
                    RegisterEdge(k.Value);
                else
                    UnregisterEdge(k.Value);
        }

        public static bool IsValidForSwitch(Node junction, Node other, bool reversedBias)
        {
            var bias = junction.Tangent;
            if (reversedBias)
                bias *= -1;
            return (other.Position - junction.Position).Dot(bias) > 0;
        }

        public void SwitchRelative(int relativeIndex)
        {
            EnsureBuild();
            if (_edgeTable.Count == 0)
                return;
            // unsigned modulo
            var newIndex = _edgeTable.IndexOf(SwitchedEdge) + relativeIndex;
            newIndex %= _edgeTable.Count;
            newIndex += _edgeTable.Count;
            newIndex %= _edgeTable.Count;
            SwitchTo(_edgeTable[newIndex]);
        }

        public void SwitchTo(Node target)
        {
            EnsureBuild();
            foreach (var k in _edgeTable)
                if (k.Opposition(Junction) == target)
                {
                    SwitchTo(k);
                    return;
                }
        }

        internal void SwitchToInternal(Edge edge)
        {
            EnsureBuild();
            if (!_edges.Contains(edge))
                return;
            SwitchedEdge = edge;
        }

        public void SwitchTo(Edge edge)
        {
            EnsureBuild();
            if (!_edges.Contains(edge))
                return;
            RailSegmentComponent.RequestSwitchNet(Junction, edge);
        }

        public bool IsSwitchedTo(Node target)
        {
            EnsureBuild();
            var edge = SwitchedEdge;
            if (edge == null)
                return false;
            return edge.Opposition(Junction) == target;
        }
    }
}