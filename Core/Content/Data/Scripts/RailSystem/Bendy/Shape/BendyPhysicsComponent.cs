using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Construction;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.Engine.Physics;
using Sandbox.ModAPI;
using VRage;
using VRage.Components;
using VRage.Components.Physics;
using VRage.Engine;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
// ReSharper disable ConditionIsAlwaysTrueOrFalse

namespace Equinox76561198048419394.RailSystem.Bendy.Shape
{
    [MyComponent(typeof(MyObjectBuilder_BendyPhysicsComponent))]
    [MyDependency(typeof(BendyComponent), Critical = true, Recursive = true)]
    [MyDependency(typeof(ConstructableComponent), Critical = false, Recursive = false)]
    [MyDefinitionRequired(typeof(BendyPhysicsComponentDefinition))]
    public class BendyPhysicsComponent : BendyShapeComponent
    {
        private readonly MyTimedUpdate _calcDel;

        public new BendyPhysicsComponentDefinition Definition { get; private set; }
        private readonly List<MyEntity> _physicsProxies = new List<MyEntity>();

        public BendyPhysicsComponent()
        {
            _calcDel = ProxyCalculate;
        }
        
        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            Definition = (BendyPhysicsComponentDefinition) definition;
        }

        public override void OnAddedToScene()
        {
            CloseProxies();
        }

        private void CloseProxies()
        {
            foreach (var k in _physicsProxies)
                k.Close();
            _physicsProxies.Clear();
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            foreach (var k in _physicsProxies)
                k.Close();
            _physicsProxies.Clear();
        }

        private bool _destroyEnvItems;

        public void DestroyEnvItems()
        {
            if (!Definition.DestroyEnvItems)
                return;
            _destroyEnvItems = true;
            if (Entity.InScene && Boxes.Count > 0)
                DestroyEnvItemsInternal();
        }

        private void DestroyEnvItemsInternal()
        {
            _destroyEnvItems = false;

            foreach (var box in Boxes)
            {
                var aabb = new BoundingBox(box.Center - box.HalfExtent, box.Center + box.HalfExtent);
                var tmp = aabb;
                tmp.Inflate(new Vector3(1, 10, 1));
                FarmingExtensions.DisableItemsIn(new OrientedBoundingBoxD(tmp, Entity.PositionComp.WorldMatrix));
            }
        }

        protected override void BoxesUpdated(List<OrientedBoundingBox> boxes)
        {
            CloseProxies();
            foreach (var box in boxes)
            {
                var ent = MyAPIGateway.Entities.CreateFromObjectBuilder(new MyObjectBuilder_EntityBase()
                {
                    SubtypeName = "BendyPhysicsProxy",
                    EntityDefinitionId = new SerializableDefinitionId(typeof(MyObjectBuilder_EntityBase), "BendyPhysicsProxy"),
                    PositionAndOrientation = new MyPositionAndOrientation(Entity.PositionComp.WorldMatrix),
                    PersistentFlags = MyPersistentEntityFlags2.InScene
                });
                ent.Save = false;
                ent.Components.Add(new BendyShapeProxy(Entity));

                var localMatrix = Matrix.CreateFromQuaternion(box.Orientation);
                localMatrix.Translation = box.Center;
                var worldMatrix = localMatrix * Entity.PositionComp.WorldMatrix;
                ent.PositionComp.WorldMatrix = worldMatrix;

                var aabb = new BoundingBox(-box.HalfExtent, box.HalfExtent);
                ent.PositionComp.LocalAABB = aabb;
                const int defaultCollisionLayer = 15;
                ent.InitBoxPhysics(Definition.Material, Vector3.Zero, box.HalfExtent * 2, 0f,
                    0f, 0f, defaultCollisionLayer, RigidBodyFlag.RBF_STATIC);
                MyAPIGateway.Entities.AddEntity(ent);
                ent.Physics.Activate();
                _physicsProxies.Add(ent);
            }

            if (_destroyEnvItems)
                DestroyEnvItemsInternal();
        }

        protected override void ScheduleCalc()
        {
            AddScheduledCallback(_calcDel);
        }

        [Update(false)]
        private void ProxyCalculate(long dt)
        {
            base.CalcShape();
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_BendyPhysicsComponent : MyObjectBuilder_EntityComponent
    {
    }
}