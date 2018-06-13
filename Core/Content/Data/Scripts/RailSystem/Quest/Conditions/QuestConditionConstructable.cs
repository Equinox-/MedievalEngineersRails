using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Construction;
using Equinox76561198048419394.RailSystem.Util;
using Medieval.Definitions.Quests.Conditions;
using Medieval.Entities.Components.Quests.Conditions;
using Medieval.ObjectBuilders.Components.Quests.Conditions;
using Medieval.ObjectBuilders.Definitions.Quests.Conditions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.ObjectBuilder;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.RailSystem.Quest.Conditions
{
    [MyQuestConditionType(typeof(MyObjectBuilder_QuestConditionConstructable))]
    public class QuestConditionConstructable : MyQuestConditionBase
    {
        private new QuestConditionConstructableDefinition Definition => (QuestConditionConstructableDefinition) m_definition;

        public override void Deserialize(MyObjectBuilder_QuestConditionBase builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_QuestConditionConstructable) builder;
            _amountConstructed = ob.AmountConstructed;
        }

        public override MyObjectBuilder_QuestConditionBase Serialize()
        {
            var ob = (MyObjectBuilder_QuestConditionConstructable) base.Serialize();
            ob.AmountConstructed = _amountConstructed;
            return ob;
        }

        protected override void OnActivate()
        {
            base.OnActivate();
            if (!MyAPIGateway.Session.IsServerDecider())
                return;

            var definition = (QuestConditionConstructableDefinition) m_definition;
            if (definition.Amount == 0 || MyAPIGateway.Session.IsCreative())
            {
                _amountConstructed = definition.Amount;
                MarkAsCompleted();
                return;
            }

            ConstructorBehavior.OnConstructed += OnConstructed;
        }

        protected override void OnDeactivate()
        {
            base.OnDeactivate();
            if (!MyAPIGateway.Session.IsServerDecider())
                return;

            ConstructorBehavior.OnConstructed -= OnConstructed;
        }

        private void OnConstructed(ConstructorBehavior behavior, ConstructableComponent target, float before, float after)
        {
            if (Owner?.Entity == null || behavior.Owner != Owner.Entity || target.Entity?.DefinitionId == null)
                return;

            if (Definition.AllowedEntities.Count > 0 && !Definition.AllowedEntities.Contains(target.Entity.DefinitionId.Value))
                return;

            var tri = Definition.BuildLevel * target.Definition.MaxIntegrity;
            if (Definition.Deconstruct)
            {
                if (before > tri && after <= tri)
                    _amountConstructed++;
            }
            else
            {
                if (before < tri && after >= tri)
                    _amountConstructed++;
            }

            if (_amountConstructed >= Definition.Amount)
                MarkAsCompleted();
            else
                MarkDirty();
        }

        public static string DescContainer(MyDefinitionId id)
        {
            var cc = MyDefinitionManager.Get<MyContainerDefinition>(id);
            return cc?.DisplayNameOf() ?? id.SubtypeName;
        }

        public override List<string> GetDescription()
        {
            if (IsCompleted)
                return new List<string>(Definition.GetCompletedDescription());

            List<string> description = base.GetDescription();
            var def = Definition;
            var verb = def.Deconstruct ? "Deconstruct" : "Construct";
            string condition;
            if (def.Deconstruct)
                condition = def.BuildLevel > 0 ? $"to below {def.BuildLevel * 100:F0}%" : "completely";
            else
                condition = def.BuildLevel < 1 ? $"to above {def.BuildLevel * 100:F0}%" : "completely";

            if (def.AllowedEntities.Count == 1)
                description.Add($"{verb} {def.Amount} {DescContainer(def.AllowedEntities.First())} {condition} ({_amountConstructed}/{def.Amount})");
            else if (def.AllowedEntities.Count > 1)
                description.Add(
                    $"{verb} {def.Amount} objects from this list {condition}: {string.Join(", ", def.AllowedEntities.Select(x => DescContainer(x)))} ({_amountConstructed}/{def.Amount})");
            else
                description.Add($"{verb} {def.Amount} objects {condition} ({_amountConstructed}/{def.Amount})");

            return description;
        }

        private int _amountConstructed;
    }

    [MyObjectBuilderDefinition(null)]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_QuestConditionConstructable : MyObjectBuilder_QuestConditionBase
    {
        public int AmountConstructed;
    }

    [MyQuestConditionDefinitionType(typeof(MyObjectBuilder_QuestConditionConstructableDefinition))]
    public class QuestConditionConstructableDefinition : MyQuestConditionBaseDefinition
    {
        public override MyObjectBuilderType SerializedTypeId => typeof(MyObjectBuilder_QuestConditionConstructable);

        public int Amount { get; private set; }
        public float BuildLevel { get; private set; }
        public bool Deconstruct { get; private set; }
        public HashSet<SerializableDefinitionId> AllowedEntities { get; private set; }

        public override void Init(MyObjectBuilder_QuestConditionBaseDefinition builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_QuestConditionConstructableDefinition) builder;
            var s = new HashSet<SerializableDefinitionId>();
            if (ob.AllowedEntities != null)
                foreach (var a in ob.AllowedEntities)
                    s.Add(a);
            BuildLevel = ob.BuildLevel;
            Deconstruct = ob.Deconstruct;
            AllowedEntities = s;
            Amount = ob.Amount;
        }

        public override List<string> GetCompletedDescription()
        {
            List<string> description = base.GetCompletedDescription();
            var def = this;
            var verb = def.Deconstruct ? "Deconstructed" : "Constructed";
            string condition;
            if (def.Deconstruct)
                condition = def.BuildLevel > 0 ? $"to below {def.BuildLevel * 100:F0}%" : "completely";
            else
                condition = def.BuildLevel < 1 ? $"to above {def.BuildLevel * 100:F0}%" : "completely";

            if (def.AllowedEntities.Count == 1)
                description.Add($"{verb} {def.Amount} {QuestConditionConstructable.DescContainer(def.AllowedEntities.First())} {condition}");
            else if (def.AllowedEntities.Count > 1)
                description.Add(
                    $"{verb} {def.Amount} objects from this list {condition}: {string.Join(", ", def.AllowedEntities.Select(x => QuestConditionConstructable.DescContainer(x)))}");
            else
                description.Add($"{verb} {def.Amount} objects {condition}");

            return description;
        }
    }

    [MyObjectBuilderDefinition(null)]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_QuestConditionConstructableDefinition : MyObjectBuilder_QuestConditionBaseDefinition
    {
        [XmlAttribute]
        public int Amount;

        [XmlAttribute]
        public float BuildLevel;

        [XmlAttribute]
        public bool Deconstruct;

        [XmlElement("Include")]
        public SerializableDefinitionId[] AllowedEntities;
    }
}