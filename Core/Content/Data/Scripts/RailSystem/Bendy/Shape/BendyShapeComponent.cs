using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Bendy;
using Equinox76561198048419394.RailSystem.Bendy.Shape;
using Equinox76561198048419394.RailSystem.Construction;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.Engine.Physics;
using Sandbox.ModAPI;
using VRage;
using VRage.Components.Entity.Camera;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Input.Devices.Keyboard;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Systems;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy.Shape
{
    public abstract class BendyShapeComponent : MyEntityComponent
    {
        private BendyDynamicComponent _bendyDynamicComponent;
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
            _bendyDynamicComponent = Entity.Components.Get<BendyDynamicComponent>();
            _bendyDynamicComponent.EdgeChanged += OnEdgeChanged;
            OnEdgeChanged();

            foreach (var c in Container)
                OnComponentAdded(c);
            Container.ComponentAdded += OnComponentAdded;
            Container.ComponentRemoved += OnComponentRemoved;
//            AddFixedUpdate(Draw);
        }

        private static readonly MyStringId _stringId = MyStringId.GetOrCompute("RailGradeClearShape");

        public void DebugDraw()
        {
            foreach (var k in _boxes)
            {
                var box = new BoundingBoxD(k.Center - k.HalfExtent, k.Center + k.HalfExtent);
                var m = MatrixD.CreateFromQuaternion(k.Orientation) * Entity.WorldMatrix;
                var queryBox = box;
                queryBox = queryBox.TransformFast(m);
                if (Vector3D.DistanceSquared(queryBox.Center, MyCameraComponent.ActiveCamera.GetPosition()) > 100 * 100 ||
                    !MyCameraComponent.ActiveCamera.GetCameraFrustum().Intersects(queryBox))
                    continue;
                var c = Color.Red;
                MySimpleObjectDraw.DrawTransparentBox(ref m, ref box, ref c, MySimpleObjectRasterizer.SolidAndWireframe, 1, .01f, _stringId, _stringId);
            }
        }
        
        private void OnComponentAdded(MyEntityComponent obj)
        {
            var changer = obj as IModelChanger;
            if (changer != null)
                changer.ModelChanged += ScheduleCalcShape;
            var ctor = obj as ConstructableComponent;
            if (ctor != null)
                ctor.IntegrityChanged += CheckActiveSegments;
        }

        private void OnComponentRemoved(MyEntityComponent obj)
        {
            var changer = obj as IModelChanger;
            if (changer != null)
                changer.ModelChanged -= ScheduleCalcShape;
            var ctor = obj as ConstructableComponent;
            if (ctor != null)
                ctor.IntegrityChanged -= CheckActiveSegments;
        }

        public override void OnBeforeRemovedFromContainer()
        {
            base.OnBeforeRemovedFromContainer();
            foreach (var c in Container)
                OnComponentRemoved(c);
            Container.ComponentAdded -= OnComponentAdded;
            Container.ComponentRemoved -= OnComponentRemoved;
            _bendyDynamicComponent.EdgeChanged -= OnEdgeChanged;
            _bendyDynamicComponent = null;
        }

        private Edge _attachedEdge;

        private void OnEdgeChanged()
        {
            if (_attachedEdge == _bendyDynamicComponent?.Edge)
                return;
            if (_attachedEdge != null)
                _attachedEdge.CurveUpdated -= ScheduleCalcShape;
            _attachedEdge = _bendyDynamicComponent?.Edge;
            if (_attachedEdge != null)
                _attachedEdge.CurveUpdated += ScheduleCalcShape;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            CheckActiveSegments();
            ScheduleCalcShape();
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

        private int DesiredActiveSegments
        {
            get
            {
                if (Entity != null && Entity.InScene)
                    return Definition.ActiveSegments(Container?.Get<ConstructableComponent>()?.BuildPercent ?? 1f);
                return 0;
            }
        }


        private int _activeSegments;

        private void CheckActiveSegments()
        {
            var segs = DesiredActiveSegments;
            if (segs == _activeSegments)
                return;
            ScheduleCalcShape();
        }

        private void CalcShape(long ticks)
        {
            if (!_calcScheduled)
                return;
            _activeSegments = DesiredActiveSegments;
            _calcScheduled = false;
            var edge = _attachedEdge;
            var curve = edge?.Curve;
            if (curve == null || Definition.Segments == 0)
                return;

            int buildStart = 0;
            BoundingBox buildingBox = default(BoundingBox);
            Vector3 accumUp = Vector3.Zero, accumForward = Vector3.Zero;
            Vector3? firstUp = null;
            Vector3 firstForward = Vector3.Zero;
            _boxes.Clear();
            for (var i = 0; i < _activeSegments; i++)
            {
                var t = (i + 0.5f) / Definition.Segments;
                var pos = (Vector3) Vector3D.Transform(curve.Sample(t), Entity.PositionComp.WorldMatrixNormalizedInv);
                var up = Vector3.TransformNormal(Vector3.Lerp((Vector3) edge.From.Up, (Vector3) edge.To.Up, t), Entity.PositionComp.WorldMatrixNormalizedInv);
                var tan = Vector3.TransformNormal((Vector3) curve.SampleDerivative(t), Entity.PositionComp.WorldMatrixNormalizedInv);

                if (firstUp.HasValue)
                {
                    accumUp += up;
                    accumForward += tan;

                    var last = i == _activeSegments - 1;
                    buildingBox.Include(pos);
                    if (last || (Math.Abs(buildingBox.HalfExtents.Dot(up)) > 0.075f || firstUp.Value.Dot(up) < 0.99 || Math.Abs(firstForward.Dot(tan)) < 0.75))
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

            return new OrientedBoundingBox(box.Center + Vector3.TransformNormal(matrix.Translation, singleInv), box.HalfExtents, Quaternion.CreateFromRotationMatrix(matrix));
        }
    }
}