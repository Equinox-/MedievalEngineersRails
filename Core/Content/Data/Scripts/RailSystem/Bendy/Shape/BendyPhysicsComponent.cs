using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Construction;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.Engine.Physics;
using Sandbox.ModAPI;
using VRage;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy.Shape
{
    [MyComponent(typeof(MyObjectBuilder_BendyPhysicsComponent))]
    [MyDependency(typeof(BendyComponent), Critical = true, Recursive = true)]
    [MyDependency(typeof(ConstructableComponent), Critical = false, Recursive = false)]
    [MyDefinitionRequired]
    public class BendyPhysicsComponent : BendyShapeComponent
    {
        private static bool DebugAsChildren = false;

        public new BendyPhysicsComponentDefinition Definition { get; private set; }
        private readonly List<MyEntity> _physicsProxies = new List<MyEntity>();

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            Definition = (BendyPhysicsComponentDefinition) definition;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            foreach (var k in _physicsProxies)
                k.Close();
            _physicsProxies.Clear();
//            AddFixedUpdate(DebugDraw);
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
            while (_physicsProxies.Count > boxes.Count)
            {
                _physicsProxies[_physicsProxies.Count - 1]?.Close();
                _physicsProxies.RemoveAt(_physicsProxies.Count - 1);
            }

            while (_physicsProxies.Count < boxes.Count)
            {
                var ent = MyAPIGateway.Entities.CreateFromObjectBuilder(new MyObjectBuilder_EntityBase()
                {
                    SubtypeName = "BendyPhysicsProxy",
                    PositionAndOrientation = new MyPositionAndOrientation(Entity.PositionComp.WorldMatrix),
                    PersistentFlags = MyPersistentEntityFlags2.InScene
                });
                ent.Save = false;
                ent.IsPreview = true;
                ent.Components.Add(new BendyShapeProxy(Entity));
                if (DebugAsChildren)
                    Entity.Hierarchy.AddChild(ent);
                else
                    MyAPIGateway.Entities.AddEntity(ent);
                _physicsProxies.Add(ent);
            }

            for (var i = 0; i < _physicsProxies.Count; i++)
            {
                var box = boxes[i];
                var ent = _physicsProxies[i];
                if (ent.Physics != null)
                {
                    ent.Physics.Close();
                    ent.Components.Remove(ent.Physics);
                }

                var m = Matrix.CreateFromQuaternion(box.Orientation);
                if (DebugAsChildren)
                    ent.PositionComp.LocalMatrix = m;
                else
                    ent.PositionComp.WorldMatrix = m * Entity.PositionComp.WorldMatrix;
                var aabb = new BoundingBox(box.Center - box.HalfExtent, box.Center + box.HalfExtent);
                ent.PositionComp.LocalAABB = aabb;
                const int defaultCollisionLayer = 15;
                ent.InitBoxPhysics(Definition.Material, box.Center, box.HalfExtent * 2, 0f, 0f, 0f, defaultCollisionLayer, RigidBodyFlag.RBF_STATIC);
                ent.Physics.Enabled = true;
                ent.RaisePhysicsChanged();
            }
            
            if (_destroyEnvItems)
                DestroyEnvItemsInternal();
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_BendyPhysicsComponent : MyObjectBuilder_EntityComponent
    {
    }
}