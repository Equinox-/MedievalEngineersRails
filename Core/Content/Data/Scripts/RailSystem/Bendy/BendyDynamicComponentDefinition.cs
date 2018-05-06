using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.RailSystem.Bendy
{
    [MyDefinitionType(typeof(MyObjectBuilder_BendyDynamicComponentDefinition))]
    public class BendyDynamicComponentDefinition : MyEntityComponentDefinition
    {
        /// <summary>
        /// Number of additional bones at the beginning of the chain.
        /// </summary>
        public int LeadingBones { get; private set; }

        /// <summary>
        /// Number of bones in the primary section of the chain.
        /// </summary>
        public int PrimaryBones { get; private set; }

        /// <summary>
        /// Number of additional bones after the primary section of the chain.
        /// </summary>
        public int TrailingBones { get; private set; }

        /// <summary>
        /// Prefix used to identify bones in the chain.  Order is based on parent-child order.
        /// </summary>
        public string BonePrefix { get; private set; }

        /// <summary>
        /// Layer this bendy block exists on.  Blocks on the same layer can be connected together.
        /// </summary>
        public string Layer { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var def = (MyObjectBuilder_BendyDynamicComponentDefinition) builder;

            LeadingBones = def.LeadingBones;
            PrimaryBones = def.PrimaryBones;
            TrailingBones = def.TrailingBones;
            BonePrefix = def.BonePrefix;
            Layer = def.Layer;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_BendyDynamicComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        /// <summary>
        /// Number of additional bones at the beginning of the chain.
        /// </summary>
        [XmlElement]
        public int LeadingBones;

        /// <summary>
        /// Number of bones in the primary section of the chain.
        /// </summary>
        [XmlElement]
        public int PrimaryBones;

        /// <summary>
        /// Number of additional bones after the primary section of the chain.
        /// </summary>
        [XmlElement]
        public int TrailingBones;

        /// <summary>
        /// Prefix used to identify bones in the chain.  Order is based on parent-child order.
        /// </summary>
        [XmlElement]
        public string BonePrefix;

        /// <summary>
        /// Layer this bendy block exists on.  Blocks on the same layer can be connected together.
        /// </summary>
        [XmlElement]
        public string Layer;
    }
}