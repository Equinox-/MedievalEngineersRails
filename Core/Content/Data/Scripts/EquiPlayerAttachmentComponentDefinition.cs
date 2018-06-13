using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;
using Medieval.Entities.UseObject;
using VRage;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity.UseObject;
using VRage.Library.Logging;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394
{
    [MyDefinitionType(typeof(MyObjectBuilder_EquiPlayerAttachmentComponentDefinition))]
    public class EquiPlayerAttachmentComponentDefinition : MyEntityComponentDefinition
    {
        public MyPositionAndOrientation Anchor { get; private set; }
        private AnimationDesc[] _animations;

        public MyStringHash ExitControl { get; private set; }

        public MyActionDescription EmptyActionDesc { get; private set; }
        public MyActionDescription OccupiedActionDesc { get; private set; }

        private readonly HashSet<string> _dummyNames = new HashSet<string>();

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiPlayerAttachmentComponentDefinition) def;
            Anchor = ob.Anchor;
            _animations = ob.Animations != null && ob.Animations.Length > 0
                ? ob.Animations.Select(x => new AnimationDesc(x)).ToArray()
                : null;

            ExitControl = MyStringHash.GetOrCompute(ob.ExitControl ??
                                                    MyObjectBuilder_EquiPlayerAttachmentComponentDefinition
                                                        .DefaultExitControl);

            EmptyActionDesc = (MyActionDescription) ob.EmptyAction;
            OccupiedActionDesc = (MyActionDescription) ob.OccupiedAction;
            _dummyNames.Clear();
            if (ob.DummyNames == null) return;
            foreach (var d in ob.DummyNames)
                if (!string.IsNullOrWhiteSpace(d))
                    _dummyNames.Add(d);
        }

        public bool AcceptDummy(MyUseObjectGeneric obj)
        {
//            return _dummyNames.Count == 0 || (obj.Dummy?.Name != null && _dummyNames.Contains(obj.Dummy.Name));
            return true; // TODO GET DEEPFLAME TO WHITELIST THINGS
        }

        public AnimationDesc? ByIndex(int index)
        {
            if (_animations == null || index < 0 || index >= _animations.Length)
                return null;
            return _animations[index];
        }

        public AnimationDesc? SelectAnimation(MyDefinitionId controller, float rand, out int index)
        {
            index = -1;
            if (_animations == null || _animations.Length == 0)
                return null;
            var totalWeight = 0f;
            foreach (var k in _animations)
                if (k.Accept(controller))
                    totalWeight += k.Weight;
            var rval = totalWeight * rand;
            for (var i = 0; i < _animations.Length; i++)
            {
                var k = _animations[i];
                if (!k.Accept(controller))
                    continue;
                totalWeight -= k.Weight;
                if (rval < totalWeight) continue;
                index = i;
                return k;
            }

            MyLog.Default.Warning(
                $"Failed to find animation for {controller}.  R={rand}, Opts={string.Join(", ", _animations.Select(x => x.ToString()))}");
            return null;
        }

        public struct AnimationDesc
        {
            public readonly MyStringId Start;
            public readonly MyStringId Stop;
            public readonly float Weight;
            public readonly bool Whitelist;
            public readonly HashSet<MyDefinitionId> CharacterFilter;

            public AnimationDesc(MyObjectBuilder_EquiPlayerAttachmentComponentDefinition.AnimationDesc desc)
            {
                Start = MyStringId.GetOrCompute(desc.Start);
                Stop = MyStringId.GetOrCompute(desc.Stop);
                Weight = desc.Weight;
                Whitelist = desc.Whitelist;
                if (desc.CharacterFilter == null || desc.CharacterFilter.Length == 0)
                {
                    CharacterFilter = null;
                    return;
                }

                CharacterFilter = new HashSet<MyDefinitionId>();
                foreach (var k in desc.CharacterFilter)
                    CharacterFilter.Add(k);
            }

            public bool Accept(MyDefinitionId id)
            {
                var has = CharacterFilter != null && CharacterFilter.Contains(id);
                return Whitelist ? has : !has;
            }

            public override string ToString()
            {
                var cf = CharacterFilter != null ? string.Join("||", CharacterFilter) : "empty";
                return $"AD[{Start.String}=>{Stop.String}, W={Weight}, W={Whitelist}, Fil={cf}]";
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiPlayerAttachmentComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [XmlElement("DummyName")] public string[] DummyNames;

        public MyPositionAndOrientation Anchor;
        public AnimationDesc[] Animations;

        public struct AnimationDesc
        {
            [XmlAttribute] public string Start;

            [XmlAttribute] public string Stop;

            [XmlAttribute] [DefaultValue(1)] public float Weight;

            [XmlAttribute] public bool Whitelist;

            public SerializableDefinitionId[] CharacterFilter;
        }

        public const string DefaultExitControl = "CharacterUse";

        [DefaultValue(DefaultExitControl)] public string ExitControl;

        public ActionDesc EmptyAction, OccupiedAction;

        public struct ActionDesc
        {
            [XmlAttribute] public string Text;

            [XmlAttribute] public string Icon;

            public static explicit operator MyActionDescription(ActionDesc d)
            {
                return new MyActionDescription
                {
                    Text = string.IsNullOrWhiteSpace(d.Text)
                        ? MyStringId.GetOrCompute("Use")
                        : MyStringId.GetOrCompute(d.Text),
                    Icon = d.Icon
                };
            }
        }
    }
}