using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Bendy;
using Equinox76561198048419394.RailSystem.Construction;
using Equinox76561198048419394.RailSystem.Util;
using Equinox76561198048419394.RailSystem.Util.Curve;
using Equinox76561198048419394.RailSystem.Voxel.Shape;
using Sandbox.Game.Entities;
using VRage;
using VRage.Components;
using VRage.Components.Entity.CubeGrid;
using VRage.Entities.Gravity;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Voxels;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Voxel
{
    [MyComponent(typeof(MyObjectBuilder_RailGradeComponent))]
    [MyDependency(typeof(BendyComponent), Critical = true)]
    [MyDefinitionRequired(typeof(RailGradeComponentDefinition))]
    public class RailGradeComponent : MyEntityComponent, IConstructionPrereq, IRailGradeComponent
    {
        public RailGradeComponentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            Definition = (RailGradeComponentDefinition) definition;
        }

        private BendyComponent _bendy;

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            _bendy = Entity.Components.Get<BendyComponent>();
            _bendy.EdgeSetupChanged += EdgeSetupChanged;
            EdgeSetupChanged(_bendy);
        }

        public override void OnBeforeRemovedFromContainer()
        {
            base.OnBeforeRemovedFromContainer();
            _bendy.EdgeSetupChanged -= EdgeSetupChanged;
            EdgeSetupChanged(_bendy);
            _bendy = null;
        }

        private void EdgeSetupChanged(BendyComponent comp)
        {
            _excavationCache = default;
            _supportCache = default;
            _shapeDirty = true;
        }

        private MyTuple<RailGradeComponentDefinition.ShapeOptions, IGradeShape> _excavationCache;

        public IGradeShape GetExcavate(in RailGradeComponentDefinition.ShapeOptions options)
        {
            if (_bendy == null || _bendy.Edges.Length == 0) return null;
            if (_excavationCache.Item1.Equals(options) && _excavationCache.Item2 != null) return _excavationCache.Item2;

            var shape = Definition.GetExcavate(in options);
            IGradeShape composite;
            if (shape.HasValue)
            {
                var s = shape.Value;
                composite = CompositeGradeShape.Composite(_bendy.Edges
                    .Where(e => e?.Curve != null)
                    .Select(e => s.CreateShape(new EdgeBlit(e), true)));
            }
            else
                composite = null;

            _excavationCache = MyTuple.Create(options, composite);
            return composite;
        }

        private MyTuple<RailGradeComponentDefinition.ShapeOptions, IGradeShape> _supportCache;

        public IGradeShape GetSupport(in RailGradeComponentDefinition.ShapeOptions options)
        {
            if (_bendy?.Edges == null || _bendy.Edges.Length == 0) return null;
            if (_supportCache.Item1.Equals(options) && _supportCache.Item2 != null) return _supportCache.Item2;
            var shape = Definition.GetSupport(in options);
            IGradeShape composite;
            if (shape.HasValue)
            {
                var s = shape.Value;
                composite = CompositeGradeShape.Composite(_bendy.Edges
                    .Where(e => e?.Curve != null)
                    .Select(e => s.CreateShape(new EdgeBlit(e), false)));
            }
            else
                composite = null;
            _supportCache = MyTuple.Create(options, composite);
            return composite;
        }

        public bool TryGetNearestCurveOrientation(in Vector3D query, out double nearestDistSq, out Vector3D nearestPos, out Quaternion nearestOrientation)
        {
            nearestDistSq = double.PositiveInfinity;
            nearestPos = default;
            nearestOrientation = default;
            if (_bendy?.Edges == null || _bendy.Edges.Length == 0) return false;
            foreach (var edge in _bendy.Edges)
            {
                float t0 = 0;
                float t1 = 0;
                CurveExtensions.NearestPoint(edge.Curve, query, 16, ref t0, ref t1);
                var t = (t0 + t1) / 2;
                var pos = edge.Curve.Sample(t);
                var distSq = Vector3D.DistanceSquared(in pos, in pos);
                if (distSq >= nearestDistSq) continue;
                nearestDistSq = distSq;
                nearestPos = pos;
                var forward = (Vector3) edge.Curve.SampleDerivative(t);
                forward.Normalize();
                var up = Vector3.Lerp(edge.FromUp, edge.ToUp, t);
                nearestOrientation = Quaternion.CreateFromForwardUp(forward, up);
            }

            return !double.IsPositiveInfinity(nearestDistSq);
        }

        public override bool IsSerialized => false;

        private bool _isComplete = false;

        private bool _shapeDirty = true;
        private BoundingBoxD _localBox;

        private void CalculateShape()
        {
            if (!Definition.RequiredSupport.HasValue)
                return;
            var shape = Definition.RequiredSupport.Value;
            var edge = _bendy.Edges.FirstOrDefault();
            var curve = edge?.Curve;
            if (curve == null)
                return;
            _shapeDirty = false;
            _localBox = BoundingBoxD.CreateInvalid();
            for (var i = 0; i < shape.Segments; i++)
            {
                Matrix matrix;
                {
                    var t = (i + 0.5f) / shape.Segments;
                    var pos = (Vector3) Vector3D.Transform(curve.Sample(t), Entity.PositionComp.WorldMatrixNormalizedInv);
                    var up = Vector3.TransformNormal(Vector3.Lerp((Vector3) edge.From.Up, (Vector3) edge.To.Up, t),
                        Entity.PositionComp.WorldMatrixNormalizedInv);
                    var tan = Vector3.TransformNormal((Vector3) curve.SampleDerivative(t), Entity.PositionComp.WorldMatrixNormalizedInv);
                    tan.Normalize();
                    up.Normalize();

                    matrix = Matrix.CreateWorld(Vector3.Zero, tan, up);
                }

                var box = BoundingBox.CreateInvalid();
                var mi = Entity.PositionComp.WorldMatrixNormalizedInv * (MatrixD) Matrix.Invert(ref matrix);

                for (var j = 0; j <= 1; j++)
                {
                    var t = (i + j) / (float) shape.Segments;
                    var pos = curve.Sample(t);
                    var up = Vector3.Lerp((Vector3) edge.From.Up, (Vector3) edge.To.Up, t);
                    var tan = (Vector3) curve.SampleDerivative(t);
                    var norm = Vector3.Cross(up, tan);
                    norm.Normalize();

                    var nl = (Vector3) Vector3D.TransformNormal(norm, ref mi);
                    var ul = (Vector3) Vector3D.TransformNormal(up, ref mi);
                    var pl = (Vector3) Vector3D.Transform(pos, ref mi);

                    box.Include(pl + nl * shape.HalfWidth + ul * shape.VerticalOffset);
                    box.Include(pl - nl * shape.HalfWidth + ul * shape.VerticalOffset);
                }

                var obb = new OrientedBoundingBox(Vector3.Transform(box.Center, matrix), box.HalfExtents, Quaternion.CreateFromRotationMatrix(matrix));
                _localBox = _localBox.Include(obb.GetAABB());
            }
        }

        private readonly MyStorageData _voxelStorage = new MyStorageData();

        private bool CalculateComplete()
        {
            if (!Definition.RequiredSupport.HasValue)
                return true;

            if (_shapeDirty)
                CalculateShape();
            var edge = _bendy.Edges.FirstOrDefault();
            var curve = edge?.Curve;
            if (curve == null)
                return true;

            var shape = Definition.RequiredSupport.Value;
            var sphere = new BoundingSphereD(Vector3D.Transform(_localBox.Center, Entity.PositionComp.WorldMatrix), _localBox.HalfExtents.Length());
            var supported = 0;
            var total = 0;
            var entities = MyEntities.GetTopMostEntitiesInSphere(ref sphere);
            using (entities.GetClearToken())
            {
                var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(Entity.GetPosition());
                gravity.Normalize();

                for (var i = 0; i < shape.Segments; i++)
                {
                    var t = (i + 0.5f) / shape.Segments;
                    var pos = curve.Sample(t);
                    pos += gravity;

                    var aabb = new BoundingBoxD(pos - 0.5f, pos + 0.5f);

                    var found = false;
                    foreach (var e in entities)
                    {
                        var vox = e as MyVoxelBase;
                        var grid = e.Components.Get<MyGridDataComponent>();
                        if (vox != null)
                        {
                            Vector3I vc;
                            var test = pos;
                            MyVoxelCoordSystems.WorldPositionToVoxelCoord(vox.PositionLeftBottomCorner, ref test, out vc);
                            _voxelStorage.Resize(Vector3I.One);
                            vox.Storage.ReadRange(_voxelStorage, MyStorageDataTypeFlags.Content, 0, in vc, in vc);
                            found |= _voxelStorage.Get(MyStorageDataTypeEnum.Content, ref Vector3I.Zero) > 0;
                        }
                        else if (grid != null)
                            found |= grid.AnyBlocksInAABB(aabb);

                        if (found) break;
                    }

                    if (found)
                        supported++;
                    total++;
                }
            }

            return supported >= 0.8 * total;
        }

        public string IncompleteMessage => "The segment isn't properly supported";

        public bool IsComplete
        {
            get
            {
                if (!_isComplete || _shapeDirty)
                    _isComplete = CalculateComplete();
                return _isComplete;
            }
        }

        public bool IsValid => _bendy?.Edges != null && Definition != null;

        public RailGradeComponentBlit Blit()
        {
            return new RailGradeComponentBlit()
            {
                Edges = _bendy?.Edges?.Select(x => new EdgeBlit(x)).ToArray() ?? Array.Empty<EdgeBlit>(),
                Definition = Definition.Id
            };
        }
    }

    public struct RailGradeComponentBlit : IRailGradeComponent
    {
        public EdgeBlit[] Edges;
        public SerializableDefinitionId Definition;

        private RailGradeComponentDefinition Def => Edges?.Length > 0 ? MyDefinitionManager.Get<RailGradeComponentDefinition>(Definition) : null; 

        public IGradeShape GetSupport(in RailGradeComponentDefinition.ShapeOptions options)
        {
            var shape = Def?.GetSupport(in options);
            if (!shape.HasValue) return null;
            var s = shape.Value;
            return CompositeGradeShape.Composite(Edges.Select(e => s.CreateShape(e, false)));
        }

        public IGradeShape GetExcavate(in RailGradeComponentDefinition.ShapeOptions options)
        {
            var shape = Def?.GetExcavate(in options);
            if (!shape.HasValue) return null;
            var s = shape.Value;
            return CompositeGradeShape.Composite(Edges.Select(e => s.CreateShape(e, true)));
        }
    }

    public interface IRailGradeComponent
    {
        IGradeShape GetSupport(in RailGradeComponentDefinition.ShapeOptions options);
        IGradeShape GetExcavate(in RailGradeComponentDefinition.ShapeOptions options);
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailGradeComponent : MyObjectBuilder_EntityComponent
    {
    }
}