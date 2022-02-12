using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Controller;
using Equinox76561198048419394.RailSystem.Util;
using Medieval.Definitions.Physics;
using Medieval.Entities.Components.Mechanical;
using Medieval.Entities.UseObject;
using Medieval.ObjectBuilders.Components.Mechanical;
using VRage;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Entity;
using VRage.Components.Entity.CubeGrid;
using VRage.Components.Physics;
using VRage.Components.Session;
using VRage.Entity.EntityComponents;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Logging;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Scene;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Physics
{
    [MyComponent(typeof(MyObjectBuilder_CouplerComponent))]
    [MyDefinitionRequired(typeof(CouplerComponentDefinition))]
    [MyDependency(typeof(MyModelComponent), Critical = true)]
    [MyDependency(typeof(MyModelAttachmentComponent), Critical = false)]
    public class CouplerComponent : MyBreakableConstraintAttachmentComponent, IMyComponentEventProvider, IMyGenericUseObjectInterfaceFiltered
    {
        [Automatic]
        private readonly MyComponentEventBus _eventBus;

        public const string CoupledEvent = "Coupler_Coupled";
        public const string UncoupledEvent = "Coupler_Uncoupled";
        private static readonly HashSet<string> Events = new HashSet<string> { CoupledEvent, UncoupledEvent };

        [Automatic]
        private readonly MyModelComponent _model;

        private MyTransform _attachmentTransform = MyTransform.Identity;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _model.ModelChanged += OnModelChanged;
            OnModelChanged(default);
        }

        public override void OnRemovedFromScene()
        {
            _model.ModelChanged -= OnModelChanged;
            base.OnRemovedFromScene();
        }

        private void OnModelChanged(MyModelComponent.ModelChangedArgs change)
        {
            var model = _model.Model;
            if (model == null) return;
            foreach (var dummy in model.Dummies)
                if (dummy.Name.StartsWith(Definition.AttachmentPoint))
                {
                    _attachmentTransform = new MyTransform(dummy.Matrix);
                    return;
                }
        }

        public CouplerComponent ConnectedTo
        {
            get
            {
                if (Entity == null || Scene == null) return null;
                var group = this.GetGroup();
                if (group == null) return null;
                var neighbors = group.GetNeighbors(Entity);
                using (var enumerator = neighbors.GetEnumerator())
                {
                    return (enumerator.MoveNext() ? enumerator.Current : null)?.Get<CouplerComponent>();
                }
            }
            set
            {
                if (Scene == null || !MyMultiplayer.IsServer) return;
                this.Groups().Disconnect((MyConstraintAttachmentComponent)this);
                if (value == null) return;
                var constraint = MyConstraint.Factory.CreateInstance<MyRopeConstraint>(Definition.RopeDefinition);
                constraint.MaxRopeLength = Definition.Distance;
                // ReSharper disable RedundantCast
                this.Groups().Link((MyConstraintAttachmentComponent)this, (MyConstraintAttachmentComponent)value, (MyConstraint)constraint);
                // ReSharper restore RedundantCast
            }
        }

        public CouplerComponentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (CouplerComponentDefinition)def;
        }

        public override MyTransform GetAttachmentTransform(MyConstraintAttachmentComponent connection) => _attachmentTransform;

        protected override void OnConstraintActivate(MyConstraint constraint)
        {
            base.OnConstraintActivate(constraint);
            _eventBus?.Invoke(CoupledEvent);
        }

        protected override void OnConstraintDeactivate(MyConstraint constraint)
        {
            _eventBus?.Invoke(UncoupledEvent);
            base.OnConstraintDeactivate(constraint);
        }

        public new bool HasEvent(string eventName) => base.HasEvent(eventName) || Events.Contains(eventName);

        public void Use(string dummyName, UseActionEnum actionEnum, MyEntity user)
        {
            if (!MyMultiplayer.IsServer) return;
            if (ConnectedTo != null)
            {
                ConnectedTo = null;
                return;
            }

            ConnectedTo = GetPossibleConnection();
        }

        private CouplerComponent GetPossibleConnection()
        {
            // Buffer by 2 meters to deal with attachment points outside of the block cell
            var query = new BoundingSphereD(GetAttachmentPositionWorld(), Definition.AttachDistance + 2);
            using (PoolManager.Get(out List<MyEntity> rootEntities))
            using (PoolManager.Get(out List<MyEntity> blocks))
            {
                MyGamePruningStructure.GetTopMostEntitiesInSphere(in query, rootEntities);
                CouplerComponent candidate = null;
                var candidateDistance = double.PositiveInfinity;
                foreach (var entity in rootEntities)
                {
                    if (entity == Entity.Parent || !entity.Components.TryGet(out MyGridHierarchyComponent gridHierarchyComponent)) continue;
                    blocks.Clear();
                    gridHierarchyComponent.QuerySphere(in query, blocks);
                    foreach (var block in blocks)
                    {
                        if (block == Entity || !block.Components.TryGet(out CouplerComponent component)) continue;
                        if (!component.Definition.CanConnectTo(Definition)) continue;
                        var dist = Vector3D.DistanceSquared(component.GetAttachmentPositionWorld(), query.Center);
                        if (dist >= candidateDistance) continue;
                        candidateDistance = dist;
                        candidate = component;
                    }
                }

                return candidateDistance >= Definition.AttachDistance * Definition.AttachDistance ? null : candidate;
            }
        }

        public MyActionDescription GetActionInfo(string dummyName, UseActionEnum actionEnum)
        {
            if (ConnectedTo != null) return Definition.UncoupleAction;
            var other = GetPossibleConnection();
            return other != null ? Definition.CoupleAction : Definition.CantCoupleAction;
        }

        public UseActionEnum SupportedActions => UseActionEnum.Manipulate;
        public UseActionEnum PrimaryAction => UseActionEnum.Manipulate;
        public UseActionEnum SecondaryAction => UseActionEnum.None;
        public bool ContinuousUsage => false;
        public bool AppliesTo(string dummyName) => dummyName.StartsWith(Definition.InteractionDummy);
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_CouplerComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_CouplerComponentDefinition))]
    [MyDependency(typeof(MyRopeConstraintDefinition))]
    public class CouplerComponentDefinition : MyEntityComponentDefinition
    {
        public MyRopeConstraintDefinition RopeDefinition { get; private set; }
        public float Distance { get; private set; }
        public float AttachDistance { get; private set; }

        public string AttachmentPoint { get; private set; }

        public string InteractionDummy { get; private set; }

        public HashSetReader<string> ConnectionTags { get; private set; }

        public MyActionDescription CoupleAction { get; private set; }

        public MyActionDescription UncoupleAction { get; private set; }

        public MyActionDescription CantCoupleAction { get; private set; }

        public bool CanConnectTo(CouplerComponentDefinition other)
        {
            foreach (var tag in ConnectionTags)
                if (other.ConnectionTags.Contains(tag))
                    return true;
            return false;
        }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_CouplerComponentDefinition)def;

            RopeDefinition = MyDefinitionManager.Get<MyRopeConstraintDefinition>(ob.RopeDefinition ??
                                                                                 new MyDefinitionId(typeof(MyObjectBuilder_RopeConstraint), "BasicRope"));
            if (RopeDefinition == null)
                MyDefinitionErrors.Add(ob.Package, $"Rope definition missing on {Id}", LogSeverity.Critical);
            Distance = ob.Distance ?? 0.25f;
            AttachDistance = ob.AttachDistance ?? Distance * 2;
            AttachmentPoint = ob.AttachmentPoint ?? "detector_rope";
            InteractionDummy = ob.InteractionDummy ?? "detector_generic";
            CoupleAction = (MyActionDescription)(ob.CoupleAction ??
                                                 new MyObjectBuilder_EquiPlayerAttachmentComponentDefinition.ActionDesc { Text = "Couple" });
            UncoupleAction = (MyActionDescription)(ob.UncoupleAction ??
                                                   new MyObjectBuilder_EquiPlayerAttachmentComponentDefinition.ActionDesc { Text = "Uncouple" });
            CantCoupleAction = (MyActionDescription)(ob.CantCoupleAction ??
                                                     new MyObjectBuilder_EquiPlayerAttachmentComponentDefinition.ActionDesc { Text = "No Nearby Couplers" });

            ConnectionTags = new HashSet<string>(ob.ConnectionTag ?? new[] { ob.Id.SubtypeName });
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_CouplerComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public SerializableDefinitionId? RopeDefinition;
        public float? Distance;
        public float? AttachDistance;

        public string AttachmentPoint;

        public string InteractionDummy;

        public string[] ConnectionTag;

        public MyObjectBuilder_EquiPlayerAttachmentComponentDefinition.ActionDesc? CoupleAction;
        public MyObjectBuilder_EquiPlayerAttachmentComponentDefinition.ActionDesc? UncoupleAction;
        public MyObjectBuilder_EquiPlayerAttachmentComponentDefinition.ActionDesc? CantCoupleAction;
    }
}