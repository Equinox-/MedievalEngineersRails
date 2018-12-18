using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Components.Entity.Camera;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy.Shape
{
    public abstract class BendyShapeComponent : MyEntityComponent
    {
        private BendyComponent _bendyDynamicComponent;
        public BendyShapeComponentDefinition Definition { get; private set; }

        private readonly List<OrientedBoundingBox> _boxes = new List<OrientedBoundingBox>();
        protected IReadOnlyList<OrientedBoundingBox> Boxes => _boxes;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            Definition = (BendyShapeComponentDefinition) definition;
        }

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            _bendyDynamicComponent = Entity.Components.Get<BendyComponent>();
            _bendyDynamicComponent.EdgeSetupChanged += EdgeSetupChanged;
        }

        private static readonly MyStringId _debugMaterialId = MyStringId.GetOrCompute("Square");

        public void DebugDraw()
        {
            if (((IMyUtilities) MyAPIUtilities.Static).IsDedicated)
                return;
            foreach (var k in _boxes)
            {
                var box = new BoundingBoxD(k.Center - k.HalfExtent, k.Center + k.HalfExtent);
                var m = MatrixD.CreateFromQuaternion(k.Orientation) * Entity.WorldMatrix;
                var queryBox = box;
                queryBox = queryBox.TransformFast(m);
                if (Vector3D.DistanceSquared(queryBox.Center, MyCameraComponent.ActiveCamera.GetPosition()) >
                    100 * 100 ||
                    !MyCameraComponent.ActiveCamera.GetCameraFrustum().Intersects(queryBox))
                    continue;
                var c = new Color(1, 0, 0, 0.25f);
                MySimpleObjectDraw.DrawTransparentBox(ref m, ref box, ref c, MySimpleObjectRasterizer.Solid, 1, .01f,
                    _debugMaterialId, _debugMaterialId);
                var cWif = Color.White;
                MySimpleObjectDraw.DrawTransparentBox(ref m, ref box, ref cWif, MySimpleObjectRasterizer.Wireframe, 1,
                    .01f, _debugMaterialId, _debugMaterialId);
            }
        }


        public override void OnBeforeRemovedFromContainer()
        {
            base.OnBeforeRemovedFromContainer();
            _bendyDynamicComponent.EdgeSetupChanged -= EdgeSetupChanged;
            _bendyDynamicComponent = null;
        }

        private void EdgeSetupChanged(BendyComponent c)
        {
            ScheduleCalcShape();
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            ScheduleCalcShape();
            if (RailConstants.Debug.DrawBendyPhysics && !((IMyUtilities) MyAPIUtilities.Static).IsDedicated)
                AddFixedUpdate(DebugDraw);
        }

        public override void OnRemovedFromScene()
        {
            if (RailConstants.Debug.DrawBendyPhysics)
                RemoveFixedUpdate(DebugDraw);
            base.OnRemovedFromScene();
        }

        protected abstract void BoxesUpdated(List<OrientedBoundingBox> boxes);

        private bool _calcScheduled;

        private void ScheduleCalcShape()
        {
            if (_calcScheduled)
                return;
            AddScheduledCallback(CalcShape);
            _calcScheduled = true;
        }

        private void CalcShape(long ticks)
        {
            if (!_calcScheduled || _bendyDynamicComponent?.Edges == null)
                return;
            _calcScheduled = false;
            _boxes.Clear();
            foreach (var edge in _bendyDynamicComponent.Edges)
            {
                var curve = edge?.Curve;
                if (curve == null || Definition.Segments == 0)
                    continue;

                var buildStart = 0;
                var buildingBox = default(BoundingBox);
                Vector3 accumUp = Vector3.Zero, accumForward = Vector3.Zero;
                Vector3? firstUp = null;
                var firstForward = Vector3.Zero;
                for (var i = 0; i < Definition.Segments; i++)
                {
                    var t = (i + 0.5f) / Definition.Segments;
                    var pos = (Vector3) Vector3D.Transform(curve.Sample(t),
                        Entity.PositionComp.WorldMatrixNormalizedInv);
                    var up = Vector3.TransformNormal(Vector3.Lerp((Vector3) edge.From.Up, (Vector3) edge.To.Up, t),
                        Entity.PositionComp.WorldMatrixNormalizedInv);
                    var tan = Vector3.TransformNormal((Vector3) curve.SampleDerivative(t),
                        Entity.PositionComp.WorldMatrixNormalizedInv);

                    if (firstUp.HasValue)
                    {
                        accumUp += up;
                        accumForward += tan;

                        var last = i == Definition.Segments - 1;
                        buildingBox.Include(pos);
                        if (last || (Math.Abs(buildingBox.HalfExtents.Dot(up)) > Definition.Height * Definition.VerticalSizeTol ||
                                     firstUp.Value.Dot(up) < (1 - Definition.VerticalAlignTol) ||
                                     Math.Abs(firstForward.Dot(tan)) < (1 - Definition.HorizontalAlignTol)))
                        {
                            accumForward.Normalize();
                            accumUp.Normalize();
                            var m = Matrix.CreateWorld(buildingBox.Center, accumForward, accumUp);
                            if (!float.IsNaN(m.M11))
                                _boxes.Add(CalculateRange(m, edge, buildStart, last ? i + 1 : i));
                        }
                        else
                            continue;
                    }

                    buildStart = i;
                    buildingBox = BoundingBox.CreateInvalid();
                    buildingBox.Include(pos);
                    firstForward = accumForward = tan;
                    firstUp = accumUp = up;
                }
            }

            BoxesUpdated(_boxes);
        }

        private OrientedBoundingBox CalculateRange(Matrix matrix, Edge edge, int min, int max)
        {
            var box = BoundingBox.CreateInvalid();
            var singleInv = Matrix.Invert(ref matrix);
            var mi = Entity.PositionComp.WorldMatrixNormalizedInv * (MatrixD) singleInv;
            var curve = edge.Curve;
            for (var j = min; j <= max; j++)
            {
                var t = j / (float) Definition.Segments;
                var pos = curve.Sample(t);
                var up = Vector3.Lerp((Vector3) edge.From.Up, (Vector3) edge.To.Up, t);
                var tan = (Vector3) curve.SampleDerivative(t);
                var norm = Vector3.Cross(up, tan);

                var nl = (Vector3) Vector3D.TransformNormal(norm, ref mi);
                var ul = (Vector3) Vector3D.TransformNormal(up, ref mi);
                var pl = (Vector3) Vector3D.Transform(pos, ref mi);
                nl.Normalize();
                ul.Normalize();

                box.Include(pl + nl * Definition.HalfWidth);
                box.Include(pl - nl * Definition.HalfWidth);
                box.Include(pl + nl * Definition.HalfWidth + ul * Definition.Height);
                box.Include(pl - nl * Definition.HalfWidth + ul * Definition.Height);
            }

            return new OrientedBoundingBox(box.Center + Vector3.TransformNormal(matrix.Translation, singleInv),
                box.HalfExtents, Quaternion.CreateFromRotationMatrix(matrix));
        }
    }
}