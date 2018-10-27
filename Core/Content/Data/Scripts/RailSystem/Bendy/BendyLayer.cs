using System;
using System.Collections.Generic;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.Game.GameSystems;
using VRage.Components.Entity.Camera;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy
{
    public class BendyLayer
    {
        public readonly string Layer;

        public BendyLayer(string id)
        {
            Layer = id;
        }

        internal readonly MyDynamicAABBTreeD Nodes = new MyDynamicAABBTreeD();
        internal readonly MyDynamicAABBTreeD Edges = new MyDynamicAABBTreeD();
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

        public Node NearestNode(Vector3D v)
        {
            using (var e = Nodes.SortedByDistance(v))
                if (e.MoveNext())
                    return Nodes.GetUserData<Node>(e.Current.Key);
            return null;
        }

        public Node GetNode(Vector3D pos)
        {
            var nearest = NearestNode(pos);
            if (nearest != null && Vector3D.DistanceSquared(nearest.Position, pos) < RailConstants.NodeMergeDistanceSq)
                return nearest;
            return null;
        }

        public Node GetOrCreateNode(Vector3D pos, Vector3D? up = null)
        {
            var nearest = NearestNode(pos);
            if (nearest != null && Vector3D.DistanceSquared(nearest.Position, pos) < RailConstants.NodeMergeDistanceSq)
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

        public Edge CreateEdge(BendyComponent owner, Node from, Node to, CurveMode mode)
        {
            return new Edge(owner, from, to, mode);
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

            if (RailConstants.Debug.DrawGraphEdges || RailConstants.Debug.DrawGraphNodes)
                DebugDraw();
        }


        private const float _edgeWidth = 0.05f;
        private const float _nodeWidth = 0.01f;
        private static Vector4 _edgeColor = new Vector4(1, 0, 1, 0.1f);
        private static readonly Vector4 _edgeColorBad = new Vector4(1, 0, 0, 0.1f);
        private static readonly Vector4 _nodeColor = new Vector4(0, 0, 1, 0.1f);
        private static readonly MyStringId _squareMaterial = MyStringId.GetOrCompute("Square");
        private const float _nodeMarkerSize = 1;
        private const float _edgeMarkerVertOffset = 0.325f;

        private void DebugDraw()
        {
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
                Edges.OverlapAllFrustum(ref frust, (Edge edge, bool intersects) =>
                {
                    var bezCurve = edge.Curve;
                    var first = bezCurve.Sample(0);
                    var last = bezCurve.Sample(1);
                    var center = (first + last) / 2;
                    var factor = Math.Sqrt(Vector3D.DistanceSquared(first, last) / (1 + Vector3D.DistanceSquared(cam.GetPosition(), center)));
                    var count = MathHelper.Clamp(factor * 100, 1, 25);
                    var lastPos = default(Vector3D);
                    for (var t = 0; t <= count; t++)
                    {
                        var time = t / (float) count;
                        var pos = bezCurve.Sample(time);
                        var pact = pos + Vector3D.Lerp(edge.From.Up, edge.To.Up, time) * _edgeMarkerVertOffset;
                        if (t > 0)
                            MySimpleObjectDraw.DrawLine(lastPos, pact, _squareMaterial, ref _edgeColor, _edgeWidth);
                        lastPos = pact;
                    }
                });
        }
    }
}