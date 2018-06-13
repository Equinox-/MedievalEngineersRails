using System;
using System.Collections.Generic;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.Game.GameSystems;
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
        }
    }
}