using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Bendy;
using Medieval.Entities.UseObject;
using VRage.Components;
using VRage.Components.Entity;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394.RailSystem.Definition
{
    [MyComponent(typeof(MyObjectBuilder_RailSwitchInternalComponent))]
    [MyDefinitionRequired(typeof(RailSwitchInternalComponentDefinition))]
    [MyDependency(typeof(BendyComponent), Critical = true)]
    [MyDependency(typeof(RailSegmentComponent), Critical = true)]
    public class RailSwitchInternalComponent : MyEntityComponent, IMyGenericUseObjectInterface, IRailSwitch
    {
        private RailSegmentComponent _segment;
        private BendyComponent _bendy;

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            _segment = Container.Get<RailSegmentComponent>();
            _bendy = Container.Get<BendyComponent>();
        }

        public override void OnBeforeRemovedFromContainer()
        {
            _segment = null;
            _bendy = null;
            base.OnBeforeRemovedFromContainer();
        }

        private SwitchableNodeSide[] _controllers;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (Definition.Switchables != null && Definition.Switchables.Length > 0)
                Array.Resize(ref _controllers, Definition.Switchables?.Length ?? 0);
            _bendy.EdgeSetupChanged += FillSwitchableControllers;
            FillSwitchableControllers(_bendy);
        }

        public override void OnRemovedFromScene()
        {
            EmptySwitchableControllers();
            base.OnRemovedFromScene();
        }

        public RailSwitchInternalComponentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (RailSwitchInternalComponentDefinition) def;
        }

        private void EmptySwitchableControllers()
        {
            if (_controllers == null)
                return;
            for (var index = 0; index < _controllers.Length; index++)
            {
                var k = _controllers[index];
                if (k != null)
                    k.SwitchChanged -= OnSwitchChanged;
                _controllers[index] = null;
            }
        }

        private void OnSwitchChanged(SwitchableNodeSide self, Node junction, Node destination)
        {
            UpdateAnimator();
        }

        private void FillSwitchableControllers(BendyComponent bc)
        {
            EmptySwitchableControllers();
            for (var i = 0; i < Definition.Switchables.Length; i++)
            {
                var config = Definition.Switchables[i];
                var junction = _bendy.Nodes[config.Intersection];
                var target = _bendy.Nodes[config.Destinations[0]];
                if (junction == null || target == null)
                    continue;
                _controllers[i] = SwitchableNodeData.GetOrCreate(junction).SideOrCreateFor(target);
                _controllers[i].SwitchChanged += OnSwitchChanged;
            }

            UpdateAnimator();
        }

        public void Switch()
        {
            for (var i = 0; i < Definition.Switchables.Length; i++)
                Switch(i);
        }

        public void Switch(int switchable)
        {
            if (switchable < 0 || switchable >= Definition.Switchables.Length)
                return;
            _controllers[switchable].SwitchRelative(1);
        }

        private static readonly MyStringHash SkinId = MyStringHash.GetOrCompute("Skin");

        private void UpdateAnimator()
        {
            if (_controllers == null)
                return;
            for (var i = 0; i < Math.Min(Definition.Switchables.Length, _controllers.Length); i++)
            {
                var key = MyStringId.GetOrCompute("switch_" + i + "_state");
                var def = Definition.Switchables[i];
                var animatedDestination = 0;
                if (_controllers[i] != null)
                    for (var j = 0; j < def.Destinations.Length; j++)
                        if (_controllers[i].Target == _bendy.Nodes[def.Destinations[j]])
                        {
                            animatedDestination = j;
                            break;
                        }

                Container.Get<MyAnimationControllerComponent>()?.Variables?.SetValue(key, animatedDestination);
                var modelAttach = Container.Get<MyModelAttachmentComponent>();
                if (modelAttach == null)
                    continue;

                foreach (var attached in modelAttach?.GetAttachedEntities(SkinId))
                {
                    attached.Get<MyAnimationControllerComponent>()?.Variables?.SetValue(key, animatedDestination);
                    var set = attached.Get<MyModelAttachmentComponent>();
                    if (set == null)
                        continue;
                    foreach (var e in set.GetAttachedEntities(MyStringHash.GetOrCompute("Handle")))
                        e.Render.NeedsDrawFromParent = true;
                }
            }
        }

        public void Use(string dummyName, UseActionEnum actionEnum, MyEntity user)
        {
            if (actionEnum != UseActionEnum.Manipulate)
                return;
            var switchable = Definition.FindSwitchableByDummy(dummyName);
            if (!switchable.HasValue)
                return;
            Switch(switchable.Value);
        }

        public MyActionDescription GetActionInfo(string dummyName, UseActionEnum actionEnum)
        {
            if (actionEnum != UseActionEnum.Manipulate)
                return default(MyActionDescription);
            var switchable = Definition.FindSwitchableByDummy(dummyName);
            // ReSharper disable once ConvertIfStatementToReturnStatement
            if (switchable.HasValue)
                return new MyActionDescription {Text = MyStringId.GetOrCompute("Switch")};
            return default(MyActionDescription);
        }

        public UseActionEnum SupportedActions => PrimaryAction;
        public UseActionEnum PrimaryAction => Definition.Switchables.Length > 0 ? UseActionEnum.Manipulate : UseActionEnum.None;
        public UseActionEnum SecondaryAction => UseActionEnum.None;
        public bool ContinuousUsage => false;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailSwitchInternalComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_RailSwitchInternalComponentDefinition))]
    public class RailSwitchInternalComponentDefinition : MyEntityComponentDefinition
    {
        public struct RailSegmentSwitchable
        {
            public readonly int Intersection;
            public readonly int[] Destinations;
            public readonly string Dummy;

            internal RailSegmentSwitchable(MyObjectBuilder_RailSwitchInternalComponentDefinition.RsObRailSwitchable ob)
            {
                Intersection = ob.Intersection;
                Destinations = ob.Destinations ?? new int[0];
                Dummy = ob.Dummy;
            }
        }

        private readonly Dictionary<string, int> _switchablesByDummy = new Dictionary<string, int>();
        public RailSegmentSwitchable[] Switchables { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_RailSwitchInternalComponentDefinition) def;


            _switchablesByDummy.Clear();
            if (ob.Switchables == null || ob.Switchables.Length == 0)
            {
                Switchables = new RailSegmentSwitchable[0];
            }
            else
            {
                Switchables = ob.Switchables.Select(x => new RailSegmentSwitchable(x)).ToArray();
                for (var index = 0; index < Switchables.Length; index++)
                {
                    var k = Switchables[index];
                    if (string.IsNullOrWhiteSpace(k.Dummy))
                    {
                        MyDefinitionErrors.Add(ob.Package, $"Switchable has no dummy name, will not be interactable", LogSeverity.Warning);
                        continue;
                    }

                    if (_switchablesByDummy.ContainsKey(k.Dummy))
                    {
                        MyDefinitionErrors.Add(ob.Package, $"Switchable has duplicated dummy name, will not operate", LogSeverity.Error);
                        continue;
                    }

                    _switchablesByDummy.Add(k.Dummy, index);
                }
            }
        }


        public int? FindSwitchableByDummy(string dummy)
        {
            int k;
            if (_switchablesByDummy.TryGetValue(dummy, out k))
                return k;
            return null;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailSwitchInternalComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [XmlElement("Switchable")]
        public RsObRailSwitchable[] Switchables;

        public class RsObRailSwitchable
        {
            [XmlAttribute("Dummy")]
            public string Dummy;

            [XmlElement("Intersection")]
            public int Intersection;

            [XmlElement("Destination")]
            public int[] Destinations;
        }
    }
}