using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Bendy;
using Equinox76561198048419394.RailSystem.Construction;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Logging;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Voxel
{
    [MyComponent(typeof(MyObjectBuilder_RailGradeComponent))]
    [MyDependency(typeof(BendyDynamicComponent), Critical = true)]
    [MyDefinitionRequired]
    public class RailGradeComponent : MyEntityComponent, IConstructionPrereq, IRailGradeComponent
    {
        public RailGradeComponentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            Definition = (RailGradeComponentDefinition) definition;
        }

        private BendyDynamicComponent _bendy;

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            _bendy = Entity.Components.Get<BendyDynamicComponent>();
            _bendy.EdgeChanged += OnEdgeChanged;
            OnEdgeChanged();
        }

        public override void OnBeforeRemovedFromContainer()
        {
            base.OnBeforeRemovedFromContainer();
            _bendy.EdgeChanged -= OnEdgeChanged;
            _bendy = null;
            OnEdgeChanged();
        }

        private void OnEdgeChanged()
        {
            _excavationCache = null;
            _shapeDirty = true;
        }

        private RailGradeShape _excavationCache;

        public RailGradeShape Excavation
        {
            get
            {
                if (_excavationCache != null) return _excavationCache;
                var e = _bendy?.Edge;
                if (e == null || !Definition.Excavate.HasValue) return _excavationCache;
                var s = Definition.Excavate.Value;
                return _excavationCache = new RailGradeShape(new EdgeBlit(e), s.Width, s.RelaxAngleRadians, s.VerticalOffset, s.Segments, -s.Height);
            }
        }

        private RailGradeShape _supportCache;

        public RailGradeShape Support
        {
            get
            {
                if (_supportCache != null) return _supportCache;
                var e = _bendy?.Edge;
                if (e == null || !Definition.Support.HasValue) return _supportCache;
                var s = Definition.Support.Value;
                return _supportCache = new RailGradeShape(new EdgeBlit(e), s.Width, s.RelaxAngleRadians, s.VerticalOffset, s.Segments, s.Height);
            }
        }

        public override bool IsSerialized => false;

        private bool _isComplete = false;

        private bool _shapeDirty = true;
        private readonly List<OrientedBoundingBox> _requiredShape = new List<OrientedBoundingBox>();
        private BoundingBoxD _localBox;

        private void CalculateShape()
        {
            if (!Definition.RequiredSupport.HasValue)
                return;
            _shapeDirty = false;
            var shape = Definition.RequiredSupport.Value;
            var edge = _bendy.Edge;
            var curve = edge.Curve;
            _requiredShape.Clear();
            _localBox = BoundingBoxD.CreateInvalid();
            for (var i = 0; i < shape.Segments; i++)
            {
                Matrix matrix;
                {
                    var t = (i + 0.5f) / shape.Segments;
                    var pos = (Vector3) Vector3D.Transform(curve.Sample(t), Entity.PositionComp.WorldMatrixNormalizedInv);
                    var up = Vector3.TransformNormal(Vector3.Lerp((Vector3) edge.From.Up, (Vector3) edge.To.Up, t), Entity.PositionComp.WorldMatrixNormalizedInv);
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
                _requiredShape.Add(obb);
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

            var sphere = new BoundingSphereD(Vector3D.Transform(_localBox.Center, Entity.PositionComp.WorldMatrix), _localBox.HalfExtents.Length());
            var ents = MyEntities.GetTopMostEntitiesInSphere(ref sphere);

            var supported = 0;
            var total = 0;
            foreach (var obb in _requiredShape)
            {
                var worldObb = new OrientedBoundingBoxD(obb.Center, obb.HalfExtent, obb.Orientation);
                worldObb.Transform(Entity.WorldMatrix);

                var worldAbb = worldObb.GetAABB();

                var halfExt = new Vector3I((int) Math.Ceiling(worldAbb.HalfExtents.X), (int) Math.Ceiling(worldAbb.HalfExtents.Y), (int) Math.Ceiling(worldAbb.HalfExtents.Z));
                var halfExtNeg = -halfExt;
                for (var itr = new Vector3I_RangeIterator(ref halfExtNeg, ref halfExt); itr.IsValid(); itr.MoveNext())
                {
                    var cell = new BoundingBoxD(worldAbb.Center + itr.Current - 0.5f, worldAbb.Center + itr.Current + 0.5f);

                    if (worldObb.Contains(ref cell) == ContainmentType.Disjoint) continue;

                    var found = false;
                    foreach (var e in ents)
                    {
                        var vox = e as MyVoxelBase;
                        var grid = e as MyCubeGrid;
                        if (vox != null)
                        {
                            Vector3I vc;
                            var test = cell.Center;
                            MyVoxelCoordSystems.WorldPositionToVoxelCoord(vox.PositionLeftBottomCorner, ref test, out vc);
                            _voxelStorage.Resize(Vector3I.One);
                            vox.Storage.ReadRange(_voxelStorage, MyStorageDataTypeFlags.Content, 0, vc, vc);
                            found |= _voxelStorage.Get(MyStorageDataTypeEnum.Content, ref Vector3I.Zero) > 0;
                        }
                        else if (grid != null)
                            found |= grid.AnyBlocksInAABB(cell);

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

        public RailGradeComponentBlit Blit()
        {
            return new RailGradeComponentBlit()
            {
                Edge = new EdgeBlit(_bendy.Edge),
                Definition = Definition.Id
            };
        }

        public void Unblit(out RailGradeShape fillShape, out RailGradeShape excavateShape)
        {
            fillShape = Support;
            excavateShape = Excavation;
        }
    }
    
    public struct RailGradeComponentBlit : IRailGradeComponent
    {
        public EdgeBlit Edge;
        public DefinitionIdBlit Definition;

        public void Unblit(out RailGradeShape fillShape, out RailGradeShape excavateShape)
        {
            fillShape = excavateShape = null;
            
            var def = MyDefinitionManager.Get<RailGradeComponentDefinition>(Definition);
            if (def == null)
                return;

            if (def.Support.HasValue)
            {
                var s = def.Support.Value;
                fillShape = new RailGradeShape(Edge, s.Width, s.RelaxAngleRadians, s.VerticalOffset, s.Segments, s.Height);
            }
            
            if (def.Excavate.HasValue)
            {
                var s = def.Excavate.Value;
                excavateShape = new RailGradeShape(Edge, s.Width, s.RelaxAngleRadians, s.VerticalOffset, s.Segments, s.Height);
            }
        }
    }

    public interface IRailGradeComponent
    {
        void Unblit(out RailGradeShape fillShape, out RailGradeShape excavateShape);
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailGradeComponent : MyObjectBuilder_EntityComponent
    {
    }
}