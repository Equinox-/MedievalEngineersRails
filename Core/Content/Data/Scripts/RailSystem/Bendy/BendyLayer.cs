using System;
using System.Collections.Generic;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.ModAPI;
using VRage.Components.Entity.Camera;
using VRage.Entities.Gravity;
using VRage.Game;
using VRage.Game.ModAPI;
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
                    return (Node) e.Current.UserData;
            return null;
        }

        public Node GetNode(Vector3D pos)
        {
            var nearest = NearestNode(pos);
            if (nearest != null && Vector3D.DistanceSquared(nearest.Position, pos) < RailConstants.NodeMergeDistanceSq)
                return nearest;
            return null;
        }

        public Node GetOrCreateNode(Vector3D pos, Vector3D? up = null, bool desirePin = false)
        {
            var nearest = NearestNode(pos);
            if (nearest != null && Vector3D.DistanceSquared(nearest.Position, pos) < RailConstants.NodeMergeDistanceSq)
            {
                if (!desirePin || Vector3D.DistanceSquared(nearest.Position, pos) < .05f * .05f)
                    return nearest;
            }

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

            if (!((IMyUtilities) MyAPIUtilities.Static).IsDedicated && (RailConstants.Debug.DrawGraphEdges || RailConstants.Debug.DrawGraphNodes))
                DebugDraw();
        }


        private const float _nodeWidth = 0.01f;
        private static Vector4 _edgeColor = new Vector4(1, 0, 1, 0.1f);
        private static readonly Vector4 _nodeColor = new Vector4(0, 0, 1, 0.1f);
        private static readonly MyStringId _squareMaterial = MyStringId.GetOrCompute("Square");
        private const float _nodeMarkerSize = 1;

        private void DebugDraw()
        {
            if (((IMyUtilities) MyAPIUtilities.Static).IsDedicated)
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
    }
}