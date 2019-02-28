using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Logging;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.RailSystem.Bendy.Shape
{
    [MyDefinitionType(typeof(MyObjectBuilder_BendyShapeComponentDefinition))]
    public class BendyShapeComponentDefinition : MyEntityComponentDefinition
    {
        public float Width { get; private set; }
        public float HalfWidth => Width / 2;
        public float Height { get; private set; }
        public int Segments { get; private set; }

        public float VerticalSizeTol { get; private set; }
        public float VerticalAlignTol { get; private set; }
        public float HorizontalAlignTol { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_BendyShapeComponentDefinition) def;
            Width = ob.Width;
            if (Width <= 0)
                MyDefinitionErrors.Add(def.Package, $"{GetType().Name} {def.GetId()} has {nameof(Width)}={Width} <= 0", LogSeverity.Warning);
            Height = ob.Height;
            if (Height <= 0)
                MyDefinitionErrors.Add(def.Package, $"{GetType().Name} {def.GetId()} has {nameof(Height)}={Height} <= 0", LogSeverity.Warning);
            Segments = ob.Segments;
            if (Segments <= 0)
                MyDefinitionErrors.Add(def.Package, $"{GetType().Name} {def.GetId()} has {nameof(Segments)}={Segments} <= 0", LogSeverity.Warning);

            VerticalSizeTol = ob.VerticalSizeTolerance ?? 0.05f;
            VerticalAlignTol = ob.VerticalAlignmentTolerance ?? 0.01f;
            HorizontalAlignTol = ob.HorizontalAlignmentTolerance ?? 0.25f;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_BendyShapeComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [XmlElement]
        public float Width;

        [XmlElement]
        public float Height;

        [XmlElement]
        public int Segments;

        [XmlElement]
        public float? VerticalSizeTolerance;

        [XmlElement]
        public float? VerticalAlignmentTolerance;

        [XmlElement]
        public float? HorizontalAlignmentTolerance;
    }
}