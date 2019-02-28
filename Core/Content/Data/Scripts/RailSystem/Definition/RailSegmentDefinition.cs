using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Logging;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.RailSystem.Definition
{
    [MyDefinitionType(typeof(MyObjectBuilder_RailSegmentDefinition))]
    public class RailSegmentDefinition : MyEntityComponentDefinition
    {
        public struct RailSegmentCaps
        {
            public readonly float ActivationLevel;
            public readonly float Friction;
            public readonly float MaxSpeed;
            public readonly float Gauge;

            public static readonly RailSegmentCaps Default = new RailSegmentCaps(0.95f, 1f, float.PositiveInfinity, 1f);

            private RailSegmentCaps(float activate, float fric, float max, float gauge)
            {
                ActivationLevel = activate;
                Friction = fric;
                MaxSpeed = max;
                Gauge = gauge;
            }

            internal RailSegmentCaps(MyObjectBuilder_RailSegmentDefinition.RsObRailSegmentCaps caps)
            {
                ActivationLevel = caps.ActivationLevel;
                Friction = caps.Friction;
                MaxSpeed = caps.MaxSpeed;
                Gauge = caps.Gauge;
            }
        }

        private RailSegmentCaps[] _caps;

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_RailSegmentDefinition) def;
            if (ob.Capabilities == null || ob.Capabilities.Length == 0)
            {
                MyDefinitionErrors.Add(def.Package, $"Definition {def.Id} has no defined capabilities, using default", LogSeverity.Warning);
                _caps = new[] {RailSegmentCaps.Default};
                return;
            }

            _caps = new RailSegmentCaps[ob.Capabilities.Length];
            for (var i = 0; i < ob.Capabilities.Length; i++)
                _caps[i] = new RailSegmentCaps(ob.Capabilities[i]);
            Array.Sort(_caps, (a, b) => Comparer<float>.Default.Compare(a.ActivationLevel, b.ActivationLevel));
        }

        public RailSegmentCaps? CapabilitiesFor(float activationLevel)
        {
            for (var i = _caps.Length - 1; i >= 0; i--)
                if (_caps[i].ActivationLevel <= activationLevel)
                    return _caps[i];

            return null;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailSegmentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public class RsObRailSegmentCaps
        {
            // Build ratio at which this is activated.
            [XmlAttribute]
            [DefaultValue(0.95f)]
            public float ActivationLevel = 0.95f;

            // Multiplier applied on the base (new steel rail) friction.
            [XmlAttribute]
            [DefaultValue(1)]
            public float Friction = 1;

            [XmlAttribute]
            [DefaultValue(float.PositiveInfinity)]
            public float MaxSpeed = float.PositiveInfinity;

            [XmlAttribute]
            public float Gauge;
        }

        [XmlArrayItem("Capability")]
        public RsObRailSegmentCaps[] Capabilities;
    }
}