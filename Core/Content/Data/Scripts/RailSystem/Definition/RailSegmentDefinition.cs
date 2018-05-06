
using System.Xml.Serialization;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Inventory;

namespace Equinox76561198048419394.RailSystem.Definition
{
    public class RailSegmentDefinition
    {
        
    }
    
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailSegmentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public class RsObRailSegmentCaps
        {
            [XmlAttribute]
            public int ComponentsInstalled;

            [XmlAttribute]
            public float Friction;
            
            [XmlAttribute]
            public float MaxSpeed;

            [XmlAttribute]
            public float TurningRadius;
        }

        public class RsObComponent
        {
            [XmlAttribute]
            public string Type
            {
                get
                {
                    return Definition.Type;
                }
                set
                {
                    Definition.Type = value;
                }
            }
            [XmlAttribute]
            public string Subtype
            {
                get
                {
                    return Definition.Subtype;
                }
                set
                {
                    Definition.Subtype = value;
                }
            }
            [XmlAttribute]
            public string Tag
            {
                get
                {
                    return Definition.Tag;
                }
                set
                {
                    Definition.Tag = value;
                }
            }
            [XmlIgnore]
            public DefinitionTagId Definition;
            
            [XmlAttribute]
            public ushort Count;
            
            public DefinitionTagId? ReturnedItem;
        }

        public class RsObBuildModel
        {
            [XmlAttribute]
            public int ComponentsInstalled;

            [XmlAttribute]
            public string Model;
        }
        
        [XmlArrayItem("Component")]
        public MyObjectBuilder_CubeBlockDefinition.CbObCubeBlockComponent[] Components;

        [XmlArrayItem("Capability")]
        public RsObRailSegmentCaps[] Capabilities;

        [XmlArrayItem("Stage")]
        public RsObBuildModel[] BuildStages;
    }
}