using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;
using VRage;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy
{
    [MyDefinitionType(typeof(MyObjectBuilder_BendyStaticComponentDefinition))]
    public class BendyStaticComponentDefinition : MyEntityComponentDefinition
    {
        public string Layer { get; private set; }
        public IReadOnlyList<MatrixD> Nodes { get; private set; }
        public IReadOnlyList<ImmutableEdge> Edges { get; private set; }

        public struct ImmutableEdge
        {
            public readonly uint From;
            public readonly uint To;
            public readonly CurveMode Mode;

            public ImmutableEdge(uint a, uint b, CurveMode mode)
            {
                From = a;
                To = b;
                Mode = mode;
            }
        }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_BendyStaticComponentDefinition) def;

            Layer = ob.Layer;
            Nodes = ob?.Nodes?.Select(x => x.GetMatrix()).ToArray() ?? new MatrixD[0];
            Edges = ob?.Edges?.Select(x => new ImmutableEdge(x.From, x.To, x.Mode)).ToArray() ?? new ImmutableEdge[0];
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_BendyStaticComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [XmlElement("Node")]
        public SimpleMatrix[] Nodes;

        [XmlElement("Edge")]
        public Edge[] Edges;

        [XmlElement("Layer")]
        public string Layer;

        public struct SimpleMatrix
        {
            public SerializableVector3 Position;
            public SerializableVector3 Forward;
            public SerializableVector3 Up;

            public MatrixD GetMatrix()
            {
                if (Vector3.IsZero(Forward))
                    throw new Exception("Zero forward");
                if (Vector3.IsZero(Up))
                    throw new Exception("Zero up");
                return MatrixD.CreateWorld((Vector3) Position, Forward, Up);
            }
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
        }
    }

    public enum CurveMode
    {
        Linear,
        CubicBez
    }
}