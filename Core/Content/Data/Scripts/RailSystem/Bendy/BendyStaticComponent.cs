using System.Linq;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Session;

namespace Equinox76561198048419394.RailSystem.Bendy
{
    [MyComponent(typeof(MyObjectBuilder_BendyStaticComponent))]
    [MyDefinitionRequired]
    public class BendyStaticComponent : MyEntityComponent
    {
        public BendyStaticComponentDefinition Definition { get; private set; }
        private BendyLayer _graph;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            Definition = (BendyStaticComponentDefinition) definition;
            _nodes = new Node[Definition.Nodes.Count];
            _edges = new Edge[Definition.Edges.Count];
        }

        private bool _attached = false;
        private Node[] _nodes;
        private Edge[] _edges;

        public override void OnAddedToScene()
        {
            if (Definition.Nodes.Count == 0)
                return;

            var parent = Entity;
            while (parent != null && parent.Physics == null)
                parent = parent.Parent;
            if (parent?.Physics == null || !parent.Physics.IsStatic)
                return;

            _graph = MySession.Static.Components.Get<BendyController>().GetOrCreateLayer(Definition.Layer);
            _attached = true;

            for (var i = 0; i < Definition.Nodes.Count; i++)
            {
                var wm = Definition.Nodes[i] * Entity.WorldMatrix;
                _nodes[i] = _graph.GetOrCreateNode(wm.Translation, wm.Up);
                _nodes[i].Pin(wm);
            }

            for (var i = 0; i < Definition.Edges.Count; i++)
            {
                var e = Definition.Edges[i];
                _edges[i] = _graph.CreateEdge(_nodes[e.From], _nodes[e.To], e.Mode);
            }
        }

        public override void OnRemovedFromScene()
        {
            if (!_attached)
                return;

            _attached = false;

            foreach (var e in _edges)
                e.Close();
            for (var i = 0; i < _edges.Length; i++)
                _edges[i] = null;
            foreach (var n in _nodes)
            {
                n.UnpinTangent();
                if (n.TangentPins == 0 && !n.Neighbors.Any())
                    n.Close();
            }
            for (var i = 0; i < _nodes.Length; i++)
                _nodes[i] = null;

            _graph = null;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_BendyStaticComponent : MyObjectBuilder_EntityComponent
    {
    }
}