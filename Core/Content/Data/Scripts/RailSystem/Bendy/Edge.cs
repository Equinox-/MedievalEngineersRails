using System;
using Equinox76561198048419394.RailSystem.Util.Curve;
using VRage.Components;
using VRage.Components.Physics;
using VRage.Logging;
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

    public interface IEdge
    {
        ICurve Curve { get; }
        MatrixD Transform { get; }
        MatrixD TransformInv { get; }
        
        Vector3 FromUp { get; }
        Vector3 ToUp { get; }
    }

    public class Edge : IEdge
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
                GetLogger().Warning($"Edge not in scene marked dirty");
            lock (this)
            {
                if (_dirty)
                    return;
                _dirty = true;
                MoveProxy();
                Graph.EdgesForUpdate.Enqueue(this);
            }
        }

        private NamedLogger GetLogger()
        {
            return Graph.Owner.GetLogger().WithContext(this);
        }

        private void MoveProxy()
        {
            var bb = new BoundingBoxD(Vector3D.Min(From.Position, To.Position) - .1f, Vector3D.Max(From.Position, To.Position) + .1f);
            if (_proxyId >= 0)
                Graph.Edges.MoveProxy(_proxyId, in bb, Vector3D.Zero);
            else
                _proxyId = Graph.Edges.AddProxy(in bb, this, 0);
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
            ComputeCurve(From.Position, From.Tangent, From.Up,
                        To.Position, To.Tangent, To.Up,
                        Mode,
                        out var fromMatrix,
                        out var toMatrix,
                        out var curve,
                        _ctl0,
                        _ctl1);
            Curve = curve;
            FromMatrix = fromMatrix;
            ToMatrix = toMatrix;
            CurveUpdated?.Invoke(this);
        }

        public static void ComputeCurve(Vector3D fromPos, Vector3 fromTangent, Vector3 fromUp,
                                        Vector3D toPos, Vector3 toTangent, Vector3 toUp,
                                        CurveMode mode,
                                        out MatrixD fromMatrix,
                                        out MatrixD toMatrix,
                                        out ICurve curve,
                                        Vector3D? ctl0 = null,
                                        Vector3D? ctl1 = null) {
            Vector3 CorrectTangent(Vector3 orig)
            {
                if (orig.Dot((Vector3) (toPos - fromPos)) < 0)
                    return -orig;
                return orig;
            }
            fromMatrix = MatrixD.CreateWorld(fromPos, CorrectTangent(fromTangent), fromUp);
            toMatrix = MatrixD.CreateWorld(toPos, CorrectTangent(toTangent), toUp);


            var ext = Math.Max((fromMatrix.Translation - toMatrix.Translation).Length() / 3, 1f);
            var d1 = default(Vector3D);
            var d2 = default(Vector3D);
            if (mode != CurveMode.Linear)
            {
                if (ctl0.HasValue)
                    d1 = Vector3D.Transform(ctl0.Value, fromMatrix);
                else
                    d1 = fromMatrix.Translation + (fromMatrix.Forward * ext);
                if (ctl1.HasValue)
                    d2 = Vector3D.Transform(ctl1.Value, toMatrix);
                else
                    d2 = toMatrix.Translation - (toMatrix.Forward * ext);
            }


            switch (mode)
            {
                case CurveMode.Linear:
                    curve = new LinearCurve(fromPos, toPos);
                    break;
                case CurveMode.QuadraticBez:
                    curve = new QuadraticCurve(fromMatrix.Translation, (d1 + d2) / 2, toMatrix.Translation);
                    break;
                case CurveMode.CubicBez:
                    curve = new CubicCurve(fromMatrix.Translation, d1, d2, toMatrix.Translation);
                    break;
                default:
                    throw new Exception($"Unsupported curve mode {mode}");
            }
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
            if (From == n)
                return To;
            return To == n ? From : null;
        }

        public Vector3 EdgeTangent => Vector3.Normalize(To.Position - From.Position);

        private const float EdgeWidth = 0.05f;
        private static readonly MyStringId SquareMaterial = MyStringId.GetOrCompute("Square");
        private const float EdgeMarkerVertOffset = 0.325f;

        public void Draw(float tStart, float tEnd, Vector4 color, int verticalGroup = 0)
        {
            if (Curve == null)
                return;
            var upOffset = EdgeMarkerVertOffset + verticalGroup * EdgeWidth * 8;
            Curve.Draw(color, tStart, tEnd, edgeWidth: EdgeWidth, upZero: From.Up * upOffset, upOne: To.Up * upOffset);
        }

        public Vector3 FromUp => From.Up;
        public Vector3 ToUp => To.Up;
        public MatrixD Transform => MatrixD.Identity;
        public MatrixD TransformInv => MatrixD.Identity;
    }
}