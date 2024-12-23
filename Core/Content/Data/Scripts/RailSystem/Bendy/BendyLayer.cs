﻿using System;
using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components.Entity.Camera;
using VRage.Entities.Gravity;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Library.Collections;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy
{
    public class BendyLayer
    {
        public readonly string Layer;
        public readonly BendyController Owner;

        public BendyLayer(BendyController owner, string id)
        {
            Owner = owner;
            Layer = id;
        }

        internal readonly MyDynamicAABBTreeD Nodes = new MyDynamicAABBTreeD(Vector3D.Zero);
        internal readonly MyDynamicAABBTreeD Edges = new MyDynamicAABBTreeD(Vector3D.Zero);
        internal readonly MyDynamicAABBTreeD DynamicEdges = new MyDynamicAABBTreeD(new Vector3D(5));
        internal readonly Queue<Node> NodesForUpdate = new Queue<Node>();
        internal readonly Queue<Edge> EdgesForUpdate = new Queue<Edge>();
        internal readonly List<Node> NodeList = new List<Node>();
        internal readonly List<Edge> EdgeList = new List<Edge>();

        public event Action<Node> NodeCreated;
        public event Action<Node> NodeMoved;
        public event Action<Node> NodeRemoved;

        internal void RaiseNodeCreated(Node n)
        {
            NodeCreated?.Invoke(n);
        }

        internal void RaiseNodeRemoved(Node n)
        {
            NodeRemoved?.Invoke(n);
        }

        internal void RaiseNodeMoved(Node n)
        {
            NodeMoved?.Invoke(n);
        }

        public Edge GetEdge(Node from, Node to)
        {
            return from.ConnectionTo(to);
        }

        public Node NearestNode(Vector3D v, double maxDistanceSq = double.PositiveInfinity)
        {
            using (var e = Nodes.SortedByDistance(v))
                if (e.MoveNext())
                {
                    if (e.Current.DistanceSquared >= maxDistanceSq)
                        return null;
                    return (Node)e.Current.UserData;
                }

            return null;
        }

        public Node GetNode(Vector3D pos, bool roughMatch = false)
        {
            var distSq = roughMatch ? RailConstants.NodeRoughDistanceSq : RailConstants.NodeMergeDistanceSq;
            var nearest = NearestNode(pos, distSq);
            if (nearest != null && Vector3D.DistanceSquared(nearest.Position, pos) < distSq)
                return nearest;
            return null;
        }

        public Node GetOrCreateNode(Vector3D pos, Vector3D? up = null)
        {
            var nearest = GetNode(pos);
            if (nearest != null)
                return nearest;

            // ReSharper disable once InvertIf
            if (!up.HasValue)
            {
                up = Vector3D.Normalize(-MyGravityProviderSystem.CalculateNaturalGravityInPoint(pos));
                if (!up.Value.IsValid() || up.Value.LengthSquared() < 1e-3f)
                    up = Vector3D.Up;
            }

            return new Node(this, pos, up.Value);
        }

        public Edge CreateEdge(BendyComponent owner, Node from, Node to, CurveMode mode, Vector3? ctl0 = null, Vector3? ctl1 = null)
        {
            return new Edge(owner, from, to, mode, ctl0, ctl1);
        }

        internal void UpdateAfterSimulation()
        {
            while (NodesForUpdate.Count > 0)
            {
                var n = NodesForUpdate.Dequeue();
                n.DeferredUpdate();
            }

            while (EdgesForUpdate.Count > 0)
            {
                var n = EdgesForUpdate.Dequeue();
                n.DeferredUpdate();
            }

            if (!((IMyUtilities)MyAPIUtilities.Static).IsDedicated && (RailConstants.Debug.DrawGraphEdges || RailConstants.Debug.DrawGraphNodes))
                DebugDraw();
        }


        private const float _nodeWidth = 0.01f;
        private static Vector4 _edgeColor = new Vector4(1, 0, 1, 0.1f);
        private static readonly Vector4 _nodeColor = new Vector4(0, 0, 1, 0.1f);
        private static readonly MyStringId _squareMaterial = MyStringId.GetOrCompute("Square");
        private const float _nodeMarkerSize = 1;

        private void DebugDraw()
        {
            if (((IMyUtilities)MyAPIUtilities.Static).IsDedicated)
                return;
            var cam = MyCameraComponent.ActiveCamera;
            if (cam == null)
                return;
            var proj = cam.GetProjectionSetup();
            proj.FarPlane = 100;
            var frust = new BoundingFrustumD(cam.GetViewMatrix() * proj.ProjectionMatrix);
            if (RailConstants.Debug.DrawGraphNodes)
                Nodes.OverlapAllFrustum(ref frust, (Node node, bool intersects) =>
                {
                    var color = _nodeColor;
                    var p1 = node.Position;
                    var p2 = node.Position + _nodeMarkerSize * node.Up;
                    MySimpleObjectDraw.DrawLine(p1, p2, _squareMaterial, ref color, _nodeWidth);
                });

            if (RailConstants.Debug.DrawGraphEdges)
                Edges.OverlapAllFrustum(ref frust, (Edge edge, bool intersects) => { edge.Draw(0, 1, _edgeColor); });
        }

        public bool TryFindPath(
            IEnumerable<Node> starts,
            IEnumerable<Node> ends,
            List<Node> pathNodes,
            List<Edge> pathEdges,
            int nodeLimit = int.MaxValue,
            float distanceLimit = float.PositiveInfinity)
        {
            using (PoolManager.Get(out Dictionary<Node, Edge> visitedToReachedUsing))
            using (PoolManager.Get(out List<Node> endsCopy))
            {
                var endHint = Vector3D.Zero;
                foreach (var end in ends)
                {
                    endsCopy.Add(end);
                    endHint += end.Position;
                }

                endHint /= endsCopy.Count;

                var heap = new MyBinaryHeap<float, PathNode>();

                void MaybeAdd(PathNode? prev, Node node, Edge from)
                {
                    if (visitedToReachedUsing.ContainsKey(node))
                        return;
                    var distFromPrev = prev != null ? (float)Vector3D.Distance(prev.Value.Pt.Position, node.Position) : 0;
                    var remainingDist = (float)Vector3D.Distance(node.Position, endHint);
                    var nextDist = (prev?.AccumulatedDistance ?? 0) + distFromPrev;
                    var nextNodes = (prev?.AccumulatedNodes ?? 0) + 1;
                    var nextScore = nextDist + remainingDist;
                    if (nextNodes > nodeLimit || nextScore > distanceLimit)
                        return;
                    visitedToReachedUsing.Add(node, from);
                    heap.Insert(new PathNode(node, nextNodes, nextDist), nextScore);
                }

                foreach (var start in starts)
                    MaybeAdd(null, start, null);
                while (true)
                {
                    foreach (var end in endsCopy)
                        if (visitedToReachedUsing.TryGetValue(end, out var via))
                        {
                            // Build up the path
                            pathNodes?.Add(end);
                            var prevNode = end;
                            var prevEdge = via;
                            while (prevEdge != null)
                            {
                                prevNode = prevEdge.Opposition(prevNode);
                                if (prevNode == null)
                                    break;
                                pathEdges?.Add(prevEdge);
                                pathNodes?.Add(prevNode);
                                prevEdge = visitedToReachedUsing.GetValueOrDefault(prevNode);
                            }

                            pathNodes?.Reverse();
                            pathEdges?.Reverse();
                            return true;
                        }

                    if (heap.Count == 0)
                        return false;
                    var min = heap.RemoveMin();
                    foreach (var neighbor in min.Pt.Connections)
                        MaybeAdd(min, neighbor.Key, neighbor.Value);
                }
            }
        }

        private readonly struct PathNode
        {
            public readonly Node Pt;
            public readonly int AccumulatedNodes;
            public readonly float AccumulatedDistance;

            public PathNode(Node pt, int accumulatedNodes, float accumulatedDistance)
            {
                Pt = pt;
                AccumulatedNodes = accumulatedNodes;
                AccumulatedDistance = accumulatedDistance;
            }
        }
    }
}