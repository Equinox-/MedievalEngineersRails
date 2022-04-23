using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.Definitions;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Entity;
using VRage.Definitions;
using VRage.Entity.EntityComponents;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Collections;
using VRage.Models;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Voxel
{
    [MyComponent(typeof(MyObjectBuilder_RailGraderComponent))]
    [MyDefinitionRequired(typeof(RailGraderComponentDefinition))]
    [MyDependency(typeof(MyComponentEventBus), Critical = false)]
    [MyDependency(typeof(MyModelAttachmentComponent), Critical = false)]
    [MyDependency(typeof(MyModelComponent), Critical = false)]
    [MyDependency(typeof(MyEntityStateComponent), Critical = false)]
    public class RailGraderComponent : MyEntityComponent, IMyComponentEventProvider
    {
        public const string EventDidWork = "RailGraderComponent_DidWork";

        private readonly PowerObserver _powerObserver = new PowerObserver();
        private readonly InventoryObserver _inventoryObserver = new InventoryObserver();

        [Automatic]
        private readonly MyPositionComponentBase _positionComponent = null;

        [Automatic]
        private readonly MyModelAttachmentComponent _modelAttachmentComponent = null;

        [Automatic]
        private readonly MyModelComponent _modelComponent = null;

        [Automatic]
        private readonly MyComponentEventBus _eventBus = null;

        [Automatic]
        private readonly MyEntityStateComponent _state = null;

        public RailGraderComponent()
        {
            _powerObserver.PoweredChanged += (oldState, newState) => ScheduleUpdate();
            _inventoryObserver.InventoryChanged += ScheduleUpdate;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            _powerObserver.OnAddedToContainer(Container);
            _inventoryObserver.OnAddedToContainer(Container);
            _positionComponent.OnPositionChanged += OnPositionChanged;
            if (_modelComponent != null)
                _modelComponent.ModelChanged += OnModelChanged;
            if (_modelAttachmentComponent != null)
            {
                _modelAttachmentComponent.OnEntityAttached += OnEntityAttached;
                _modelAttachmentComponent.OnEntityDetached += OnEntityDetached;
                if (Definition.DummyAttachment != MyStringHash.NullOrEmpty)
                    foreach (var attached in _modelAttachmentComponent.GetAttachedEntities(Definition.DummyAttachment))
                        OnEntityAttached(_modelAttachmentComponent, attached);
            }
        }

        public override void OnRemovedFromScene()
        {
            if (MyMultiplayerModApi.Static.IsServer)
            {
                _powerObserver.OnRemovedFromContainer();
                _inventoryObserver.OnRemovedFromContainer();
                _positionComponent.OnPositionChanged -= OnPositionChanged;
                if (_modelComponent != null)
                    _modelComponent.ModelChanged -= OnModelChanged;
                if (_modelAttachmentComponent != null)
                {
                    _modelAttachmentComponent.OnEntityAttached -= OnEntityAttached;
                    _modelAttachmentComponent.OnEntityDetached -= OnEntityDetached;
                    if (Definition.DummyAttachment != MyStringHash.NullOrEmpty)
                        foreach (var attached in _modelAttachmentComponent.GetAttachedEntities(Definition.DummyAttachment))
                            OnEntityDetached(_modelAttachmentComponent, attached);
                }
            }

            base.OnRemovedFromScene();
        }

        private void OnEntityAttached(MyModelAttachmentComponent component, MyEntity entity)
        {
            if (Definition.DummyAttachment != component.GetEntityAttachmentPoint(entity)) return;
            entity.PositionComp.OnPositionChanged += OnPositionChanged;
            var model = entity.Get<MyModelComponent>();
            if (model != null) model.ModelChanged += OnModelChanged;
        }

        private void OnEntityDetached(MyModelAttachmentComponent component, MyEntity entity)
        {
            entity.PositionComp.OnPositionChanged -= OnPositionChanged;
            var model = entity.Get<MyModelComponent>();
            if (model != null) model.ModelChanged -= OnModelChanged;
        }

        private void OnPositionChanged(MyPositionComponentBase obj) => ScheduleUpdate();

        private void OnModelChanged(MyModelComponent.ModelChangedArgs modelChangedArgs) => ScheduleUpdate();

        public RailGraderComponentDefinition Definition { get; private set; }

        private bool _updateScheduled;
        private readonly VoxelPlacementBuffer _placementBuffer = new VoxelPlacementBuffer();
        private readonly VoxelMiningBuffer _miningBuffer = new VoxelMiningBuffer();

        private void ScheduleUpdate()
        {
            if (!Entity.InScene || !MyMultiplayerModApi.Static.IsServer) return;
            if (_updateScheduled) return;
            Scheduler.AddScheduledCallback(ExecuteUpdate, MyEngineConstants.UPDATE_STEP_SIZE_IN_MILLISECONDS * 2);
            _updateScheduled = true;
        }

        [Update(false)]
        private void ExecuteUpdate(long dt)
        {
            _updateScheduled = false;
            if (!_powerObserver.IsPowered) return;
            if (Definition.RequiredStates.Count > 0 && _state != null && !Definition.RequiredStates.Contains(_state.CurrentState)) return;

            using (PoolManager.Get(out List<RailGradeComponent> gradeComponents))
            {
                var pos = GetDummyPosition();
                if (!pos.HasValue) return;
                var position = pos.Value;
                RailGraderSystem.GatherGradeComponents(position, gradeComponents, Definition.Radius);
                if (gradeComponents.Count == 0) return;

                var availableForDeposit = Math.Min((uint) ComputeDepositAvailability(), Definition.FillVolume);
                if (availableForDeposit == 0)
                    return;
                var availableForExcavate = Definition.ExcavateVolume;

                var system = MySession.Static.Components.Get<RailGraderSystem>();
                var modified = system.DoGrading(gradeComponents, position, Definition.Radius,
                    availableForDeposit,
                    availableForExcavate, _miningBuffer, Definition.FillMaterial.Material.Index,
                    out var deposited, out var excavated, out _, out _,
                    out var dynamicEntities, out var voxelEntity, out var voxelRadius);

                if (!modified) return;
                _eventBus?.Invoke(EventDidWork, true);
                UseDepositMaterials((int) deposited);
                GiftMiningMaterials();
                system.RaiseDoGrade(gradeComponents, position, voxelRadius, availableForDeposit, availableForExcavate,
                    Definition.FillMaterial.Material.Index, excavated, deposited, voxelEntity.Id, dynamicEntities);
            }
        }

        private int ComputeDepositAvailability()
        {
            if (MyAPIGateway.Session.CreativeMode)
                return int.MaxValue;
            if (string.IsNullOrEmpty(Definition.SourceInventory.String))
                return _placementBuffer.AvailableVolume(_inventoryObserver.Inventories.Values.GetEnumerator(), Definition.FillMaterial);
            // ReSharper disable once ConvertIfStatementToReturnStatement
            if (_inventoryObserver.Inventories.TryGetValue(Definition.SourceInventory, out var inv))
                return _placementBuffer.AvailableVolume(inv, Definition.FillMaterial);
            return 0;
        }

        private void UseDepositMaterials(int amount)
        {
            if (MyAPIGateway.Session.CreativeMode)
                return;
            if (string.IsNullOrEmpty(Definition.SourceInventory.String))
                _placementBuffer.ConsumeVolume(_inventoryObserver.Inventories.Values.GetEnumerator(), Definition.FillMaterial, amount);
            if (_inventoryObserver.Inventories.TryGetValue(Definition.SourceInventory, out var inv))
                _placementBuffer.ConsumeVolume(inv, Definition.FillMaterial, amount);
        }

        private void GiftMiningMaterials()
        {
            var def = Definition.ExcavateDefinition;
            if (def == null) return;
            if (string.IsNullOrEmpty(Definition.DestinationInventory.String))
            {
                foreach (var inv in _inventoryObserver.Inventories.Values)
                    _miningBuffer.PutMaterialsInto(inv, def);
            }
            else if (_inventoryObserver.Inventories.TryGetValue(Definition.DestinationInventory, out var inv))
            {
                _miningBuffer.PutMaterialsInto(inv, def);
            }
        }

        private Vector3D? GetDummyPosition()
        {
            if (Entity == null || !Entity.InScene)
                return null;
            var matrix = Entity.WorldMatrix;
            IMyModel model = null;
            // ReSharper disable once InvertIf
            if (Definition.DummyAttachment != MyStringHash.NullOrEmpty && _modelAttachmentComponent != null)
            {
                var attached = _modelAttachmentComponent.GetAttachedEntities(Definition.DummyAttachment);
                foreach (var ent in attached)
                {
                    var attachedModel = ent?.Model;
                    if (attachedModel != null)
                    {
                        model = attachedModel;
                        matrix = ent.WorldMatrix;
                        break;
                    }
                }
            }
            else
                model = _modelComponent?.Model;

            if (model == null)
                return null;
            return model.TryGetDummy(Definition.DummyName, out var dummy) ? Vector3D.Transform(dummy.Matrix.Translation, in matrix) : matrix.Translation;
        }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (RailGraderComponentDefinition) def;
            _powerObserver.RequiredPower = Definition.RequiredPower;
        }

        public bool HasEvent(string eventName) => eventName == EventDidWork;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailGraderComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_RailGraderComponentDefinition))]
    public class RailGraderComponentDefinition : MyEntityComponentDefinition
    {
        public PowerObserver.RequiredPowerEnum RequiredPower;
        public HashSetReader<MyStringHash> RequiredStates { get; private set; }

        public MyStringHash DummyAttachment { get; private set; }
        public string DummyName { get; private set; }

        public MyStringHash SourceInventory { get; private set; }
        public MyStringHash DestinationInventory { get; private set; }
        public float Radius { get; private set; }

        public MyVoxelMiningDefinition ExcavateDefinition { get; private set; }
        public uint ExcavateVolume { get; private set; }

        public VoxelPlacementBuffer.VoxelPlacementDefinition FillMaterial { get; private set; }
        public uint FillVolume { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_RailGraderComponentDefinition) def;

            var spec = new MyDummySpec(ob.Dummy);
            DummyAttachment = MyStringHash.GetOrCompute(spec.SubpartName);
            DummyName = spec.Name;

            RequiredPower = ob.RequiredPower ?? PowerObserver.RequiredPowerEnum.None;
            if (ob.RequiredStates != null && ob.RequiredStates.Length > 0)
            {
                var requiredStates = new HashSet<MyStringHash>();
                foreach (var required in ob.RequiredStates)
                    requiredStates.Add(MyStringHash.GetOrCompute(required));
                RequiredStates = requiredStates;
            }
            else
                RequiredStates = default;

            ExcavateDefinition = ob.ExcavateVolume > 0 && ob.Radius > 0
                ? Assert.Definition<MyVoxelMiningDefinition>(ob.ExcavateDefinition, $"For rail grader behavior {def.Id}'s excavate definition")
                : null;
            ExcavateVolume = ob.ExcavateVolume ?? 1024;

            FillMaterial = new VoxelPlacementBuffer.VoxelPlacementDefinition(ob.FillMaterial, Log);
            FillVolume = ob.FillVolume ?? 1024;
            SourceInventory = MyStringHash.GetOrCompute(ob.SourceInventory);
            DestinationInventory = MyStringHash.GetOrCompute(ob.DestinationInventory);

            Radius = ob.Radius ?? 2;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailGraderComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [XmlElement("RequiredPower")]
        public PowerObserver.RequiredPowerEnum? RequiredPower;

        [XmlElement("RequiredState")]
        public string[] RequiredStates;

        [XmlElement("SourceInventory")]
        public string SourceInventory;

        [XmlElement("DestInventory")]
        public string DestinationInventory;

        public string Dummy;
        public float? Radius;

        public SerializableDefinitionId? ExcavateDefinition;
        public uint? ExcavateVolume;

        public MyObjectBuilder_VoxelMiningDefinition.MiningDef FillMaterial;
        public uint? FillVolume;
    }
}