using System;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Bendy;
using Medieval.Entities.UseObject;
using Sandbox.ModAPI;
using VRage.Components.Entity;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Logging;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Definition
{
    [MyComponent(typeof(MyObjectBuilder_RailSegmentComponent))]
    [MyDefinitionRequired]
    [MyDependency(typeof(BendyComponent))]
    public class RailSegmentComponent : MyEntityComponent, IMyGenericUseObjectInterface
    {
        public RailSegmentDefinition Definition { get; private set; }
        private int[] _switchableTargets;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            Definition = (RailSegmentDefinition) definition;
        }

        private BendyComponent _bendy;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _bendy = Container.Get<BendyComponent>();
            Array.Resize(ref _switchableTargets, Definition.Switchables?.Length ?? 0);
            CommitSwitchState();
        }

        public bool IsSwitchedTo(Edge edge, float factor)
        {
            if (Definition.Switchables == null || Definition.Switchables.Length == 0)
                return true;

            const float switchingEpsilon = 0.1f;
            Node junction;
            Node alternate;
            if (factor < switchingEpsilon)
            {
                junction = edge.From;
                alternate = edge.To;
            }
            else if (factor >= 1 - switchingEpsilon)
            {
                junction = edge.To;
                alternate = edge.From;
            }
            else
                return true;

            for (var index = 0; index < Definition.Switchables.Length; index++)
            {
                var switchData = Definition.Switchables[index];
                var target = _switchableTargets[index];
                if (target < 0 || target >= switchData.Destinations.Length) continue;
                if (_bendy.Nodes[switchData.Intersection] == junction)
                    return _bendy.Nodes[switchData.Destinations[target]] == alternate;
            }

            return true;
        }

        public bool Switch(int switchable)
        {
            if (switchable < 0 || switchable >= Definition.Switchables.Length)
                return false;
            var data = Definition.Switchables[switchable];
            _switchableTargets[switchable] = (_switchableTargets[switchable] + 1) % data.Destinations.Length;
            CommitSwitchState();
            return true;
        }

        private static readonly MyStringHash SkinId = MyStringHash.GetOrCompute("Skin");

        private void CommitSwitchState()
        {
            for (var i = 0; i < _switchableTargets.Length; i++)
            {
                var key = MyStringId.GetOrCompute("switch_" + i + "_state");
                
                Container.Get<MyAnimationControllerComponent>()?.Variables?.SetValue(key, _switchableTargets[i]);
                var modelAttach = Container.Get<MyModelAttachmentComponent>();
                if (modelAttach == null)
                    continue;

                foreach (var attached in modelAttach?.GetAttachedEntities(SkinId))
                {
                    attached.Get<MyAnimationControllerComponent>()?.Variables?.SetValue(key, _switchableTargets[i]);
                    var set = attached.Get<MyModelAttachmentComponent>();
                    if (set!=null)
                        foreach (var e in set.GetAttachedEntities(MyStringHash.GetOrCompute("Handle")))
                            e.Render.NeedsDrawFromParent = true;
                }
            }
        }

        public override bool IsSerialized => _switchableTargets != null && _switchableTargets.Any(x => x >= 0);

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_RailSegmentComponent) base.Serialize(copy);
            ob.SwitchableTargets = _switchableTargets?.ToArray();
            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_RailSegmentComponent) builder;
            if (ob.SwitchableTargets == null)
                return;
            Array.Resize(ref _switchableTargets, ob.SwitchableTargets.Length);
            for (var i = 0; i < ob.SwitchableTargets.Length; i++)
                _switchableTargets[i] = ob.SwitchableTargets[i];
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
    public class MyObjectBuilder_RailSegmentComponent : MyObjectBuilder_EntityComponent
    {
        public int[] SwitchableTargets;
    }
}