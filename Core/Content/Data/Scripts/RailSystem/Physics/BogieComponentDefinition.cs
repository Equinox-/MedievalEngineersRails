using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.RailSystem.Physics
{
    [MyDefinitionType(typeof(MyObjectBuilder_BogieComponentDefinition))]
    public class BogieComponentDefinition : MyEntityComponentDefinition
    {
        public float VerticalOffset { get; private set; }
        public string Layer { get; private set; }
        public float MaxVelocity { get; private set; }
        public float MaxForce { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_BogieComponentDefinition) def;
            VerticalOffset = ob.VerticalOffset;
            Layer = ob.Layer;
            MaxVelocity = ob.MaxVelocity;
            MaxForce = ob.MaxForce;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_BogieComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public float VerticalOffset;
        public string Layer;
        public float MaxVelocity, MaxForce;
    }
}