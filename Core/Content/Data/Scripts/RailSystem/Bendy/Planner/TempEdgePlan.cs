using System;
using System.Collections;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.RailSystem.Util;
using Equinox76561198048419394.RailSystem.Util.Curve;
using Sandbox.Game.Entities;
using VRage.Collections;
using VRage.Collections.Graph;
using VRage.Entities.Gravity;
using VRage.Library.Collections;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy.Planner
{
    public sealed class TempEdgePlan
    {
        private readonly BendyLayer _backing;
        private readonly BendyComponentDefinition _placed;
        private readonly MyFreeList<NodeData> _nodes = new MyFreeList<NodeData>();
        private readonly AlGraph<NodeId> _graph = new AlGraph<NodeId>();
        private readonly MyFreeList<EdgeData> _edges = new MyFreeList<EdgeData>();
        private readonly Dictionary<GraphEdge<NodeId>, EdgeId> _edgeIndex = new Dictionary<GraphEdge<NodeId>, EdgeId>(GraphEdge<NodeId>.Comparer);

        public TempEdgePlan(BendyLayer backing, BendyComponentDefinition placed)
        {
            _backing = backing;
            _placed = placed;
        }


        public struct NodeData
        {
            public Vector3D DerivedPosition;
            public Vector3 Up;
            public Vector3 DerivedTangent;
            public Node Existing;

            // User Inputs
            public Vector3D RawPosition;
            public float Smoothness;
            public float? PinnedGrade;
            public float? PinnedDirection;
            public float VerticalShift;

            public MatrixD Matrix => MatrixD.CreateWorld(DerivedPosition, DerivedTangent, Up);

            public bool Dirty;
        }

        public struct EdgeData
        {
            public NodeId Left, Right;

            public bool Dirty;
            public BoundingBoxD Bounds;
            public List<IntermediateCurve> IntermediateCurves;
        }

        private void CreateNodeData(ref NodeData newNode, Vector3D pos)
        {
            newNode.RawPosition = pos;
            newNode.Existing = _backing.GetNode(pos);
            newNode.DerivedPosition = newNode.Existing?.Position ?? pos;
            newNode.DerivedTangent = newNode.Existing?.Tangent ?? Vector3.Zero;
            newNode.Dirty = true;
            newNode.Smoothness = CubicCurve.DefaultSmoothness;

            if (newNode.Existing == null)
            {
                var up = Vector3.Normalize(-MyGravityProviderSystem.CalculateNaturalGravityInPoint(pos));
                if (!up.IsValid() || up.LengthSquared() < 1e-3f)
                    up = Vector3.Up;
                newNode.Up = up;
            }
            else
                newNode.Up = newNode.Existing.Up;
        }

        public FreeListReader<NodeData> Nodes => _nodes;
        public FreeListReader<EdgeData> Edges => _edges;

        public ref NodeData GetNode(NodeId node) => ref _nodes.GetRef(node);

        public ref EdgeData GetEdge(EdgeId edge) => ref _edges.GetRef(edge);

        public EdgesReader GetEdges(NodeId node) => new EdgesReader(this, node);

        public bool TryGetNearestNode(Vector3D pos, out NodeId nodeId, float maxDistanceSquared = RailConstants.NodeMergeDistanceSq)
        {
            nodeId = default;
            double bestNodeDistSq = maxDistanceSquared;
            foreach (var id in _nodes.Ids())
            {
                ref var node = ref _nodes.GetRef(id);
                var distSquared = Vector3D.DistanceSquared(node.RawPosition, pos);
                if (distSquared >= bestNodeDistSq)
                    continue;
                nodeId = id;
                bestNodeDistSq = distSquared;
            }

            return bestNodeDistSq < maxDistanceSquared;
        }

        public bool TryGetNearestEdge(Vector3D pos, out EdgeId edgeId,
            out ICurve curve, out float curveTime,
            float maxDistanceSquared = RailConstants.NodeMergeDistanceSq, float minSeparation = 1f)
        {
            edgeId = default;
            curve = null;
            curveTime = default;
            double bestDistanceSq = maxDistanceSquared;
            double secondBestDistanceSq = maxDistanceSquared;
            foreach (var id in _edges.Ids())
            {
                ref var edge = ref _edges.GetRef(id);
                if (edge.Bounds.DistanceSquared(pos) > bestDistanceSq)
                    continue;
                foreach (var intermediate in edge.IntermediateCurves)
                {
                    var t0 = 0f;
                    var t1 = 1f;
                    CurveExtensions.NearestPoint(intermediate.Curve, pos, 10, ref t0, ref t1);
                    var t = (t0 + t1) / 2;
                    var curvePos = intermediate.Curve.Sample(t);
                    var distSq = Vector3D.DistanceSquared(pos, curvePos);
                    if (distSq > bestDistanceSq)
                    {
                        if (distSq < secondBestDistanceSq)
                            secondBestDistanceSq = distSq;
                        continue;
                    }

                    edgeId = id;
                    curve = intermediate.Curve;
                    curveTime = t;
                    secondBestDistanceSq = bestDistanceSq;
                    bestDistanceSq = distSq;
                }
            }

            return bestDistanceSq < maxDistanceSquared && Math.Sqrt(secondBestDistanceSq) - Math.Sqrt(bestDistanceSq) < minSeparation;
        }

        public NodeId GetOrCreateNode(Vector3D pos, out bool created)
        {
            if (TryGetNearestNode(pos, out var nearId))
            {
                created = false;
                return nearId;
            }

            var newId = _nodes.Allocate();
            ref var newNode = ref _nodes.GetRef(newId);
            CreateNodeData(ref newNode, pos);
            _graph.AddVertex(newId);
            created = true;
            return new NodeId(newId);
        }

        public EdgeId GetOrCreateEdge(NodeId left, NodeId right, out bool created)
        {
            var edgeKey = new GraphEdge<NodeId>(left, right);
            if (_edgeIndex.TryGetValue(edgeKey, out var edgeId))
            {
                created = false;
                return edgeId;
            }

            ref var leftNode = ref _nodes.GetRef(left);
            ref var rightNode = ref _nodes.GetRef(right);
            var newId = _edges.Allocate();
            _graph.AddEdge(left, right);
            _edgeIndex.Add(edgeKey, newId);
            ref var newEdge = ref _edges.GetRef(newId);
            newEdge.Left = left;
            newEdge.Right = right;
            newEdge.IntermediateCurves = PoolManager.Get<List<IntermediateCurve>>();

            newEdge.Dirty = true;
            leftNode.Dirty = true;
            rightNode.Dirty = true;

            created = true;
            return new EdgeId(newId);
        }

        public void RemoveEdge(EdgeId edgeId)
        {
            ref var edge = ref _edges.GetRef(edgeId.RawId);
            _graph.RemoveEdge(edge.Left, edge.Right);
            _edgeIndex.Remove(new GraphEdge<NodeId>(edge.Left, edge.Right));
            PoolManager.Return(ref edge.IntermediateCurves);
            _edges.Free(edgeId.RawId);
        }

        public void RemoveNode(NodeId node)
        {
            foreach (var neighbor in _graph.GetAdjacentVertices(node))
            {
                var edgeKey = new GraphEdge<NodeId>(node, neighbor);
                if (_edgeIndex.TryGetValue(edgeKey, out var edgeId))
                {
                    ref var edge = ref _edges.GetRef(edgeId.RawId);
                    PoolManager.Return(ref edge.IntermediateCurves);
                    _edgeIndex.Remove(edgeKey);
                    _edges.Free(edgeId.RawId);
                }
            }

            _graph.RemoveVertex(node);
            _nodes.Free(node.RawId);
        }

        public void PinNode(NodeId id)
        {
            ref var node = ref GetNode(id);
            if (node.PinnedDirection.HasValue && node.PinnedGrade.HasValue)
                return;
            var surfaceMatrix = DirectionAndGrade.ComputeSurfaceMatrix(node.DerivedPosition);
            DirectionAndGrade.DecomposeTangent(surfaceMatrix, node.DerivedTangent, out var direction, out var grade);
            node.PinnedDirection = direction;
            node.PinnedGrade = grade;
        }

        public void Update()
        {
            for (var id = 0; id < _nodes.UsedLength; id++)
                if (_nodes.IsValidKey(id))
                {
                    ref var node = ref _nodes.GetRef(id);
                    if (!node.Dirty)
                        continue;
                    var newPosition = CalculatePosition(in node);
                    var newTangent = CalculateTangent(id, in node);
                    if (!newTangent.Equals(node.DerivedTangent, 1e-4f) || !newPosition.Equals(node.DerivedPosition, 1e-4f))
                    {
                        node.DerivedPosition = newPosition;
                        node.DerivedTangent = newTangent;

                        // Mark neighbor edges dirty
                        foreach (var neighbor in _graph.GetAdjacentVertices(id))
                            if (_edgeIndex.TryGetValue(new GraphEdge<NodeId>(id, neighbor), out var edgeId))
                                _edges.GetRef(edgeId).Dirty = true;
                    }
                }

            for (var id = 0; id < _edges.UsedLength; id++)
                if (_edges.IsValidKey(id))
                {
                    ref var edge = ref _edges.GetRef(id);
                    if (!edge.Dirty)
                        continue;
                    ref var from = ref _nodes.GetRef(edge.Left);
                    ref var to = ref _nodes.GetRef(edge.Right);
                    RecomputeIntermediateCurves(ref edge, ref from, ref to);
                    RecomputeEdgeBounds(ref edge);
                }
        }

        private void RecomputeIntermediateCurves(ref EdgeData edge, ref NodeData from, ref NodeData to)
        {
            edge.IntermediateCurves.Clear();

            // Consider division
            var sphericalCurve = EdgePlacerBehavior.PrepareSphericalBez(from.Matrix, to.Matrix,
                from.Smoothness, to.Smoothness);
            var sphericalLength = sphericalCurve.LengthAuto(_placed.Distance.Min / 8);

            var minCount = (int)Math.Ceiling(sphericalLength / _placed.Distance.Max);
            var maxCount = (int)Math.Floor(sphericalLength / _placed.Distance.Min);
            var idealCount = (int)Math.Round(sphericalLength / _placed.PreferredDistance);
            var edgeCount = MathHelper.Clamp(idealCount, minCount, maxCount);

            if (edgeCount == 1)
            {
                edge.IntermediateCurves.Add(ComputeCurve(in from, in to));
                return;
            }

            // Divide it into multiple curves.
            using (PoolManager.Get(out List<NodeData> intermediateNodes))
            {
                intermediateNodes.EnsureCapacity(edgeCount - 1);
                for (var i = 1; i < edgeCount; i++)
                {
                    var t = i / (float)edgeCount;
                    var data = new NodeData();
                    CreateNodeData(ref data, sphericalCurve.Sample(t));
                    data.DerivedTangent = Vector3.Normalize(sphericalCurve.SampleDerivative(t));
                    intermediateNodes.Add(data);
                }

                edge.IntermediateCurves.EnsureCapacity(edgeCount);
                for (var i = 0; i < edgeCount; i++)
                {
                    var subFrom = i == 0 ? from : intermediateNodes[i - 1];
                    var subTo = i < intermediateNodes.Count ? intermediateNodes[i] : to;
                    edge.IntermediateCurves.Add(ComputeCurve(in subFrom, in subTo));
                }
            }
        }

        private static void RecomputeEdgeBounds(ref EdgeData edge)
        {
            edge.Bounds = BoundingBoxD.CreateInvalid();
            foreach (var curve in edge.IntermediateCurves)
                edge.Bounds.Include(curve.Bounds);
        }

        private IntermediateCurve ComputeCurve(in NodeData from, in NodeData to)
        {
            Edge.ComputeCurve(from.DerivedPosition, from.DerivedTangent, from.Up,
                to.DerivedPosition, to.DerivedTangent, to.Up,
                _placed.Edges[0].Mode, out _, out _, out var singleCurve);

            return new IntermediateCurve(singleCurve);
        }

        private static void AccumulateTangent(ref Vector3 state, in Vector3D a, in Vector3D b)
        {
            var t = Vector3.Normalize(a - b);
            if (t.Dot(state) < 0)
                t = -t;
            state += t;
        }

        private Vector3D CalculatePosition(in NodeData node)
        {
            if (node.Existing != null)
                return node.Existing.Position;
            return node.RawPosition + node.Up * node.VerticalShift;
        }

        private Vector3 CalculateTangent(NodeId nodeId, in NodeData node)
        {
            var existing = node.Existing;
            if (existing != null && existing.TangentPins > 0)
                return existing.Tangent;
            var tan = Vector3.Zero;

            if (existing != null)
                foreach (var neighbor in existing.Neighbors)
                    AccumulateTangent(ref tan, in node.DerivedPosition, neighbor.Position);
            foreach (var neighborId in _graph.GetAdjacentVertices(nodeId))
            {
                ref var neighbor = ref _nodes.GetRef(neighborId);
                if (existing == null || !existing.Connections.ContainsKey(neighbor.Existing))
                    AccumulateTangent(ref tan, in node.DerivedPosition, in neighbor.DerivedPosition);
            }

            if (tan.LengthSquared() < 1e-4)
                tan = Vector3.CalculatePerpendicularVector(node.Up);
            var suggestedTangent = Vector3.Normalize(tan);
            if (!node.PinnedDirection.HasValue && !node.PinnedGrade.HasValue)
                return suggestedTangent;
            var surfaceMatrix = DirectionAndGrade.ComputeSurfaceMatrix(node.DerivedPosition);
            DirectionAndGrade.DecomposeTangent(surfaceMatrix, suggestedTangent, out var direction, out var grade);
            if (node.PinnedDirection.HasValue)
            {
                // If the direction is flipping then also flip the suggested grade.
                var directionDiff = Math.Abs(node.PinnedDirection.Value - direction);
                if (directionDiff > MathHelper.Pi)
                    directionDiff = MathHelper.TwoPi - directionDiff;
                if (directionDiff > MathHelper.PiOver2)
                    grade *= -1;
                direction = node.PinnedDirection.Value;
            }

            if (node.PinnedGrade.HasValue)
                grade = node.PinnedGrade.Value * (grade < 0 && !node.PinnedDirection.HasValue ? -1 : 1);
            return DirectionAndGrade.ComposeTangent(surfaceMatrix, direction, grade);
        }

        public readonly struct NodeId : IEquatable<NodeId>
        {
            public readonly int RawId;

            public NodeId(int rawId) => RawId = rawId;

            public bool Equals(NodeId other) => RawId == other.RawId;

            public override bool Equals(object obj) => obj is NodeId other && Equals(other);

            public override int GetHashCode() => RawId;

            public static implicit operator int(NodeId id) => id.RawId;

            public static implicit operator NodeId(int id) => new NodeId(id);
        }

        public readonly struct EdgeId : IEquatable<EdgeId>
        {
            public readonly int RawId;

            public EdgeId(int rawId) => RawId = rawId;

            public bool Equals(EdgeId other) => RawId == other.RawId;

            public override bool Equals(object obj) => obj is EdgeId other && Equals(other);

            public override int GetHashCode() => RawId;

            public static implicit operator int(EdgeId id) => id.RawId;

            public static implicit operator EdgeId(int id) => new EdgeId(id);
        }

        public readonly struct EdgesReader : IReadOnlyCollection<KeyValuePair<NodeId, EdgeId>>
        {
            private readonly TempEdgePlan _owner;
            private readonly NodeId _node;
            private readonly HashSetReader<NodeId> _neighbors;

            internal EdgesReader(TempEdgePlan owner, NodeId node)
            {
                _owner = owner;
                _node = node;
                _neighbors = _owner._graph.GetAdjacentVertices(node);
            }

            public EdgesEnumerator GetEnumerator() => new EdgesEnumerator(_owner, _node, _neighbors.GetEnumerator());

            IEnumerator<KeyValuePair<NodeId, EdgeId>> IEnumerable<KeyValuePair<NodeId, EdgeId>>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public int Count => _neighbors.Count;
        }

        public struct EdgesEnumerator : IEnumerator<KeyValuePair<NodeId, EdgeId>>
        {
            private readonly TempEdgePlan _owner;
            private readonly NodeId _node;
            private HashSet<NodeId>.Enumerator _neighbors;

            internal EdgesEnumerator(TempEdgePlan owner, NodeId node, HashSet<NodeId>.Enumerator neighbors)
            {
                _owner = owner;
                _node = node;
                _neighbors = neighbors;
            }

            public void Dispose() => _neighbors.Dispose();

            public bool MoveNext() => _neighbors.MoveNext();

            public void Reset() => ((IEnumerator)_neighbors).Reset();

            public KeyValuePair<NodeId, EdgeId> Current => new KeyValuePair<NodeId, EdgeId>(
                _neighbors.Current,
                _owner._edgeIndex[new GraphEdge<NodeId>(_node, _neighbors.Current)]);

            object IEnumerator.Current => Current;
        }
    }
}