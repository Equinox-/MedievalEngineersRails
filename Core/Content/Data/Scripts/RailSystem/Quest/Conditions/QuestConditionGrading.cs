using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Util;
using Equinox76561198048419394.RailSystem.Voxel;
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
    [MyQuestConditionType(typeof(MyObjectBuilder_QuestConditionGrading))]
    public class QuestConditionGrading : MyQuestConditionBase
    {
        private new QuestConditionGradingDefinition Definition => (QuestConditionGradingDefinition) m_definition;

        public override void Deserialize(MyObjectBuilder_QuestConditionBase builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_QuestConditionGrading) builder;
            _volumeAdded = ob.VolumeAdded;
            _volumeRemoved = ob.VolumeRemoved;
        }

        public override MyObjectBuilder_QuestConditionBase Serialize()
        {
            var ob = (MyObjectBuilder_QuestConditionGrading) base.Serialize();
            ob.VolumeAdded = _volumeAdded;
            ob.VolumeRemoved = _volumeRemoved;
            return ob;
        }

        protected override void OnActivate()
        {
            base.OnActivate();
            if (!MyAPIGateway.Session.IsServerDecider())
                return;

            if ((Definition.VolumeAdded <= 0 && Definition.VolumeRemoved <= 0) || MyAPIGateway.Session.CreativeMode)
            {
                _volumeAdded = Definition.VolumeAdded;
                _volumeRemoved = Definition.VolumeRemoved;
                MarkAsCompleted();
                return;
            }

            RailGraderBehavior.GraderUsed += OnGraderUsed;
        }

        protected override void OnDeactivate()
        {
            base.OnDeactivate();
            if (!MyAPIGateway.Session.IsServerDecider())
                return;

            RailGraderBehavior.GraderUsed -= OnGraderUsed;
        }

        private void OnGraderUsed(RailGraderBehavior behavior, IReadOnlyCollection<RailGradeComponent> grades, uint added, uint removed)
        {
            if (Owner?.Entity == null || behavior.Owner != Owner.Entity)
                return;

            if (Definition.AllowedEntities.Count > 0)
            {
                var okay = false;
                foreach (var k in grades)
                    if (k.Entity.DefinitionId.HasValue && Definition.AllowedEntities.Contains(k.Entity.DefinitionId.Value))
                    {
                        okay = true;
                        break;
                    }

                if (!okay)
                    return;
            }

            _volumeAdded += (added * 1000f / byte.MaxValue);
            _volumeRemoved += (removed * 1000f / byte.MaxValue);

            if (_volumeAdded >= Definition.VolumeAdded && _volumeRemoved >= Definition.VolumeRemoved)
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

            var descRemoved = Math.Min(_volumeRemoved, def.VolumeRemoved);
            var descAdded = Math.Min(_volumeAdded, def.VolumeAdded);

            string descRoot;
            string amnt;
            if (def.VolumeAdded > 0 && def.VolumeRemoved > 0)
            {
                descRoot = $"Fill {def.VolumeAdded:F0}L and excavate {def.VolumeRemoved}L";
                amnt = $"(+ {descAdded:F0}/{def.VolumeAdded:F0}) (- {descRemoved:F0}/{def.VolumeRemoved:F0})";
            }
            else if (def.VolumeAdded > 0)
            {
                descRoot = $"Fill {def.VolumeAdded:F0}L";
                amnt = $"(+ {descAdded:F0}/{def.VolumeAdded:F0})";
            }
            else if (def.VolumeRemoved > 0)
            {
                descRoot = $"Excavated {def.VolumeRemoved:F0}L";
                amnt = $"(- {descRemoved:F0}/{def.VolumeRemoved:F0})";
            }
            else
                amnt = descRoot = "error";

            var verb = def.VolumeRemoved > 0 ? "from" : "into";

            if (def.AllowedEntities.Count == 1)
                description.Add($"{descRoot} of material {verb} a {DescContainer(def.AllowedEntities.First())} segment {amnt}");
            else if (def.AllowedEntities.Count > 1)
                description.Add(
                    $"{descRoot} of material {verb} a segment from this list: {string.Join(", ", def.AllowedEntities.Select(DescContainer))} {amnt}");
            else
                description.Add($"{descRoot} of material {verb} a segment {amnt}");

            return description;
        }

        private float _volumeAdded, _volumeRemoved;
    }

    [MyObjectBuilderDefinition(null)]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_QuestConditionGrading : MyObjectBuilder_QuestConditionBase
    {
        public float VolumeAdded, VolumeRemoved;
    }

    [MyQuestConditionDefinitionType(typeof(MyObjectBuilder_QuestConditionGradingDefinition))]
    public class QuestConditionGradingDefinition : MyQuestConditionBaseDefinition
    {
        public override MyObjectBuilderType SerializedTypeId => typeof(MyObjectBuilder_QuestConditionGrading);

        public float VolumeRemoved { get; private set; }
        public float VolumeAdded { get; private set; }
        public HashSet<MyDefinitionId> AllowedEntities { get; private set; }

        public override void Init(MyObjectBuilder_QuestConditionBaseDefinition builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_QuestConditionGradingDefinition) builder;
            VolumeAdded = ob.VolumeAdded;
            VolumeRemoved = ob.VolumeRemoved;
            var s = new HashSet<MyDefinitionId>();
            if (ob.AllowedEntities != null)
                foreach (var a in ob.AllowedEntities)
                    s.Add(a);
            AllowedEntities = s;
        }

        public override List<string> GetCompletedDescription()
        {
            List<string> description = base.GetCompletedDescription();
            var def = this;
            var descRemoved = def.VolumeRemoved;
            var descAdded = def.VolumeAdded;

            string descRoot;
            if (def.VolumeAdded > 0 && def.VolumeRemoved > 0)
                descRoot = $"Filled {def.VolumeAdded:F0}L and removed {def.VolumeRemoved}L";
            else if (def.VolumeAdded > 0)
                descRoot = $"Filled {def.VolumeAdded:F0}L";
            else if (def.VolumeRemoved > 0)
                descRoot = $"Excavated {def.VolumeRemoved:F0}L";
            else
                descRoot = "error";

            var verb = def.VolumeRemoved > 0 ? "from" : "into";

            if (def.AllowedEntities.Count == 1)
                description.Add($"{descRoot} of material {verb} a {QuestConditionGrading.DescContainer(def.AllowedEntities.First())} segment");
            else if (def.AllowedEntities.Count > 1)
                description.Add(
                    $"{descRoot} of material {verb} a segment from this list: {string.Join(", ", def.AllowedEntities.Select(QuestConditionGrading.DescContainer))}");
            else
                description.Add($"{descRoot} of material {verb} a segment");

            return description;
        }
    }

    [MyObjectBuilderDefinition(null)]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_QuestConditionGradingDefinition : MyObjectBuilder_QuestConditionBaseDefinition
    {
        [XmlAttribute]
        public float VolumeRemoved;

        [XmlAttribute]
        public float VolumeAdded;

        [XmlElement("Include")]
        public SerializableDefinitionId[] AllowedEntities;
    }
}