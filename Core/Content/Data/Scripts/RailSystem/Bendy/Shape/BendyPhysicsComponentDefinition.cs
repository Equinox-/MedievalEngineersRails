using System.ComponentModel;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394.RailSystem.Bendy.Shape
{
    [MyDefinitionType(typeof(MyObjectBuilder_BendyPhysicsComponentDefinition))]
    public class BendyPhysicsComponentDefinition : BendyShapeComponentDefinition
    {
        public bool DestroyEnvItems { get; private set; }
        
        public MyStringHash Material { get; private set; }
        public string MaterialName => Material.String;

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_BendyPhysicsComponentDefinition) def;
            DestroyEnvItems = ob.DestroyEnvItems;
            Material = MyStringHash.GetOrCompute(ob.Material);
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_BendyPhysicsComponentDefinition : MyObjectBuilder_BendyShapeComponentDefinition
    {
        [XmlElement]
        public bool DestroyEnvItems;

        [XmlElement]
        [DefaultValue("Wood")]
        public string Material = "Wood";
    }
}