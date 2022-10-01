using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Util;
using VRage;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy
{
    [MyDefinitionType(typeof(MyObjectBuilder_BendyComponentDefinition))]
    public class BendyComponentDefinition : BendyComponentDefinitionBase
    {
        public struct BoneEdgeMetadata
        {
            /// <summary>
            /// Edge ID
            /// </summary>
            public readonly uint Edge;

            /// <summary>
            /// Ratio of distance along edge
            /// </summary>
            public readonly float EdgeFactor;

            /// <summary>
            /// Weight of edge to final bone position
            /// </summary>
            public readonly float EdgeWeight;

            public BoneEdgeMetadata(uint edge, float edgeFactor, float edgeWeight)
            {
                Edge = edge;
                EdgeFactor = edgeFactor;
                EdgeWeight = edgeWeight;
            }
        }

        public IReadOnlyDictionary<string, IReadOnlyList<BoneEdgeMetadata>> BoneMetadata { get; private set; }

        #region Constraints

        public ImmutableRange<float> Distance { get; private set; }
        
        public float PreferredDistance { get; private set; }
        
        public float MaxGradeRatio { get; private set; }

        public float MaxAngleRadians { get; private set; }

        public float MaxAngleDegrees
        {
            get { return (float) (MaxAngleRadians * 180 / Math.PI); }
            private set { MaxAngleRadians = (float) (value * Math.PI / 180); }
        }

        #endregion

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var def = (MyObjectBuilder_BendyComponentDefinition) builder;

            #region Bone Meta

            var tmp = new Dictionary<string, List<BoneEdgeMetadata>>();
            for (var i = 0; i < Edges.Count; i++)
            {
                var e = Edges[i];
                if (e.Bones == null || e.Bones.Count <= 0) continue;
                for (var index = 0; index < e.Bones.Count; index++)
                {
                    var b = e.Bones[index];
                    List<BoneEdgeMetadata> src;
                    if (!tmp.TryGetValue(b, out src))
                        tmp.Add(b, src = new List<BoneEdgeMetadata>());
                    src.Add(new BoneEdgeMetadata((uint) i, index / (float) (e.Bones.Count - 1), 1f));
                }
            }

            BoneMetadata = tmp.ToDictionary(x => x.Key, x =>
            {
                var totalBoneWeight = x.Value.Sum(y => y.EdgeWeight);
                var dest = new List<BoneEdgeMetadata>(x.Value.Count);
                foreach (var old in x.Value)
                {
                    if (old.EdgeWeight < 0)
                        continue;
                    dest.Add(new BoneEdgeMetadata(old.Edge, old.EdgeFactor, old.EdgeWeight / totalBoneWeight));
                }

                return (IReadOnlyList<BoneEdgeMetadata>) new ReadOnlyList<BoneEdgeMetadata>(dest);
            });

            #endregion

            #region Constraints

            Distance = def.Distance?.Immutable() ??
                       new ImmutableRange<float>(RailConstants.DefaultMinLength, RailConstants.DefaultMaxLength);
            if (Distance.Min > Distance.Max)
                MyDefinitionErrors.Add(def.Package,
                    $"{nameof(BendyComponentDefinition)} {builder.GetId()} distance has min > max",
                    LogSeverity.Error);
            PreferredDistance = def.PreferredDistance ?? ((Distance.Max + Distance.Min) / 2);
            MaxAngleDegrees = def.MaxAngleDegrees ?? RailConstants.DefaultMaxAngleDegrees;
            if (MaxAngleDegrees < 0)
                MyDefinitionErrors.Add(def.Package,
                    $"{nameof(BendyComponentDefinition)} {builder.GetId()} max angle is less than zero",
                    LogSeverity.Error);
            MaxGradeRatio = def.MaxGradeRatio ?? RailConstants.DefaultMaxGradeRatio;
            if (MaxGradeRatio < 0)
                MyDefinitionErrors.Add(def.Package,
                    $"{nameof(BendyComponentDefinition)} {builder.GetId()} max grade ratio is less than zero",
                    LogSeverity.Error);

            #endregion
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_BendyComponentDefinition : MyObjectBuilder_BendyComponentDefinitionBase
    {
        #region Constraints

        [XmlElement]
        public MutableRange<float>? Distance;

        [XmlElement]
        public float? PreferredDistance;

        [XmlElement]
        public float? MaxGradeRatio;

        [XmlElement]
        public float? MaxAngleDegrees;

        #endregion
    }
}