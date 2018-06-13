using System;
using Equinox76561198048419394.RailSystem.Util;
using Equinox76561198048419394.RailSystem.Util.Curve;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy
{
    public enum CurveMode
    {
        Linear,
        CubicBez
    }
    
    public class Edge
    {
        public readonly BendyLayer Graph;
        public readonly Node From, To;
        public ICurve Curve { get; private set; }
        public readonly CurveMode Mode;
        public readonly BendyComponent Owner;

        internal Edge(BendyComponent owner, Node from, Node to, CurveMode mode)
        {
            Owner = owner;
            Graph = from.Graph;
            From = from;
            To = to;
            Mode = mode;
            EnsureInScene();
        }

        public void MarkDirty()
        {
            lock (this)
            {
                if (_dirty)
                    return;
                _dirty = true;
                MoveProxy();
                Graph.EdgesForUpdate.Enqueue(this);
            }
        }

        private void MoveProxy()
        {
            var bb = BoundingBoxD.CreatePoint(From.Position).Include(To.Position).Inflate(0.1f);
            if (_proxyId >= 0)
                Graph.Edges.MoveProxy(_proxyId, ref bb, Vector3D.Zero);
            else
                _proxyId = Graph.Edges.AddProxy(ref bb, this, 0);
        }

        public MatrixD FromMatrix { get; private set; }
        public MatrixD ToMatrix { get; private set; }

        public void DeferredUpdate()
        {
            lock (this)
            {
                if (!_dirty)
                    return;
                _dirty = false;
            }

            FromMatrix = MatrixD.CreateWorld(From.Position, Tangent(From.Tangent), From.Up);
            ToMatrix = MatrixD.CreateWorld(To.Position, Tangent(To.Tangent), To.Up);

            switch (Mode)
            {
                case CurveMode.Linear:
                    Curve = new LinearCurve(From.Position, To.Position);
                    break;
                case CurveMode.CubicBez:
                    Curve = new CubicCurve(FromMatrix, ToMatrix);
                    break;
                default:
                    throw new Exception($"Unsupported curve mode {Mode}");
            }

            CurveUpdated?.Invoke(this);
        }

        public event Action<Edge> CurveUpdated;

        private bool _dirty;
        private int _proxyId = -1;

        
        public bool InScene { get; private set; }

        public void EnsureInScene()
        {
            if (InScene)
                return;
            Node.AddConnection(this);
            Graph.EdgeList.Add(this);
            From.MarkDirty();
            To.MarkDirty();
            MarkDirty();
        }
        
        public virtual void Close()
        {
            InScene = false;
            Node.RemoveConnection(this);
            if (_proxyId >= 0)
            {
                Graph.Edges.RemoveProxy(_proxyId);
                _proxyId = -1;
            }

            Graph.EdgeList.Remove(this);
        }


        public Vector3D Tangent(Vector3D orig)
        {
            if (orig.Dot(To.Position - From.Position) < 0)
                return -orig;
            return orig;
        }
    }
}