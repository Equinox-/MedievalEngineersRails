using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Voxel.Shape;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Voxel
{
    [MyDefinitionType(typeof(MyObjectBuilder_RailGradeComponentDefinition))]
    public class RailGradeComponentDefinition : MyEntityComponentDefinition
    {
        public static readonly MyStringHash DefaultPresetName = MyStringHash.GetOrCompute("Default");

        public struct ShapeOptions : IEquatable<ShapeOptions>
        {
            public MyStringHash Preset;
            public float ReposeAngleRadians;
            public float WidthIncrease;
            public float HeightIncrease;

            public bool Equals(ShapeOptions other) => Preset.Equals(other.Preset)
                                                      && ReposeAngleRadians.Equals(other.ReposeAngleRadians)
                                                      && WidthIncrease.Equals(other.WidthIncrease)
                                                      && HeightIncrease.Equals(other.HeightIncrease);

            public override bool Equals(object obj) => obj is ShapeOptions other && Equals(other);

            public override int GetHashCode()
            {
                var hashCode = Preset.GetHashCode();
                hashCode = (hashCode * 397) ^ ReposeAngleRadians.GetHashCode();
                hashCode = (hashCode * 397) ^ WidthIncrease.GetHashCode();
                hashCode = (hashCode * 397) ^ HeightIncrease.GetHashCode();
                return hashCode;
            }
        }

        public readonly struct ImmutableShape
        {
            public float HalfWidth => GradingParams.Width / 2;
            public float RelaxAngleDegrees => MathHelper.ToDegrees(GradingParams.RelaxAngleRadians);
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

            private ImmutableShape(in ImmutableShape reference, in ShapeOptions options)
            {
                ref readonly var gp = ref reference.GradingParams;
                GradingParams = new MeshGradeShape.GradingParams
                {
                    Width = gp.Width + Math.Max(0, options.WidthIncrease),
                    RelaxAngleRadians = options.ReposeAngleRadians > 0 ? options.ReposeAngleRadians : gp.RelaxAngleRadians,
                    ShiftUp = gp.ShiftUp,
                    Segments = gp.Segments,
                    Height = gp.Height + Math.Max(0, options.HeightIncrease),
                    EndPadding = gp.EndPadding,
                };
                Type = reference.Type;
            }

            public IGradeShape CreateShape(EdgeBlit e, bool invertHeight)
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

            public ImmutableShape Modify(in ShapeOptions options)
            {
                if (options.ReposeAngleRadians == 0 && options.WidthIncrease == 0 && options.HeightIncrease == 0) return this;
                return new ImmutableShape(in this, in options);
            }
        }

        public ImmutableShape? RequiredSupport { get; private set; }

        public ImmutableShape? GetSupport(in ShapeOptions options)
        {
            return (SupportPresets.TryGetValue(options.Preset, out var preset) ? preset.Shape : DefaultSupportPreset?.Shape)?.Modify(in options);
        }

        public ImmutableShape? GetExcavate(in ShapeOptions options)
        {
            return (ExcavatePresets.TryGetValue(options.Preset, out var preset) ? preset.Shape : DefaultExcavatePreset?.Shape)?.Modify(in options);
        }

        private RailGradePreset DefaultSupportPreset { get;  set; }
        internal DictionaryReader<MyStringHash, RailGradePreset> SupportPresets { get; private set; }

        private RailGradePreset DefaultExcavatePreset { get; set; }
        internal DictionaryReader<MyStringHash, RailGradePreset> ExcavatePresets { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_RailGradeComponentDefinition)def;
            RequiredSupport = ob.RequiredSupport.HasValue ? new ImmutableShape(ob.RequiredSupport.Value) : (ImmutableShape?)null;

            SupportPresets = InitPresets(in ob.Support, ob.SupportPresets);
            DefaultSupportPreset = SupportPresets.GetValueOrDefault(DefaultPresetName);

            ExcavatePresets = InitPresets(in ob.Excavate, ob.ExcavatePresets);
            DefaultExcavatePreset = ExcavatePresets.GetValueOrDefault(DefaultPresetName);
            return;

            Dictionary<MyStringHash, RailGradePreset> InitPresets(
                in MyObjectBuilder_RailGradeComponentDefinition.Shape? shape,
                List<MyObjectBuilder_RailGradeComponentDefinition.MyObjectBuilder_RailGradePreset> obPresets)
            {
                var presets = new Dictionary<MyStringHash, RailGradePreset>();
                if (shape.HasValue)
                    presets[DefaultPresetName] = new RailGradePreset(new MyObjectBuilder_RailGradeComponentDefinition.MyObjectBuilder_RailGradePreset
                    {
                        Name = DefaultPresetName.String,
                        Shape = shape.Value,
                    });
                if (obPresets == null) return presets;

                foreach (var obPreset in obPresets)
                {
                    var preset = new RailGradePreset(obPreset);
                    presets[preset.Name] = preset;
                    if (!presets.ContainsKey(DefaultPresetName)) presets[DefaultPresetName] = preset;
                }

                return presets;
            }
        }

        public class RailGradePreset
        {
            public readonly MyStringHash Name;
            public readonly string Tooltip;
            public readonly ImmutableShape Shape;

            internal RailGradePreset(MyObjectBuilder_RailGradeComponentDefinition.MyObjectBuilder_RailGradePreset ob)
            {
                Name = ob.Name != null ? MyStringHash.GetOrCompute(ob.Name) : DefaultPresetName;
                Tooltip = ob.Tooltip;
                Shape = new ImmutableShape(ob.Shape);
            }
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
                get => MathHelper.ToDegrees(RelaxAngleRadians);
                set => RelaxAngleRadians = MathHelper.ToRadians(value);
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
                Grade,
                Tunnel
            }
        }

        /// <summary>
        /// Obsolete. Use SupportPreset.
        /// </summary>
        [XmlElement]
        public Shape? Support;

        /// <summary>
        /// Obsolete. Use ExcavatePreset.
        /// </summary>
        [XmlElement]
        public Shape? Excavate;

        /// <summary>
        /// The shape that must be supported by voxels or grids for the rail to be usable.
        /// </summary>
        [XmlElement]
        public Shape? RequiredSupport;

        /// <summary>
        /// Preset shapes that define the filled voxels when using the railroad grader tool.
        /// </summary>
        [XmlElement("SupportPreset")]
        public List<MyObjectBuilder_RailGradePreset> SupportPresets;

        /// <summary>
        /// Preset shapes that define the excavated voxels when using the railroad grader tool.
        /// </summary>
        [XmlElement("ExcavatePreset")]
        public List<MyObjectBuilder_RailGradePreset> ExcavatePresets;

        public class MyObjectBuilder_RailGradePreset
        {
            [XmlAttribute]
            public string Name;

            [XmlAttribute]
            public string Tooltip;

            [XmlElement]
            public Shape Shape;
        }
    }
}