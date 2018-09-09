using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Bendy.Planner;
using Equinox76561198048419394.RailSystem.Util;
using Medieval.Definitions.Quests.Conditions;
using Medieval.Entities.Components.Quests.Conditions;
using Medieval.ObjectBuilders.Components.Quests.Conditions;
using Medieval.ObjectBuilders.Definitions.Quests.Conditions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ObjectBuilder;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.RailSystem.Quest.Conditions
{
    [MyQuestConditionType(typeof(MyObjectBuilder_QuestConditionPlanBendy))]
    public class QuestConditionPlanBendy : MyQuestConditionBase
    {
        private new QuestConditionPlanBendyDefinition Definition => (QuestConditionPlanBendyDefinition) m_definition;

        public override void Deserialize(MyObjectBuilder_QuestConditionBase builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_QuestConditionPlanBendy) builder;
            _amountPlanned = ob.AmountPlanned;
        }

        public override MyObjectBuilder_QuestConditionBase Serialize()
        {
            var ob = (MyObjectBuilder_QuestConditionPlanBendy) base.Serialize();
            ob.AmountPlanned = _amountPlanned;
            return ob;
        }

        protected override void OnActivate()
        {
            base.OnActivate();
            if (!MyAPIGateway.Session.IsServerDecider())
                return;

            if (Definition.Amount == 0 || this.Player().IsCreative())
            {
                _amountPlanned = Definition.Amount;
                MarkAsCompleted();
                return;
            }

            EdgePlacerSystem.EntityAdded += EdgePlacerBehaviorOnEntityAdded;
        }

        private void EdgePlacerBehaviorOnEntityAdded(MyEntity holderEntity, IMyPlayer holderPlayer, MyEntity modifiedEntity)
        {
            if (Owner?.Entity == null || holderEntity != Owner.Entity || !modifiedEntity.DefinitionId.HasValue)
                return;

            if (Definition.AllowedEntities != null && Definition.AllowedEntities.Count > 0 && !Definition.AllowedEntities.Contains(modifiedEntity.DefinitionId.Value))
                return;

            _amountPlanned++;
            if (_amountPlanned >= Definition.Amount)
                MarkAsCompleted();
            else
                MarkDirty();
        }

        protected override void OnDeactivate()
        {
            base.OnDeactivate();
            if (!MyAPIGateway.Session.IsServerDecider())
                return;

            EdgePlacerSystem.EntityAdded -= EdgePlacerBehaviorOnEntityAdded;
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
            if (def.AllowedEntities.Count == 1)
                description.Add($"Plan {def.Amount} {DescContainer(def.AllowedEntities.First())} segments ({_amountPlanned}/{def.Amount})");
            else if (def.AllowedEntities.Count > 1)
                description.Add(
                    $"Plan {def.Amount} segments from this list: {string.Join(", ", def.AllowedEntities.Select(DescContainer))} ({_amountPlanned}/{def.Amount})");
            else
                description.Add($"Plan {def.Amount} segments ({_amountPlanned}/{def.Amount})");

            return description;
        }

        private int _amountPlanned;
    }

    [MyObjectBuilderDefinition(null)]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_QuestConditionPlanBendy : MyObjectBuilder_QuestConditionBase
    {
        public int AmountPlanned;
    }

    [MyQuestConditionDefinitionType(typeof(MyObjectBuilder_QuestConditionPlanBendyDefinition))]
    public class QuestConditionPlanBendyDefinition : MyQuestConditionBaseDefinition
    {
        public override MyObjectBuilderType SerializedTypeId => typeof(MyObjectBuilder_QuestConditionPlanBendy);

        public int Amount { get; private set; }
        public HashSet<MyDefinitionId> AllowedEntities { get; private set; }

        public override void Init(MyObjectBuilder_QuestConditionBaseDefinition builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_QuestConditionPlanBendyDefinition) builder;
            var s = new HashSet<MyDefinitionId>();
            if (ob.AllowedEntities != null)
                foreach (var a in ob.AllowedEntities)
                    s.Add(a);
            AllowedEntities = s;
            Amount = ob.Amount;
        }

        public override List<string> GetCompletedDescription()
        {
            List<string> description = base.GetCompletedDescription();
            var def = this;
            if (def.AllowedEntities.Count == 1)
                description.Add($"Planned {def.Amount} {QuestConditionPlanBendy.DescContainer(def.AllowedEntities.First())} segments");
            else if (def.AllowedEntities.Count > 1)
                description.Add($"Planned {def.Amount} segments from this list: {string.Join(", ", def.AllowedEntities.Select(QuestConditionPlanBendy.DescContainer))}");
            else
                description.Add($"Plan {def.Amount} segments");

            return description;
        }
    }

    [MyObjectBuilderDefinition(null)]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_QuestConditionPlanBendyDefinition : MyObjectBuilder_QuestConditionBaseDefinition
    {
        [XmlAttribute]
        public int Amount;

        [XmlElement("Include")]
        public SerializableDefinitionId[] AllowedEntities;
    }
}