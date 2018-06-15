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
        public float CoefficientOfFriction { get; private set; }
        public float BrakingCoefficientOfFriction { get; private set; }
        private float _detachDistance;

        public float DetachDistance
        {
            get { return _detachDistance; }
            private set
            {
                _detachDistance = value;
                DetachDistanceSq = value * value;
            }
        }

        public float DetachDistanceSq { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_BogieComponentDefinition) def;
            VerticalOffset = ob.VerticalOffset;
            Layer = ob.Layer;
            MaxVelocity = ob.MaxVelocity;
            BrakingCoefficientOfFriction = ob.BrakingCoefficientOfFriction;
            MaxForce = ob.MaxForce;
            DetachDistance = ob.DetachDistance;
            CoefficientOfFriction = ob.CoefficientOfFriction;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_BogieComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public float VerticalOffset;
        public string Layer;
        
        // For this bogie on new steel rails.  Other rail types use a multiplier.
        public float CoefficientOfFriction;
        public float BrakingCoefficientOfFriction;
        
        public float MaxVelocity, MaxForce;
        public float DetachDistance;
    }
}