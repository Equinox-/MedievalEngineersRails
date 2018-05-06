using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Util;
using VRage.Game;
using VRage.Game.Definitions;
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
        public IReadOnlyList<float> ActivationUpperBound { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_BendyShapeComponentDefinition) def;
            Width = ob.Width;
            Height = ob.Height;
            Segments = ob.Segments;
            ActivationUpperBound = ob.ActivationUpperBound != null && ob.ActivationUpperBound.Length > 0 ? new ReadOnlyList<float>(ob.ActivationUpperBound) : null;
        }

        public int ActiveSegments(float buildPercent)
        {
            if (ActivationUpperBound == null || ActivationUpperBound.Count == 0)
                return Segments;
            var cap = Math.Min(ActivationUpperBound.Count, Segments);
            for (var i = 0; i < cap; i++)
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (buildPercent <= ActivationUpperBound[i] && (i + 1 >= cap || ActivationUpperBound[i] != ActivationUpperBound[i + 1]))
                    return i + 1;
            return 0;
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

        [XmlArrayItem("percent")]
        [DefaultValue(null)]
        public float[] ActivationUpperBound;
    }
}