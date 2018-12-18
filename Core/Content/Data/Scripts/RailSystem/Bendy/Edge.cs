using System;
using Equinox76561198048419394.RailSystem.Util;
using Equinox76561198048419394.RailSystem.Util.Curve;
using VRage.Components.Entity.Camera;
using VRage.Game;
using VRage.Library.Logging;
using VRage.Utils;
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

            FromMatrix = MatrixD.CreateWorld(From.Position, CorrectTangent(From.Tangent), From.Up);
            ToMatrix = MatrixD.CreateWorld(To.Position, CorrectTangent(To.Tangent), To.Up);


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

        public void Close()
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

        /// <summary>
        /// Gets the other endpoint of this curve, given one endpoint
        /// </summary>
        /// <param name="n"></param>
        /// <returns></returns>
        public Node Opposition(Node n)
        {
            return From == n ? To : From;
        }

        public Vector3 EdgeTangent => Vector3.Normalize(To.Position - From.Position);

        /// <summary>
        /// Corrects a tangent vector so that it points in the general direction of positive T factor
        /// </summary>
        /// <param name="orig"></param>
        /// <returns></returns>
        public Vector3 CorrectTangent(Vector3 orig)
        {
            if (orig.Dot((Vector3) (To.Position - From.Position)) < 0)
                return -orig;
            return orig;
        }

        private const float EdgeWidth = 0.05f;
        private static readonly MyStringId SquareMaterial = MyStringId.GetOrCompute("Square");
        private const float EdgeMarkerVertOffset = 0.325f;

        public void DebugDraw(float tStart, float tEnd, Vector4 color, int verticalGroup = 0)
        {
            var cam2 = MyCameraComponent.ActiveCamera;
            if (cam2 == null || Curve == null)
                return;
            var bezCurve = Curve;
            var first = bezCurve.Sample(tStart);
            var last = bezCurve.Sample(tEnd);
            var center = (first + last) / 2;
            var factor = Math.Sqrt(Vector3D.DistanceSquared(first, last) / (1 + Vector3D.DistanceSquared(cam2.GetPosition(), center)));
            var count = (int) MathHelper.Clamp(factor * 5, 1, 25);
            if (bezCurve is LinearCurve)
                count = 1;
            var lastPos = default(Vector3D);
            for (var t = 0; t <= count; t++)
            {
                var time = MathHelper.Lerp(tStart, tEnd, (t / (float) count));
                var pos = bezCurve.Sample(time);
                var pact = pos + Vector3D.Lerp(From.Up, To.Up, time) * (EdgeMarkerVertOffset + verticalGroup * EdgeWidth * 8);
                if (t > 0)
                {
                    MySimpleObjectDraw.DrawLine(lastPos, pact, SquareMaterial, ref color, EdgeWidth);
                }

                lastPos = pact;
            }
        }
    }
}