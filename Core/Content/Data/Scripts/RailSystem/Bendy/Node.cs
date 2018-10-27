using System;
using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.RailSystem.Util;
using VRage.Library.Logging;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy
{
    public class Node
    {
        public readonly BendyLayer Graph;
        public Vector3D Position { get; private set; }
        public Vector3D UpBias { get; private set; }
        public Vector3D Up { get; private set; }
        private int _proxyId = -1;
        public int TangentPins { get; private set; }

        public Node(BendyLayer s, Vector3D pos, Vector3D up)
        {
            Position = pos;
            Up = UpBias = up;
            Tangent = Vector3D.Normalize(Vector3D.Cross(UpBias, UpBias.Shifted()));
            Graph = s;
            TangentPins = 0;
            EnsureInScene();
        }

        public void Pin(MatrixD matrix)
        {
            if (TangentPins == 0)
            {
                Tangent = matrix.Forward;
                Position = matrix.Translation;
                UpBias = Up = matrix.Up;
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
                MyLog.Default.Warning($"Unpinned node more than we pinned");
        }

        public Vector3D Tangent { get; private set; }

        private readonly Dictionary<Node, Edge> _neighbors = new Dictionary<Node, Edge>();

        public IEnumerable<Node> Neighbors => _neighbors.Keys;
        public IEnumerable<Edge> Edges => _neighbors.Values;
        public MatrixD Matrix => MatrixD.CreateWorld(Position, Tangent, Up);

        public Edge ConnectionTo(Node other)
        {
            return _neighbors.GetValueOrDefault(other);
        }

        /// <summary>
        /// Do not directly use!  Edge handles this.
        /// </summary>
        internal static void AddConnection(Edge edge)
        {
            edge.From._neighbors.Add(edge.To, edge);
            edge.From.MarkDirty();
            edge.To._neighbors.Add(edge.From, edge);
            edge.To.MarkDirty();
        }

        /// <summary>
        /// Do not directly use!  Edge handles this.
        /// </summary>
        internal static void RemoveConnection(Edge edge)
        {
            edge.From._neighbors.Remove(edge.To);
            edge.To._neighbors.Remove(edge.From);

            if (edge.From._neighbors.Count > 0)
                edge.From.MarkDirty();
            else
                edge.From.Close();
            if (edge.To._neighbors.Count > 0)
                edge.To.MarkDirty();
            else
                edge.To.Close();
        }

        /// <summary>
        /// Determines which node to connect to
        /// </summary>
        /// <param name="nPos">incoming node position</param>
        /// <returns>outgoing node</returns>
        public Node Opposition(Vector3D nPos)
        {
            var nd = Vector3D.Normalize(nPos - Position);
            Node best = null;
            double bestDot = 2;
            foreach (var op in _neighbors.Keys)
            {
                if (Vector3D.DistanceSquared(op.Position, nPos) <= RailConstants.NodeMergeDistance * RailConstants.NodeMergeDistance)
                    continue;
                var od = Vector3D.Normalize(op.Position - Position);
                var dot = nd.Dot(od);
                if (dot >= bestDot)
                    continue;
                bestDot = dot;
                best = op;
            }

            return best;
        }

        /// <summary>
        /// Determines which node to connect to
        /// </summary>
        /// <param name="n">incoming node</param>
        /// <returns>outgoing node</returns>
        public Node Opposition(Node n)
        {
            return Opposition(n.Position);
        }

        private bool _dirty;

        public void MarkDirty()
        {
            if (!InScene)
                MyLog.Default.Warning($"Node not in scene marked dirty");
            lock (this)
            {
                if (_dirty)
                    return;
                MoveProxy();
                Graph.NodesForUpdate.Enqueue(this);
                _dirty = true;
            }
        }

        private void MoveProxy()
        {
            var bb = BoundingBoxD.CreatePoint(Position).Inflate(0.1f);
            if (_proxyId >= 0)
                Graph.Nodes.MoveProxy(_proxyId, ref bb, Vector3D.Zero);
            else
                _proxyId = Graph.Nodes.AddProxy(ref bb, this, 0);
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
                var norm = Vector3D.Cross(UpBias, Tangent);
                Up = Vector3D.Normalize(Vector3D.Cross(Tangent, norm));
            }

            Graph.RaiseNodeMoved(this);

            // Two levels b/c tracks use data from 3 nodes.
            foreach (var a in _neighbors.Keys)
            foreach (var b in a._neighbors.Values)
                b.MarkDirty();
        }

        private Vector3D CalculateTangent()
        {
            if (TangentPins > 0)
                return Tangent;
            if (_neighbors.Count == 0)
            {
                var tmp = Vector3D.Cross(UpBias, UpBias.Shifted());
                tmp.Normalize();
                return tmp;
            }

            // ReSharper disable once InvertIf
            if (_neighbors.Count > 1)
            {
                var tan = Vector3D.Zero;
                foreach (var k in _neighbors.Keys)
                {
                    var op = Opposition(k);

                    var a = Vector3D.Normalize(k.Position - Position);
                    var b = Vector3D.Normalize(op.Position - Position);
                    var tanVec = (a - b).SafeNormalized();
                    if (tan.Dot(tanVec) < 0)
                        tan -= tanVec;
                    else
                        tan += tanVec;
                }

                var len = tan.Length();
                if (len > 1e-3f)
                    return tan / len;
            }

            return Vector3D.Normalize(_neighbors.First().Key.Position - Position);
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
                MyLog.Default.Warning($"Closed a node when there were still neighbors");
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