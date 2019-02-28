using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.RailSystem.Util;
using VRage.Collections;
using VRage.Components;
using VRage.Logging;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy
{
    public class Node : UserDataTable
    {
        public readonly BendyLayer Graph;
        public Vector3D Position { get; private set; }
        public Vector3 UpBias { get; private set; }
        public Vector3 Up { get; private set; }
        private int _proxyId = -1;
        public int TangentPins { get; private set; }

        public Node(BendyLayer s, Vector3D pos, Vector3D up)
        {
            Position = pos;
            Up = UpBias = Vector3.Normalize((Vector3) up);
            Tangent = Vector3.Normalize(Vector3.CalculatePerpendicularVector(Up));
            Graph = s;
            TangentPins = 0;
            EnsureInScene();
        }

        public void Pin(MatrixD matrix)
        {
            if (TangentPins == 0)
            {
                Tangent = (Vector3) matrix.Forward;
                Position = matrix.Translation;
                UpBias = Up = (Vector3) matrix.Up;
                MarkDirty();
            }

            TangentPins++;
        }

        public void UnpinTangent()
        {
            TangentPins--;
            if (TangentPins == 0 && InScene)
                MarkDirty();
            else if (TangentPins < 0)
                GetLogger().Warning($"Unpinned node more than we pinned");
        }

        public Vector3 Tangent { get; private set; }

        public delegate void DelNeighborChanged(Node self, Node target, Edge via);

        public event DelNeighborChanged NeighborAdded;
        public event DelNeighborChanged NeighborRemoved;

        private readonly Dictionary<Node, Edge> _neighbors = new Dictionary<Node, Edge>();

        public DictionaryReader<Node, Edge> Connections => _neighbors;
        public DictionaryKeysReader<Node, Edge> Neighbors => new DictionaryKeysReader<Node, Edge>(_neighbors);
        public DictionaryValuesReader<Node, Edge> Edges => _neighbors;
        public MatrixD Matrix => MatrixD.CreateWorld(Position, Tangent, Up);

        public Edge ConnectionTo(Node other)
        {
            return _neighbors.GetValueOrDefault(other);
        }

        private void RemoveNeighbor(Node n, Edge via)
        {
            if (via != _neighbors.GetValueOrDefault(n))
                GetLogger().Warning("Remove neighbor via improper edge");
            _neighbors.Remove(n);
            NeighborRemoved?.Invoke(this, n, via);
        }

        private void AddNeighbor(Node n, Edge via)
        {
            _neighbors.Add(n, via);
            NeighborAdded?.Invoke(this, n, via);
        }

        /// <summary>
        /// Do not directly use!  Edge handles this.
        /// </summary>
        internal static void AddConnection(Edge edge)
        {
            edge.From.AddNeighbor(edge.To, edge);
            edge.From.MarkDirty();
            edge.To.AddNeighbor(edge.From, edge);
            edge.To.MarkDirty();
        }

        /// <summary>
        /// Do not directly use!  Edge handles this.
        /// </summary>
        internal static void RemoveConnection(Edge edge)
        {
            edge.From.RemoveNeighbor(edge.To, edge);
            edge.To.RemoveNeighbor(edge.From, edge);

            if (edge.From._neighbors.Count > 0)
                edge.From.MarkDirty();
            else
                edge.From.Close();
            if (edge.To._neighbors.Count > 0)
                edge.To.MarkDirty();
            else
                edge.To.Close();
        }

        private bool _dirty;

        public void MarkDirty()
        {
            if (!InScene)
                GetLogger().Warning($"Node not in scene marked dirty");
            lock (this)
            {
                if (_dirty)
                    return;
                MoveProxy();
                Graph.NodesForUpdate.Enqueue(this);
                _dirty = true;
            }
        }

        private NamedLogger GetLogger()
        {
            return Graph.Owner.GetLogger().WithContext(this);
        }

        private void MoveProxy()
        {
            var bb = BoundingBoxD.CreatePoint(Position).Inflate(0.005f);
            if (_proxyId >= 0)
                Graph.Nodes.MoveProxy(_proxyId, in bb, Vector3D.Zero);
            else
                _proxyId = Graph.Nodes.AddProxy(in bb, this, 0);
        }

        public void DeferredUpdate()
        {
            lock (this)
            {
                if (!_dirty)
                    return;
                _dirty = false;
            }

            if (TangentPins == 0)
            {
                Tangent = CalculateTangent();
                var norm = Vector3.Cross(UpBias, Tangent);
                Up = Vector3.Normalize(Vector3.Cross(Tangent, norm));
            }

            Graph.RaiseNodeMoved(this);

            // Two levels b/c tracks use data from 3 nodes.
            foreach (var a in _neighbors.Keys)
            foreach (var b in a._neighbors.Values)
                b.MarkDirty();
        }

        private Vector3 CalculateTangent()
        {
            if (TangentPins > 0)
                return Tangent;
            if (_neighbors.Count == 0)
            {
                var alt = Vector3.CalculatePerpendicularVector(Up);
                alt.Normalize();
                return alt;
            }

            var tan = Vector3.Zero;
            foreach (var k in _neighbors.Keys)
            {
                var t = Vector3.Normalize(k.Position - Position);
                if (t.Dot(tan) < 0)
                    t = -t;
                tan += t;
            }
            return Vector3.Normalize(tan);
        }


        public bool InScene { get; private set; }

        public void EnsureInScene()
        {
            if (InScene)
                return;
            InScene = true;
            Graph.NodeList.Add(this);
            Graph.RaiseNodeCreated(this);
            MarkDirty();
        }

        public void Close()
        {
            if (_neighbors.Count > 0)
                GetLogger().Warning($"Closed a node when there were still neighbors");
            foreach (var k in _neighbors.Values.ToArray())
                k.Close();
            if (_proxyId >= 0)
            {
                Graph.Nodes.RemoveProxy(_proxyId);
                _proxyId = -1;
            }

            Graph.RaiseNodeRemoved(this);
            Graph.NodeList.Remove(this);
            InScene = false;
        }
    }
}