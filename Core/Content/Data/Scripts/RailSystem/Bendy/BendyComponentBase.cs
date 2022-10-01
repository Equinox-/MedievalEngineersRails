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
    [MyDefinitionType(typeof(MyObjectBuilder_BendyComponentDefinitionBase))]
    public abstract class BendyComponentDefinitionBase : MyEntityComponentDefinition
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

            /// <summary>
            /// Optional control values for bez curves
            /// </summary>
            public readonly Vector3? Control0, Control1;

            public ImmutableEdge(uint a, uint b, CurveMode mode, IReadOnlyList<string> bones, Vector3? ctl0, Vector3? ctl1)
            {
                From = a;
                To = b;
                Mode = mode;
                Bones = bones != null ? new ReadOnlyList<string>(bones) : null;
                Control0 = ctl0;
                Control1 = ctl1;
            }
        }

        public IReadOnlyList<ImmutableNode> Nodes { get; private set; }

        public IReadOnlyList<ImmutableEdge> Edges { get; private set; }

        /// <summary>
        /// Layer this bendy block exists on.  Blocks on the same layer can be connected together.
        /// </summary>
        public string Layer { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var def = (MyObjectBuilder_BendyComponentDefinitionBase) builder;

            #region Config

            Layer = def.Layer ?? "DefaultLayer";
            if (string.IsNullOrWhiteSpace(def.Layer))
                MyDefinitionErrors.Add(def.Package,
                    $"{nameof(BendyComponentDefinitionBase)} {builder.GetId()} has {nameof(Layer)} that is null or whitespace",
                    LogSeverity.Error);

            var nodes = new ImmutableNode[def.Nodes?.Length ?? 0];
            if (def.Nodes != null)
                for (var i = 0; i < def.Nodes.Length; i++)
                {
                    var n = def.Nodes[i];
                    var m = Matrix.CreateWorld(n.Position, n.Forward, n.Up);
                    if (Vector3.IsZero(m.Forward))
                        MyDefinitionErrors.Add(def.Package,
                            $"{nameof(BendyComponentDefinitionBase)} {builder.GetId()} node {i} has an invalid matrix forward",
                            LogSeverity.Error);
                    if (Vector3.IsZero(m.Up))
                        MyDefinitionErrors.Add(def.Package,
                            $"{nameof(BendyComponentDefinitionBase)} {builder.GetId()} node {i} has an invalid matrix up",
                            LogSeverity.Error);
                    if (Math.Abs(m.Forward.Dot(m.Up)) > 1e-3f)
                        MyDefinitionErrors.Add(def.Package,
                            $"{nameof(BendyComponentDefinitionBase)} {builder.GetId()} node {i} has an invalid matrix",
                            LogSeverity.Error);
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
                        MyDefinitionErrors.Add(def.Package,
                            $"{nameof(BendyComponentDefinitionBase)} {builder.GetId()} edge {i} refers to an invalid from",
                            LogSeverity.Error);
                    if (n1 != e.To)
                        MyDefinitionErrors.Add(def.Package,
                            $"{nameof(BendyComponentDefinitionBase)} {builder.GetId()} edge {i} refers to an invalid to",
                            LogSeverity.Error);

                    var bones = e.Bones?.Split(null).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
                    edges[i] = new ImmutableEdge((uint) n0, (uint) n1, e.Mode, bones != null ? new ReadOnlyList<string>(bones) : null, e.Control1, e.Control2);
                }

            Nodes = new ReadOnlyList<ImmutableNode>(nodes);
            Edges = new ReadOnlyList<ImmutableEdge>(edges);

            #endregion
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public abstract class MyObjectBuilder_BendyComponentDefinitionBase : MyObjectBuilder_EntityComponentDefinition
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

            [XmlElement]
            [DefaultValue(null)]
            public SerializableVector3? Control1;

            [XmlElement]
            [DefaultValue(null)]
            public SerializableVector3? Control2;


            public string Bones;
        }

        #endregion
    }
}