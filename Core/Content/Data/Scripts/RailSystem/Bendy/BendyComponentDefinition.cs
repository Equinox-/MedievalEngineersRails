using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Util;
using VRage;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy
{
    [MyDefinitionType(typeof(MyObjectBuilder_BendyComponentDefinition))]
    public class BendyComponentDefinition : MyEntityComponentDefinition
    {
        public struct ImmutableNode
        {
            public readonly Matrix Position;
            public readonly bool Movable;

            public ImmutableNode(Matrix pos, bool movable)
            {
                Position = pos;
                Movable = movable;
            }
        }

        public struct ImmutableEdge
        {
            public readonly uint From;
            public readonly uint To;
            public readonly CurveMode Mode;
            public readonly IReadOnlyList<string> Bones;

            public ImmutableEdge(uint a, uint b, CurveMode mode, IReadOnlyList<string> bones)
            {
                From = a;
                To = b;
                Mode = mode;
                Bones = bones != null ? new ReadOnlyList<string>(bones) : null;
            }
        }

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

        public IReadOnlyList<ImmutableNode> Nodes { get; private set; }

        public IReadOnlyList<ImmutableEdge> Edges { get; private set; }

        public IReadOnlyDictionary<string, IReadOnlyList<BoneEdgeMetadata>> BoneMetadata { get; private set; }

        /// <summary>
        /// Layer this bendy block exists on.  Blocks on the same layer can be connected together.
        /// </summary>
        public string Layer { get; private set; }

        #region Constraints

        public ImmutableRange<float> Distance { get; private set; }
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

            #region Config

            Layer = def.Layer;
            if (string.IsNullOrWhiteSpace(Layer))
                MyDefinitionErrors.Add(def.ModContext,
                    $"{nameof(BendyComponentDefinition)} {builder.GetId()} has {nameof(Layer)} that is null or whitespace",
                    TErrorSeverity.Error);

            var nodes = new ImmutableNode[def.Nodes?.Length ?? 0];
            if (def.Nodes != null)
                for (var i = 0; i < def.Nodes.Length; i++)
                {
                    var n = def.Nodes[i];
                    var m = Matrix.CreateWorld(n.Position, n.Forward, n.Up);
                    if (Vector3.IsZero(m.Forward))
                        MyDefinitionErrors.Add(def.ModContext,
                            $"{nameof(BendyComponentDefinition)} {builder.GetId()} node {i} has an invalid matrix forward",
                            TErrorSeverity.Error);
                    if (Vector3.IsZero(m.Up))
                        MyDefinitionErrors.Add(def.ModContext,
                            $"{nameof(BendyComponentDefinition)} {builder.GetId()} node {i} has an invalid matrix up",
                            TErrorSeverity.Error);
                    if (Math.Abs(m.Forward.Dot(m.Up)) > 1e-3f)
                        MyDefinitionErrors.Add(def.ModContext,
                            $"{nameof(BendyComponentDefinition)} {builder.GetId()} node {i} has an invalid matrix",
                            TErrorSeverity.Error);
                    nodes[i] = new ImmutableNode(m, n.Movable);
                }

            var edges = new ImmutableEdge[def.Edges?.Length ?? 0];
            if (edges.Length > 0 && nodes.Length == 0)
                throw new Exception($"Component {builder.GetId()} has edges when it has no nodes.  Unrecoverable.");
            if (def.Edges != null)
                for (var i = 0; i < def.Edges.Length; i++)
                {
                    var e = def.Edges[i];
                    var n0 = MathHelper.Clamp((int) e.From, 0, nodes.Length - 1);
                    var n1 = MathHelper.Clamp((int) e.To, 0, nodes.Length - 1);
                    if (n0 != e.From)
                        MyDefinitionErrors.Add(def.ModContext,
                            $"{nameof(BendyComponentDefinition)} {builder.GetId()} edge {i} refers to an invalid from",
                            TErrorSeverity.Error);
                    if (n1 != e.To)
                        MyDefinitionErrors.Add(def.ModContext,
                            $"{nameof(BendyComponentDefinition)} {builder.GetId()} edge {i} refers to an invalid to",
                            TErrorSeverity.Error);

                    var bones = e.Bones?.Split(null).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
                    edges[i] = new ImmutableEdge((uint) n0, (uint) n1, e.Mode,
                        bones != null ? new ReadOnlyList<string>(bones) : null);
                }

            Nodes = new ReadOnlyList<ImmutableNode>(nodes);
            Edges = new ReadOnlyList<ImmutableEdge>(edges);

            #endregion

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
                MyDefinitionErrors.Add(def.ModContext,
                    $"{nameof(BendyComponentDefinition)} {builder.GetId()} distance has min > max",
                    TErrorSeverity.Error);
            MaxAngleDegrees = def.MaxAngleDegrees ?? RailConstants.DefaultMaxAngleDegrees;
            if (MaxAngleDegrees < 0)
                MyDefinitionErrors.Add(def.ModContext,
                    $"{nameof(BendyComponentDefinition)} {builder.GetId()} max angle is less than zero",
                    TErrorSeverity.Error);
            MaxGradeRatio = def.MaxGradeRatio ?? RailConstants.DefaultMaxGradeRatio;
            if (MaxGradeRatio < 0)
                MyDefinitionErrors.Add(def.ModContext,
                    $"{nameof(BendyComponentDefinition)} {builder.GetId()} max grade ratio is less than zero",
                    TErrorSeverity.Error);

            #endregion
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_BendyComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        #region Config

        /// <summary>
        /// Layer this bendy block exists on.  Blocks on the same layer can be connected together.
        /// </summary>
        [XmlElement]
        public string Layer;

        [XmlArrayItem("Node")]
        public Node[] Nodes;

        [XmlArrayItem("Edge")]
        public Edge[] Edges;

        public struct Node
        {
            public SerializableVector3 Position;
            public SerializableVector3 Forward;
            public SerializableVector3 Up;

            [XmlAttribute(nameof(Movable))]
            public bool Movable;
        }

        public struct Edge
        {
            [XmlAttribute(nameof(From))]
            public uint From;

            [XmlAttribute(nameof(To))]
            public uint To;

            [XmlAttribute(nameof(Mode))]
            [DefaultValue(CurveMode.Linear)]
            public CurveMode Mode;

            public string Bones;
        }

        #endregion

        #region Constraints

        [XmlElement]
        public MutableRange<float>? Distance;

        [XmlElement]
        public float? MaxGradeRatio;

        [XmlElement]
        public float? MaxAngleDegrees;

        #endregion
    }
}