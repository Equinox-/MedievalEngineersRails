using System.Runtime.Remoting.Contexts;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Logging;
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
        
        public PowerObserver.RequiredPowerEnum NeedsPower { get; private set; }
        
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
        
        public float OrientationConvergenceFactor { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_BogieComponentDefinition) def;
            
            if (!ob.VerticalOffset.HasValue)
                MyDefinitionErrors.Add(Package, $"Bogie {Id} has no {nameof(VerticalOffset)}", LogSeverity.Critical);
            if (!ob.CoefficientOfFriction.HasValue)
                MyDefinitionErrors.Add(Package, $"Bogie {Id} has no {nameof(CoefficientOfFriction)}", LogSeverity.Critical);
            if (!ob.DetachDistance.HasValue)
                MyDefinitionErrors.Add(Package, $"Bogie {Id} has no {nameof(DetachDistance)}", LogSeverity.Critical);
            
            VerticalOffset = ob.VerticalOffset ?? 0;
            Layer = ob.Layer;
            MaxVelocity = ob.MaxVelocity ?? 0;
            BrakingCoefficientOfFriction = ob.BrakingCoefficientOfFriction ?? 0.2f;
            MaxForce = ob.MaxForce ?? 0;
            DetachDistance = ob.DetachDistance ?? 1.5f;
            CoefficientOfFriction = ob.CoefficientOfFriction ?? 0.01f;
            OrientationConvergenceFactor = ob.OrientationConvergenceFactor ?? 1f;
            NeedsPower = ob.NeedsPower ?? PowerObserver.RequiredPowerEnum.None;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_BogieComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public float? VerticalOffset;
        public string Layer;
        
        // For this bogie on new steel rails.  Other rail types use a multiplier.
        public float? CoefficientOfFriction;
        public float? BrakingCoefficientOfFriction;
        
        public float? MaxVelocity, MaxForce;
        public float? DetachDistance;

        public float? OrientationConvergenceFactor;

        public PowerObserver.RequiredPowerEnum? NeedsPower;
    }
}