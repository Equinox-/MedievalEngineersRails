using System;
using Equinox76561198048419394.RailSystem.Util;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy
{
    public class Edge
    {
        public readonly BendyLayer Graph;
        public readonly Node From, To;
        public Bezier.IBezier Curve { get; private set; }
        public readonly CurveMode Mode;

        internal Edge(Node from, Node to, CurveMode mode)
        {
            Graph = from.Graph;
            From = from;
            To = to;
            Mode = mode;
            Node.AddConnection(this);
            Graph.EdgeList.Add(this);
            From.MarkDirty();
            To.MarkDirty();
            MarkDirty();
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
                    Curve = new Bezier.LinearCurve(From.Position, To.Position);
                    break;
                case CurveMode.CubicBez:
                    Curve = new Bezier.CubicBezier(FromMatrix, ToMatrix);
                    break;
                default:
                    throw new Exception($"Unsupported curve mode {Mode}");
            }

            CurveUpdated?.Invoke();
        }

        public event Action CurveUpdated;

        private bool _dirty;
        private int _proxyId = -1;

        public virtual void Close()
        {
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