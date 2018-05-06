using System;
using System.ComponentModel;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.RailSystem.Voxel
{
    [MyDefinitionType(typeof(MyObjectBuilder_RailGradeComponentDefinition))]
    public class RailGradeComponentDefinition : MyEntityComponentDefinition
    {
        public struct ImmutableShape
        {
            public readonly float Width;
            public float HalfWidth => Width / 2;
            public float RelaxAngleDegrees => (float) (RelaxAngleRadians * 180 / Math.PI);
            public readonly float RelaxAngleRadians;
            public readonly float VerticalOffset;
            public readonly int Segments;
            public readonly float Height;

            public ImmutableShape(MyObjectBuilder_RailGradeComponentDefinition.Shape s)
            {
                Width = s.Width;
                RelaxAngleRadians = s.RelaxAngleRadians;
                VerticalOffset = s.VerticalOffset;
                Segments = s.Segments <= 0 ? MyObjectBuilder_RailGradeComponentDefinition.Shape.DefaultSegments : s.Segments;
                Height = s.Height;
            }
        }

        public ImmutableShape? Support { get; private set; }
        public ImmutableShape? Excavate { get; private set; }
        public ImmutableShape? RequiredSupport { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_RailGradeComponentDefinition) def;
            Support = ob.Support.HasValue ? new ImmutableShape(ob.Support.Value) : (ImmutableShape?) null;
            Excavate = ob.Excavate.HasValue ? new ImmutableShape(ob.Excavate.Value) : (ImmutableShape?) null;
            RequiredSupport = ob.RequiredSupport.HasValue ? new ImmutableShape(ob.RequiredSupport.Value) : (ImmutableShape?) null;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailGradeComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public struct Shape
        {
            public const int DefaultSegments = 6;

            [XmlAttribute]
            public float Width;

            [XmlAttribute]
            public float RelaxAngleDegrees
            {
                get { return (float) (RelaxAngleRadians * 180 / Math.PI); }
                set { RelaxAngleRadians = (float) (Math.PI * value / 180f); }
            }

            [XmlIgnore]
            public float RelaxAngleRadians;

            [XmlElement]
            public float VerticalOffset;

            [XmlAttribute]
            [DefaultValue(DefaultSegments)]
            public int Segments;

            [XmlAttribute]
            public float Height;
        }

        [XmlElement]
        public Shape? Support;

        [XmlElement]
        public Shape? Excavate;

        [XmlElement]
        public Shape? RequiredSupport;
    }
}