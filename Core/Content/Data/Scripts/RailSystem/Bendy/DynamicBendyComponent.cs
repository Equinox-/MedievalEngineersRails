using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Util.Curve;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRageMath;
using MySession = VRage.Session.MySession;

namespace Equinox76561198048419394.RailSystem.Bendy
{
    [MyComponent(typeof(MyObjectBuilder_DynamicBendyComponent))]
    [MyDefinitionRequired(typeof(BendyComponentDefinition))]
    public class DynamicBendyComponent : MyEntityComponent
    {
        private int _proxyId = -1;
        private DynamicBendyComponentDefinition _definition;
        private BendyLayer _graph;

        public ListReader<IEdge> LocalEdges { get; private set; }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _graph = MySession.Static.Components.Get<BendyController>().GetOrCreateLayer(_definition.Layer);
            Entity.PositionComp.OnPositionChanged += PositionChanged;
            _proxyId = _graph.DynamicEdges.AddProxy(Entity.PositionComp.WorldAABB, this, 0);
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            Entity.PositionComp.OnPositionChanged -= PositionChanged;
            _graph.DynamicEdges.RemoveProxy(_proxyId);
            _proxyId = -1;
        }

        private void PositionChanged(MyPositionComponentBase obj)
        {
            _graph.DynamicEdges.MoveProxy(_proxyId, Entity.PositionComp.WorldAABB, Entity?.Parent?.Physics?.LinearVelocity ?? Vector3.Zero);
        }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            _definition = (DynamicBendyComponentDefinition)def;
            var edges = new List<IEdge>(_definition.Edges.Count);
            for (var i = 0; i < _definition.Edges.Count; i++)
                edges.Add(new DynamicEdge(this, i));
            LocalEdges = edges;
        }

        public override bool IsSerialized => false;

        public sealed class DynamicEdge : IEdge
        {
            public readonly DynamicBendyComponent Owner;

            public DynamicEdge(DynamicBendyComponent owner, int edgeId)
            {
                var edge = owner._definition.Edges[edgeId];
                var from = owner._definition.Nodes[(int)edge.From].Position;
                var to = owner._definition.Nodes[(int)edge.To].Position;
                Owner = owner;
                Edge.ComputeCurve(from.Translation, from.Forward, from.Up,
                    to.Translation, to.Forward, to.Up,
                    edge.Mode,
                    out var fromMatrix,
                    out var toMatrix,
                    out var curve,
                    edge.Control0,
                    edge.Control1);
                Curve = curve;
                FromUp = (Vector3)fromMatrix.Up;
                ToUp = (Vector3)toMatrix.Up;
            }

            public ICurve Curve { get; }
            public MatrixD Transform => Owner.Entity.WorldMatrix;
            public MatrixD TransformInv => Owner.Entity.PositionComp.WorldMatrixNormalizedInv;
            public Vector3 FromUp { get; }
            public Vector3 ToUp { get; }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_DynamicBendyComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_DynamicBendyComponentDefinition))]
    public class DynamicBendyComponentDefinition : BendyComponentDefinitionBase
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_DynamicBendyComponentDefinition : MyObjectBuilder_BendyComponentDefinitionBase
    {
    }
}