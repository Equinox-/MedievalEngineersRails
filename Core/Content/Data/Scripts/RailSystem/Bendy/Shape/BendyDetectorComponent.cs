using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Construction;
using Sandbox.Engine.Physics;
using Sandbox.ModAPI;
using VRage;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy.Shape
{
    [MyComponent(typeof(MyObjectBuilder_BendyDetectorComponent))]
    [MyDependency(typeof(BendyComponent), Critical = true, Recursive = true)]
    [MyDependency(typeof(ConstructableComponent), Critical = false, Recursive = false)]
    [MyDefinitionRequired]
    public class BendyDetectorComponent : BendyShapeComponent
    {
        private static bool DebugAsChildren = false;

        public new BendyDetectorComponentDefinition Definition { get; private set; }
        private readonly List<MyEntity> _physicsProxies = new List<MyEntity>();

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            Definition = (BendyDetectorComponentDefinition) definition;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
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

                const int NoCollisionLayer = 19;
                ent.InitBoxPhysics(MyStringHash.GetOrCompute("Wood"), box.Center, box.HalfExtent * 2, 0f, 0f, 0f, NoCollisionLayer, RigidBodyFlag.RBF_STATIC | RigidBodyFlag.RBF_DISABLE_COLLISION_RESPONSE);
                ent.Physics.Enabled = true;
                ent.RaisePhysicsChanged();
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_BendyDetectorComponent : MyObjectBuilder_EntityComponent
    {
    }
}