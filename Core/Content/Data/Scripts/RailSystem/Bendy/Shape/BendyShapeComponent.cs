using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Components.Entity.Camera;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy.Shape
{
    public abstract class BendyShapeComponent : MyEntityComponent, IComponentDebugDraw
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
            if (!RailConstants.Debug.DrawBendyPhysics)
                return;
            if (Vector3D.DistanceSquared(MyCameraComponent.ActiveCamera.GetPosition(), Entity.GetPosition()) > 50 * 50)
                return;
            foreach (var k in _boxes)
            {
                var box = new BoundingBoxD(-k.HalfExtent, k.HalfExtent);
                var m = MatrixD.CreateFromQuaternion(k.Orientation) * MatrixD.CreateTranslation(k.Center) * Entity.WorldMatrix;
                var queryBox = box;
                queryBox = queryBox.TransformFast(m);
                if (Vector3D.DistanceSquared(queryBox.Center, MyCameraComponent.ActiveCamera.GetPosition()) >
                    25 * 25 ||
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
        }

        protected abstract void BoxesUpdated(List<OrientedBoundingBox> boxes);

        private bool _calcScheduled;

        private void ScheduleCalcShape()
        {
            if (_calcScheduled)
                return;
            ScheduleCalc();
            _calcScheduled = true;
        }

        protected abstract void ScheduleCalc();

        protected void CalcShape()
        {
            if (!_calcScheduled || _bendyDynamicComponent?.Edges == null || Definition.Segments == 0)
                return;
            _calcScheduled = false;
            _boxes.Clear();
            foreach (var edge in _bendyDynamicComponent.Edges)
            {
                var originalHead = _boxes.Count;
                for (var i = 0; i < Definition.Segments; i++)
                {
                    _boxes.Add(CalculateRange(edge, i / (float) Definition.Segments, (i + 1) / (float) Definition.Segments));
                }

                var head = originalHead; // index of the LHS box that's actively being expanded
                var tail = 1; // (_boxes.Count - tail) == index of the RHS box that's actively being expanded
                while (head < _boxes.Count - tail)
                {
                    // Try merging [head] and [head+1]
                    {
                        var a = _boxes[head];
                        var b = _boxes[head + 1];
                        OrientedBoundingBox result;
                        Merge(in a, in b, out result);
                        var baseVol = a.HalfExtent.Volume + b.HalfExtent.Volume;
                        var mergeVol = result.HalfExtent.Volume;
                        if (mergeVol * (1 - Definition.VolumeErrorTolerance) < baseVol)
                        {
                            _boxes[head] = result;
                            _boxes.RemoveAt(head + 1);
                        }
                        else
                        {
                            head++;
                        }
                    }
                    // Try merging [tail] and [tail - 1]
                    var tailReal = _boxes.Count - tail;
                    if (tailReal - 1 >= head)
                    {
                        var a = _boxes[tailReal]; 
                        var b = _boxes[tailReal - 1];
                        OrientedBoundingBox result;
                        Merge(in a, in b, out result);
                        var baseVol = a.HalfExtent.Volume + b.HalfExtent.Volume;
                        var mergeVol = result.HalfExtent.Volume;
                        if (mergeVol * (1 - Definition.VolumeErrorTolerance) < baseVol)
                        {
                            _boxes[tailReal - 1] = result;
                            _boxes.RemoveAt(tailReal);
                        }
                        else
                        {
                            tail++;
                        }
                    }
                }

                for (var i = head; i < _boxes.Count - 1; i++)
                {
                    var a = _boxes[i];
                    var b = _boxes[i + 1];
                    OrientedBoundingBox result;
                    Merge(in a, in b, out result);
                    var baseVol = a.HalfExtent.Volume + b.HalfExtent.Volume;
                    var mergeVol = result.HalfExtent.Volume;
                    if (mergeVol * (1 - Definition.VolumeErrorTolerance) < baseVol)
                    {
                        _boxes[i] = result;
                        _boxes.RemoveAt(i + 1);
                        i--;
                    }
                }
            }

            BoxesUpdated(_boxes);
        }

        private void Merge(in OrientedBoundingBox a, in OrientedBoundingBox b, out OrientedBoundingBox result)
        {
            var halfOrientation = Quaternion.Normalize(a.Orientation * a.HalfExtent.Volume + b.Orientation * b.HalfExtent.Volume);
            var halfOrientationInv = Matrix.CreateFromQuaternion(Quaternion.Conjugate(halfOrientation));

            var builder = BoundingBox.CreateInvalid();
            IncludeObb(ref builder, ref halfOrientationInv, in a);
            IncludeObb(ref builder, ref halfOrientationInv, in b);

            result.Orientation = halfOrientation;
            result.Center = Vector3.TransformNormal(builder.Center, Matrix.CreateFromQuaternion(halfOrientation));
            result.HalfExtent = builder.HalfExtents;
        }

        private void IncludeObb(ref BoundingBox builder, ref Matrix worldToBuilder, in OrientedBoundingBox box)
        {
            var mTotal = Matrix.CreateFromQuaternion(box.Orientation) * worldToBuilder;
            var invCenter = Vector3.Transform(box.Center, ref worldToBuilder);

            var x0 = invCenter + mTotal.Right * box.HalfExtent.X;
            var x1 = invCenter + mTotal.Right * -box.HalfExtent.X;

            var y0 = mTotal.Up * box.HalfExtent.Y;
            var y1 = mTotal.Up * -box.HalfExtent.Y;

            var z0 = mTotal.Forward * box.HalfExtent.Z;
            var z1 = mTotal.Forward * -box.HalfExtent.Z;

            builder.Include(x0 + y0 + z0);
            builder.Include(x0 + y0 + z1);
            builder.Include(x0 + y1 + z0);
            builder.Include(x0 + y1 + z1);
            builder.Include(x1 + y0 + z0);
            builder.Include(x1 + y0 + z1);
            builder.Include(x1 + y1 + z0);
            builder.Include(x1 + y1 + z1);
        }

        private OrientedBoundingBox CalculateRange(Edge edge, float t0, float t1)
        {
            var curve = edge.Curve;

            var pos0 = curve.Sample(t0);
            var up0 = Vector3D.Lerp(edge.From.Up, edge.To.Up, t0);
            var tan0 = curve.SampleDerivative(t0);

            var pos1 = curve.Sample(t1);
            var up1 = Vector3D.Lerp(edge.From.Up, edge.To.Up, t1);
            var tan1 = curve.SampleDerivative(t1);

            var centerTransform = MatrixD.CreateWorld((pos0 + pos1) / 2, tan0 + tan1, up0 + up1);
            var centerTransformInv = MatrixD.Invert(ref centerTransform);

            var box = BoundingBox.CreateInvalid();
            IncludeCurvePhysics(ref box, ref centerTransformInv, ref pos0, ref up0, ref tan0);
            IncludeCurvePhysics(ref box, ref centerTransformInv, ref pos1, ref up1, ref tan1);

            var centerTransformLocal = centerTransform * Entity.PositionComp.WorldMatrixNormalizedInv;
            return new OrientedBoundingBox(box, centerTransformLocal);
        }

        private void IncludeCurvePhysics(ref BoundingBox box, ref MatrixD inverse, ref Vector3D pos, ref Vector3D up, ref Vector3D tan)
        {
            Vector3D.Cross(ref up, ref tan, out var norm);

            Vector3D.TransformNormal(ref norm, ref inverse, out var nl);
            Vector3D.TransformNormal(ref up, ref inverse, out var ul);
            Vector3D.Transform(ref pos, ref inverse, out var pl);
            nl.Normalize();
            ul.Normalize();

            box.Include((Vector3) (pl + nl * Definition.HalfWidth));
            box.Include((Vector3) (pl - nl * Definition.HalfWidth));
            box.Include((Vector3) (pl + nl * Definition.HalfWidth + ul * Definition.Height));
            box.Include((Vector3) (pl - nl * Definition.HalfWidth + ul * Definition.Height));
        }
    }
}