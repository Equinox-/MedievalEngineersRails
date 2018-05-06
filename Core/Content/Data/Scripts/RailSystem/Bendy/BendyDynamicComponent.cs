using System;
using System.Diagnostics;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.ModAPI;
using VRage;
using VRage.Components.Entity.Animations;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Systems;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy
{
    [MyComponent(typeof(MyObjectBuilder_BendyDynamicComponent))]
    [MyDefinitionRequired]
    [MyDependency(typeof(MySkeletonComponent), Recursive = true, Critical = false)]
    public class BendyDynamicComponent : MyEntityComponent
    {
        private Vector3D _from, _to;
        private Vector3 _fromUp, _toUp;

        public Edge Edge { get; private set; }
        public event Action EdgeChanged;

        public BendyDynamicComponentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (BendyDynamicComponentDefinition) def;
        }

        public override bool IsSerialized => true;

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            return new MyObjectBuilder_BendyDynamicComponent()
            {
                From = _from,
                To = _to,
                FromUp = _fromUp,
                ToUp = _toUp
            };
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent bbase)
        {
            var ob = (MyObjectBuilder_BendyDynamicComponent) bbase;
            _from = ob.From;
            _to = ob.To;
            _fromUp = ob.FromUp;
            _toUp = ob.ToUp;
        }

        private void CheckEdge()
        {
            var desired = Entity != null && Entity.InScene;
            if (desired == (Edge != null))
                return;
            var graph = MySession.Static.Components.Get<BendyController>();
            var layer = graph.GetOrCreateLayer(Definition.Layer);
            if (desired)
            {
                var from = layer.GetOrCreateNode(_from, _fromUp);
                var to = layer.GetOrCreateNode(_to, _toUp);
                Edge = layer.CreateEdge(from, to, CurveMode.CubicBez);
                Edge.CurveUpdated += Pose;
                EdgeChanged?.Invoke();
            }
            else
            {
                Edge.CurveUpdated -= Pose;
                Edge.Close();
                Edge = null;
                EdgeChanged?.Invoke();
            }
        }

        private MySkeletonComponent _skeletonComponent;

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            foreach (var c in Container)
                OnComponentAdded(c);
            Container.ComponentAdded += OnComponentAdded;
            Container.ComponentRemoved += OnComponentRemoved;
            EdgeChanged = null;
            if (string.IsNullOrEmpty(Entity.DisplayName))
                Entity.DisplayName = "Bendyrail";
        }

        private void OnComponentRemoved(MyEntityComponent obj)
        {
            var changer = obj as IModelChanger;
            if (changer != null)
                changer.ModelChanged -= Pose;
        }

        private void OnComponentAdded(MyEntityComponent obj)
        {
            var changer = obj as IModelChanger;
            if (changer != null)
                changer.ModelChanged += Pose;
        }

        public override void OnBeforeRemovedFromContainer()
        {
            base.OnBeforeRemovedFromContainer();
            foreach (var c in Container)
                OnComponentRemoved(c);
            Container.ComponentAdded -= OnComponentAdded;
            Container.ComponentRemoved -= OnComponentRemoved;
            EdgeChanged = null;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _skeletonComponent = Entity.Components.Get<MySkeletonComponent>();
            CheckEdge();
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            CheckEdge();
            _skeletonComponent = null;
        }

        private void Pose()
        {
            if (Edge?.From == null || Edge?.To == null || Edge?.Curve == null)
                return;
            var first = Edge.FromMatrix;
            var last = Edge.ToMatrix;
            MatrixD? next = null;
            {
                var opponent = Edge.To.Opposition(Edge.From);
                if (opponent != null)
                    next = MatrixD.CreateWorld(opponent.Position, Edge.Tangent(opponent.Tangent), opponent.Up);
            }
            MatrixD? prev = null;
            {
                var opponent = Edge.From.Opposition(Edge.To);
                if (opponent != null)
                    prev = MatrixD.CreateWorld(opponent.Position, Edge.Tangent(opponent.Tangent), opponent.Up);
            }

            var skeleton = Entity.Components.Get<MySkeletonComponent>();
            if (skeleton?.CharacterBones == null)
                return;
            Entity.PositionComp.WorldMatrix = Bezier.BSpline(first, last, 0.5f);
            {
                // TODO refactor to be tight fitting based on def
                var box = BoundingBoxD.CreateInvalid();
                for (var i = 0; i <= 8; i++)
                {
                    var p = Edge.Curve.Sample(i / 8f);
                    box.Include(Vector3D.Transform(p, Entity.PositionComp.WorldMatrixInvScaled));
                }

                box.Inflate(2f);
                Entity.PositionComp.LocalAABB = (BoundingBox) box;
            }

            var count = Definition.PrimaryBones;
            var leadingAddl = Definition.LeadingBones;
            var trailingAddl = Definition.TrailingBones;

            MatrixD worldPrevious;
            {
                var bones = 0;
                if (prev.HasValue)
                    worldPrevious = Bezier.BSpline(prev.Value, first, (count - leadingAddl - 1) / (float) count);
                else
                {
                    var l = Matrix.Identity;
                    foreach (var bone in skeleton.CharacterBones)
                    {
                        if (!bone.Name.StartsWith(Definition.BonePrefix))
                            continue;
                        if (bones <= leadingAddl)
                            l = bone.Transform.BindTransformMatrix * l;
                        else break;
                        bones++;
                    }

                    worldPrevious = MatrixD.Invert(l) * first;
                }
            }

            {
                var boneId = 0;
                var countMinus1 = (float) (count - 1);
                foreach (var bone in skeleton.CharacterBones)
                {
                    if (!bone.Name.StartsWith(Definition.BonePrefix))
                        continue;
                    if (boneId >= leadingAddl + count + trailingAddl)
                        break;

                    MatrixD desired;
                    if (boneId < leadingAddl && prev.HasValue)
                        desired = Bezier.BSpline(prev.Value, first, (boneId + count - leadingAddl) / countMinus1);
                    else if (boneId < leadingAddl)
                        desired = bone.Transform.BindTransformMatrix * worldPrevious;
                    else if (boneId < leadingAddl + count)
                        desired = Bezier.BSpline(first, last, (boneId - leadingAddl) / countMinus1);
                    else if (next.HasValue)
                        desired = Bezier.BSpline(last, next.Value, (boneId - count - leadingAddl) / countMinus1);
                    else
                        desired = bone.Transform.BindTransformMatrix * worldPrevious;

                    if (boneId == 0)
                    {
                        var m = (Matrix) (desired * Entity.PositionComp.WorldMatrixInvScaled);
                        bone.SetTransformFromAbsoluteMatrix(ref m, false);
                    }
                    else
                    {
                        var local = desired * MatrixD.Invert(worldPrevious);
                        var lTrans = (Vector3) local.Translation;
                        var lRot = Quaternion.CreateFromRotationMatrix(local);
                        bone.SetTransform(ref lTrans, ref lRot);
                    }

                    worldPrevious = desired;
                    boneId++;
                }
            }

            skeleton.ComputeAbsoluteTransforms();
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_BendyDynamicComponent : MyObjectBuilder_EntityComponent
    {
        [XmlElement]
        public SerializableVector3D From;

        [XmlElement]
        public SerializableVector3D To;

        [XmlElement]
        public SerializableVector3 FromUp;

        [XmlElement]
        public SerializableVector3 ToUp;
    }
}