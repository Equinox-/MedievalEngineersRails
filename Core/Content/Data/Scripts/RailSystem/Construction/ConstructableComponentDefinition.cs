using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Util;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions;
using VRage.ObjectBuilders.Definitions.Inventory;

namespace Equinox76561198048419394.RailSystem.Construction
{
    [MyDefinitionType(typeof(MyObjectBuilder_ConstructableComponentDefinition))]
    public class ConstructableComponentDefinition : MyEntityComponentDefinition
    {
        public struct CcComponent
        {
            public readonly DefinitionTagId Required;
            public readonly ushort Count;
            public readonly DefinitionTagId Returned;

            public CcComponent(MyObjectBuilder_ConstructableComponentDefinition.CcObComponent ob)
            {
                Required = ob.Definition;
                Count = ob.Count;
                Returned = ob.ReturnedItem ?? ob.Definition;
            }
        }

        public struct CcBuildModel
        {
            public readonly float UpperBound;
            public readonly string ModelFile;

            public CcBuildModel(MyObjectBuilder_ConstructableComponentDefinition.CcObBuildModel ob)
            {
                UpperBound = ob.UpperBound;
                ModelFile = ob.File;
            }
        }

        public IReadOnlyList<CcComponent> Components { get; private set; }
        public IReadOnlyList<CcBuildModel> BuildModels { get; private set; }
        public float MaxIntegrity { get; private set; }
        public float IntegrityPerSecond => MaxIntegrity / (float) BuildTime.TotalSeconds;
        public TimeSpan BuildTime { get; private set; }
        public int TotalComponents { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var def = (MyObjectBuilder_ConstructableComponentDefinition) builder;
            
            Components = new ReadOnlyList<CcComponent>(def.Components.Select(x => new CcComponent(x)).ToList());
            TotalComponents = Components.Sum(x => x.Count);
            if (TotalComponents == 0)
                MyDefinitionErrors.Add(Package, $"{nameof(MyObjectBuilder_ConstructableComponentDefinition.BuildProgressModels)} has no values",
                    LogSeverity.Critical);            
            
            var models = def.BuildProgressModels.Select(x => new CcBuildModel(x)).ToList();
            models.Sort((a, b) => a.UpperBound.CompareTo(b.UpperBound));
            BuildModels = new ReadOnlyList<CcBuildModel>(models);
            if (BuildModels.Count == 0)
                MyDefinitionErrors.Add(Package, $"{nameof(MyObjectBuilder_ConstructableComponentDefinition.BuildProgressModels)} has no values",
                    LogSeverity.Critical);
            
            if (!def.BuildTime.HasValue)
                MyDefinitionErrors.Add(Package, $"{nameof(MyObjectBuilder_ConstructableComponentDefinition.BuildTime)} has no value", LogSeverity.Critical);
            BuildTime = def.BuildTime != null ? (TimeSpan) def.BuildTime.Value : TimeSpan.Zero;
            
            if (!def.MaxIntegrity.HasValue)
                MyDefinitionErrors.Add(Package, $"{nameof(MyObjectBuilder_ConstructableComponentDefinition.MaxIntegrity)} has no value",
                    LogSeverity.Critical);
            MaxIntegrity = def.MaxIntegrity ?? 0;
        }

        public CcBuildModel BuildModelFor(float percent)
        {
            foreach (var k in BuildModels)
                if (k.UpperBound >= percent)
                    return k;
            return BuildModels.Last();
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_ConstructableComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public class CcObComponent
        {
            [XmlAttribute]
            public string Type
            {
                get { return Definition.Type; }
                set { Definition.Type = value; }
            }

            [XmlAttribute]
            public string Subtype
            {
                get { return Definition.Subtype; }
                set { Definition.Subtype = value; }
            }

            [XmlAttribute]
            public string Tag
            {
                get { return Definition.Tag; }
                set { Definition.Tag = value; }
            }

            [XmlIgnore]
            public DefinitionTagId Definition;

            [XmlAttribute]
            public ushort Count;

            [XmlElement]
            public DefinitionTagId? ReturnedItem;
        }

        public class CcObBuildModel
        {
            [XmlAttribute]
//            [Obsolete("Use UpperBound")]
            public float BuildPercentUpperBound
            {
                get => UpperBound;
                set => UpperBound = value;
            }
            
            [XmlAttribute]
            public float UpperBound;

            [XmlAttribute]
            public string File;
        }

        [XmlArrayItem("Component")]
        public CcObComponent[] Components;

        [XmlArrayItem("Model")]
        public CcObBuildModel[] BuildProgressModels;

        [XmlElement("BuildTime")]
        public TimeDefinition? BuildTime;

        [XmlElement("MaxIntegrity")]
        public float? MaxIntegrity;
    }
}