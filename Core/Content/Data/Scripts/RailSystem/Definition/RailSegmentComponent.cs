using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.RailSystem.Definition
{
    [MyComponent(typeof(MyObjectBuilder_RailSegmentComponent))]
    [MyDefinitionRequired]
    public class RailSegmentComponent : MyEntityComponent
    {
        public RailSegmentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            Definition = (RailSegmentDefinition) definition;
        }
    }


    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailSegmentComponent : MyObjectBuilder_EntityComponent
    {
    }
}