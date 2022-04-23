using System;
using System.ComponentModel;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Voxel.Shape;
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
            public float HalfWidth => GradingParams.Width / 2;
            public float RelaxAngleDegrees => (float) (GradingParams.RelaxAngleRadians * 180 / Math.PI);
            public int Segments => GradingParams.Segments;
            public float VerticalOffset => GradingParams.ShiftUp;
            public readonly MeshGradeShape.GradingParams GradingParams;
            public readonly MyObjectBuilder_RailGradeComponentDefinition.Shape.GradeType Type;

            public ImmutableShape(MyObjectBuilder_RailGradeComponentDefinition.Shape s)
            {
                GradingParams = new MeshGradeShape.GradingParams
                {
                    Width = s.Width,
                    RelaxAngleRadians = s.RelaxAngleRadians,
                    ShiftUp = s.VerticalOffset,
                    Segments = s.Segments <= 0 ? MyObjectBuilder_RailGradeComponentDefinition.Shape.DefaultSegments : s.Segments,
                    Height = s.Height,
                    EndPadding = s.EndPadding,
                };
                Type = s.Type;
            }

            public MeshGradeShape CreateShape(EdgeBlit e, bool invertHeight)
            {
                var paramsCopy = GradingParams;
                if (invertHeight)
                    paramsCopy.Height *= -1;
                switch (Type)
                {
                    case MyObjectBuilder_RailGradeComponentDefinition.Shape.GradeType.Tunnel:
                        return MeshGradeShape.CreateTunnelShape(e, paramsCopy);
                    case MyObjectBuilder_RailGradeComponentDefinition.Shape.GradeType.Grade:
                    default:
                        return MeshGradeShape.CreateGradeShape(e, paramsCopy);
                }
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

            [XmlAttribute]
            [DefaultValue(0.5f)]
            public float EndPadding;

            [XmlAttribute]
            [DefaultValue(0f)]
            public float VerticalOffset;

            [XmlAttribute]
            [DefaultValue(DefaultSegments)]
            public int Segments;

            [XmlAttribute]
            public float Height;

            [XmlAttribute]
            [DefaultValue(GradeType.Grade)]
            public GradeType Type;
            
            public enum GradeType
            {
                Grade, Tunnel
            }
        }

        [XmlElement]
        public Shape? Support;

        [XmlElement]
        public Shape? Excavate;

        [XmlElement]
        public Shape? RequiredSupport;
    }
}