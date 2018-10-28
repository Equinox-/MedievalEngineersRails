using System;
using Equinox76561198048419394.RailSystem.Util;
using Equinox76561198048419394.RailSystem.Util.Curve;
using VRage.Library.Logging;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy
{
    public enum CurveMode
    {
        Linear,
        QuadraticBez,
        CubicBez
    }

    public class Edge
    {
        public readonly BendyLayer Graph;
        public readonly Node From, To;
        public ICurve Curve { get; private set; }
        public readonly CurveMode Mode;
        public readonly BendyComponent Owner;

        private readonly Vector3? _ctl0, _ctl1;

        internal Edge(BendyComponent owner, Node from, Node to, CurveMode mode, Vector3? ctl0 = null, Vector3? ctl1 = null)
        {
            Owner = owner;
            Graph = from.Graph;
            From = from;
            To = to;
            Mode = mode;
            _ctl0 = ctl0;
            _ctl1 = ctl1;

            EnsureInScene();
        }

        public void MarkDirty()
        {
            if (!InScene)
                MyLog.Default.Warning($"Edge not in scene marked dirty");
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


            var ext = Math.Max((FromMatrix.Translation - ToMatrix.Translation).Length() / 3, 1f);
            var d1 = default(Vector3D);
            var d2 = default(Vector3D);
            if (Mode != CurveMode.Linear)
            {
                if (_ctl0.HasValue)
                    d1 = Vector3D.Transform(_ctl0.Value, FromMatrix);
                else
                    d1 = FromMatrix.Translation + (FromMatrix.Forward * ext);
                if (_ctl1.HasValue)
                    d2 = Vector3D.Transform(_ctl1.Value, ToMatrix);
                else
                    d2 = ToMatrix.Translation - (ToMatrix.Forward * ext);
            }


            switch (Mode)
            {
                case CurveMode.Linear:
                    Curve = new LinearCurve(From.Position, To.Position);
                    break;
                case CurveMode.QuadraticBez:
                    Curve = new QuadraticCurve(FromMatrix.Translation, (d1 + d2) / 2, ToMatrix.Translation);
                    break;
                case CurveMode.CubicBez:
                    Curve = new CubicCurve(FromMatrix.Translation, d1, d2, ToMatrix.Translation);
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
            InScene = true;
            From.EnsureInScene();
            To.EnsureInScene();
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