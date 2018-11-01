using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Bendy;
using Equinox76561198048419394.RailSystem.Construction;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Voxel
{
    [MyComponent(typeof(MyObjectBuilder_RailGradeComponent))]
    [MyDependency(typeof(BendyComponent), Critical = true)]
    [MyDefinitionRequired]
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
            _excavationCache = null;
            _shapeDirty = true;
        }

        private IGradeShape _excavationCache;

        public IGradeShape Excavation
        {
            get
            {
                if (_excavationCache != null) return _excavationCache;
                if (_bendy == null || _bendy.Edges.Count == 0 || !Definition.Excavate.HasValue) return _excavationCache;
                var s = Definition.Excavate.Value;
                return _excavationCache = CompositeGradeShape.Composite(
                    _bendy.Edges.Select(e => new RailGradeShape(new EdgeBlit(e), s.Width, s.RelaxAngleRadians, s.VerticalOffset, s.Segments, -s.Height, s.EndPadding))
                        .ToArray());
            }
        }

        private IGradeShape _supportCache;

        public IGradeShape Support
        {
            get
            {
                if (_supportCache != null) return _supportCache;
                if (_bendy == null || _bendy.Edges.Count == 0 || !Definition.Support.HasValue) return _supportCache;
                var s = Definition.Support.Value;
                return _supportCache = CompositeGradeShape.Composite(
                    _bendy.Edges.Select(e => new RailGradeShape(new EdgeBlit(e), s.Width, s.RelaxAngleRadians, s.VerticalOffset, s.Segments, s.Height, s.EndPadding))
                        .ToArray());
            }
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
            var entities = MyEntities.GetTopMostEntitiesInSphere(ref sphere);
            var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(Entity.GetPosition());
            gravity.Normalize();

            var supported = 0;
            var total = 0;
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
                    var grid = e as MyCubeGrid;
                    if (vox != null)
                    {
                        Vector3I vc;
                        var test = pos;
                        MyVoxelCoordSystems.WorldPositionToVoxelCoord(vox.PositionLeftBottomCorner, ref test, out vc);
                        _voxelStorage.Resize(Vector3I.One);
                        vox.Storage.ReadRange(_voxelStorage, MyStorageDataTypeFlags.Content, 0, vc, vc);
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

        public RailGradeComponentBlit Blit()
        {
            return new RailGradeComponentBlit()
            {
                Edges = _bendy.Edges.Select(x => new EdgeBlit(x)).ToArray(),
                Definition = Definition.Id
            };
        }

        public void Unblit(out IGradeShape fillShape, out IGradeShape excavateShape)
        {
            fillShape = Support;
            excavateShape = Excavation;
        }
    }

    public struct RailGradeComponentBlit : IRailGradeComponent
    {
        public EdgeBlit[] Edges;
        public DefinitionIdBlit Definition;

        public void Unblit(out IGradeShape fillShape, out IGradeShape excavateShape)
        {
            fillShape = excavateShape = null;

            var def = MyDefinitionManager.Get<RailGradeComponentDefinition>(Definition);
            if (def == null)
                return;

            if (def.Support.HasValue)
            {
                var s = def.Support.Value;
                fillShape = CompositeGradeShape.Composite(Edges
                    .Select(e => new RailGradeShape(e, s.Width, s.RelaxAngleRadians, s.VerticalOffset, s.Segments, s.Height, s.EndPadding)).ToArray());
            }

            if (def.Excavate.HasValue)
            {
                var s = def.Excavate.Value;
                excavateShape = CompositeGradeShape.Composite(Edges
                    .Select(e => new RailGradeShape(e, s.Width, s.RelaxAngleRadians, s.VerticalOffset, s.Segments, -s.Height, s.EndPadding)).ToArray());
            }
        }
    }

    public interface IRailGradeComponent
    {
        void Unblit(out IGradeShape fillShape, out IGradeShape excavateShape);
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailGradeComponent : MyObjectBuilder_EntityComponent
    {
    }
}