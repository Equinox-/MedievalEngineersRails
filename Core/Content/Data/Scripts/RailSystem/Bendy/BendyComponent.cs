using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Util;
using VRage;
using VRage.Components;
using VRage.Components.Entity.Animations;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy
{
    [MyComponent(typeof(MyObjectBuilder_BendyComponent))]
    [MyDefinitionRequired(typeof(BendyComponentDefinition))]
    [MyDependency(typeof(MySkeletonComponent), Critical = false)]
    public class BendyComponent : MyEntityComponent
    {
        public BendyComponentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (BendyComponentDefinition) def;
            _nodeRefs = new NodeRef[Definition.Nodes.Count];
            Edges = new Edge[Definition.Edges.Count];
        }

        #region Serialization

        public override bool IsSerialized => Definition?.Nodes != null && Definition.Nodes.Any(x => x.Movable);

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_BendyComponent)base.Serialize(copy);
            lock (_movableNodeData)
            {
                if (Entity != null && Entity.InScene)
                    CacheMovableData(_movableNodeData);
                ob.Overrides = new MyObjectBuilder_BendyComponent.NodePose[_movableNodeData.Count];
                var i = 0;
                foreach (var x in _movableNodeData)
                {
                    ob.Overrides[i++] = new MyObjectBuilder_BendyComponent.NodePose
                    {
                        Index = x.Key,
                        Position = x.Value.Position,
                        Up = x.Value.Up
                    };
                }
            }
            return ob;
        }

        private void CacheMovableData(Dictionary<uint, MyObjectBuilder_BendyComponent.NodePose> movableNodes)
        {
            if (_nodeRefs == null || Definition == null || _nodeRefs.All(x => x.Node == null))
                return;
            movableNodes.Clear();
            for (var i = 0; i < Math.Min(Definition.Nodes.Count, _nodeRefs.Length); i++)
            {
                var node = _nodeRefs[i];
                if (Definition.Nodes[i].Movable && node.Node != null)
                {
                    var inv = Entity.PositionComp.WorldMatrixInvScaled;
                    movableNodes[(uint)i] = new MyObjectBuilder_BendyComponent.NodePose
                    {
                        Index = (uint)i,
                        Position = (Vector3)Vector3D.Transform(node.Node.Position, inv),
                        Up = (Vector3)Vector3D.TransformNormal(node.Node.Up, inv),
                        Tangent = node.Pinned ? (SerializableVector3?) (Vector3)Vector3D.TransformNormal(node.Node.Tangent, inv) : null,
                    };
                }
            }
        }

        private readonly Dictionary<uint, MyObjectBuilder_BendyComponent.NodePose> _movableNodeData =
            new Dictionary<uint, MyObjectBuilder_BendyComponent.NodePose>();

        public override void Deserialize(MyObjectBuilder_EntityComponent bbase)
        {
            base.Deserialize(bbase);
            var ob = (MyObjectBuilder_BendyComponent) bbase;
            lock (_movableNodeData)
            {
                _movableNodeData.Clear();
                if (ob.Overrides != null)
                    foreach (var k in ob.Overrides)
                        _movableNodeData[k.Index] = k;

                if (Entity != null && Entity.InScene)
                    ReloadNodesAndEdges(_movableNodeData);
            }
        }

        #endregion

        private NodeRef[] _nodeRefs;

        public NodeList Nodes => new NodeList(this);
        public Edge[] Edges { get; private set; }

        public event Action<BendyComponent> EdgeSetupChanged;
        public event Action<BendyComponent, Edge> EdgeRemoved;
        public event Action<BendyComponent, Edge> EdgeAdded;
        public event Action<BendyComponent, Node> NodeRemoved;
        public event Action<BendyComponent, Node> NodeAdded;

        public BendyLayer Graph { get; private set; }
        private MySkeletonComponent _skeletonComponent;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            Graph = MySession.Static.Components.Get<BendyController>().GetOrCreateLayer(Definition.Layer);
            _skeletonComponent = Entity.Components.Get<MySkeletonComponent>();
            lock (_movableNodeData)
            {
                ReloadNodesAndEdges(_movableNodeData);
            }

            if (_skeletonComponent != null)
                _skeletonComponent.OnReloadBones += OnBonesReloaded;
        }

        private void ReloadNodesAndEdges(Dictionary<uint, MyObjectBuilder_BendyComponent.NodePose> movableNodeData)
        {
            CloseNodesAndEdges(movableNodeData);
            if (Graph == null)
                return;
            var entityMatrix = Entity.PositionComp.WorldMatrix;

            for (var i = 0; i < _nodeRefs.Length; i++)
            {
                if (movableNodeData.TryGetValue((uint) i, out var data))
                {
                    var pos = Vector3D.Transform((Vector3)data.Position, ref entityMatrix);
                    var up = Vector3D.TransformNormal((Vector3)data.Up, ref entityMatrix);
                    var pin = data.Tangent.HasValue;
                    var node = Graph.GetOrCreateNode(pos, up, pin);
                    if (pin)
                    {
                        var tangent = Vector3D.TransformNormal((Vector3)data.Tangent, ref entityMatrix);
                        node.Pin(MatrixD.CreateWorld(pos, tangent, up));
                    }

                    _nodeRefs[i] = new NodeRef(node, pin);
                }
                else
                {
                    Assert.False(Definition.Nodes[i].Movable,
                        $"Creating movable bendy node {i} for entity {Entity}, component def {Definition.Id} without movable data");

                    var nodeMatrix = Definition.Nodes[i].Position * entityMatrix;
                    var pin = !Definition.Nodes[i].Movable;
                    var node = Graph.GetOrCreateNode(nodeMatrix.Translation, nodeMatrix.Up, pin);
                    if (pin)
                        node.Pin(nodeMatrix);

                    _nodeRefs[i] = new NodeRef(node, pin);
                }

                if (_nodeRefs[i].Node != null)
                    NodeAdded?.Invoke(this, _nodeRefs[i].Node);
            }

            for (var i = 0; i < Edges.Length; i++)
            {
                var def = Definition.Edges[i];
                var from = _nodeRefs[def.From].Node;
                var to = _nodeRefs[def.To].Node;
                Edges[i] = Graph.GetEdge(from, to) ?? Graph.CreateEdge(this, from, to, def.Mode, def.Control0, def.Control1);
                if (Edges[i] != null)
                {
                    EdgeAdded?.Invoke(this, Edges[i]);
                    Edges[i].CurveUpdated += OnCurveUpdated;
                }
            }

            EdgeSetupChanged?.Invoke(this);

            if (_skeletonComponent != null)
                OnBonesReloaded(_skeletonComponent);
        }

        private readonly HashSet<string> _dirtyBones = new HashSet<string>();

        private void MarkBoneDirty(string bone)
        {
            if (_skeletonComponent == null)
                return;
            if (_dirtyBones.Count == 0)
                AddScheduledCallback(ApplyPose, -1L);
            _dirtyBones.Add(bone);
        }

        private void OnCurveUpdated(Edge e)
        {
            if (Edges == null)
                return;
            EdgeSetupChanged?.Invoke(this);
            for (var i = 0; i < Edges.Length; i++)
                if (Edges[i] == e)
                {
                    var def = Definition.Edges[i];
                    if (def.Bones != null && def.Bones.Count > 0)
                        foreach (var b in def.Bones)
                            MarkBoneDirty(b);
                    break;
                }
        }

        private void OnBonesReloaded(MySkeletonComponent obj)
        {
            foreach (var bone in obj.CharacterBones)
                MarkBoneDirty(bone.Name);
        }

        [Update(false)]
        private void ApplyPose(long dt)
        {
            if (_skeletonComponent == null)
                return;

            foreach (var bone in _skeletonComponent.CharacterBones)
            {
                if (_dirtyBones.Contains(bone.Name))
                {
                    IReadOnlyList<BendyComponentDefinition.BoneEdgeMetadata> boneMeta;
                    if (Definition.BoneMetadata.TryGetValue(bone.Name, out boneMeta) && boneMeta != null &&
                        boneMeta.Count != 0)
                    {
                        var result = Matrix.Zero;
                        var weight = 0f;
                        foreach (var meta in boneMeta)
                        {
                            var edge = Edges?[meta.Edge];
                            if (edge?.Curve == null)
                                continue;
                            var pos = edge.Curve.Sample(meta.EdgeFactor);
                            var fwd = edge.Curve.SampleDerivative(meta.EdgeFactor);
                            var up = Vector3D.Lerp(edge.From.Up, edge.To.Up, meta.EdgeFactor);
                            var m = (Matrix) (MatrixD.CreateWorld(pos, fwd, up) *
                                              Entity.PositionComp.WorldMatrixInvScaled);
                            weight += meta.EdgeWeight;
                            result += m;
                        }

                        if (Math.Abs(weight) >= 1e-3f)
                        {
                            result = Matrix.Normalize(result / weight);
                            bone.SetTransformFromAbsoluteMatrix(ref result, false);
                        }
                    }
                }

                bone.ComputeAbsoluteTransform(null, false);
            }

            _skeletonComponent.ComputeAbsoluteTransforms();
            _dirtyBones.Clear();
        }

        public override void OnRemovedFromScene()
        {
            _skeletonComponent = null;
            lock (_movableNodeData)
            {
                CloseNodesAndEdges(_movableNodeData);
            }

            Graph = null;
            base.OnRemovedFromScene();
        }

        private void CloseNodesAndEdges(Dictionary<uint, MyObjectBuilder_BendyComponent.NodePose> movableNodeData)
        {
            CacheMovableData(movableNodeData);
            if (Edges != null)
                for (var i = 0; i < Edges.Length; i++)
                {
                    if (Edges[i] != null)
                    {
                        Edges[i].CurveUpdated -= OnCurveUpdated;
                        EdgeRemoved?.Invoke(this, Edges[i]);
                        Edges[i].Close();
                    }

                    Edges[i] = null;
                }

            // ReSharper disable once InvertIf
            if (_nodeRefs != null)
                for (var i = 0; i < _nodeRefs.Length; i++)
                {
                    ref var node = ref _nodeRefs[i];
                    if (node.Node != null)
                    {
                        NodeRemoved?.Invoke(this, node.Node);
                        if (node.Pinned)
                            node.Node.UnpinTangent();
                    }

                    _nodeRefs[i] = default;
                }

            EdgeSetupChanged?.Invoke(this);
        }

        public int IndexOfNode(Node n)
        {
            if (_nodeRefs == null)
                return -1;
            for (var i = 0; i < _nodeRefs.Length; i++)
                if (_nodeRefs[i].Node == n)
                    return i;
            return -1;
        }

        public int IndexOfEdge(Edge n)
        {
            if (Edges == null)
                return -1;
            for (var i = 0; i < Edges.Length; i++)
                if (Edges[i] == n)
                    return i;
            return -1;
        }

        private readonly struct NodeRef
        {
            public readonly Node Node;
            public readonly bool Pinned;
            public NodeRef(Node node, bool pinned)
            {
                Node = node;
                Pinned = pinned;
            }
        }
        

        public readonly struct NodeList : IReadOnlyList<Node>
        {
            private readonly BendyComponent _cmp;
            internal NodeList(BendyComponent cmp) => _cmp = cmp;
            
            public NodeEnumerator GetEnumerator() => new NodeEnumerator(_cmp);

            IEnumerator<Node> IEnumerable<Node>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public int Count => _cmp._nodeRefs.Length;
            public int Length => Count;

            public Node this[int index] => _cmp._nodeRefs[index].Node;

            public Node AccessSafe(int index) => index >= 0 && index < _cmp._nodeRefs.Length ? _cmp._nodeRefs[index].Node : default;
        }

        public struct NodeEnumerator : IEnumerator<Node>
        {
            private readonly BendyComponent _cmp;
            private int _index;
            internal NodeEnumerator(BendyComponent cmp)
            {
                _cmp = cmp;
                _index = -1;
            }

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if (_index + 1 >= _cmp._nodeRefs.Length) return false;
                _index++;
                return true;
            }

            public void Reset() => _index = -1;

            public Node Current => _cmp._nodeRefs[_index].Node;

            object IEnumerator.Current => Current;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_BendyComponent : MyObjectBuilder_EntityComponent
    {
        public struct NodePose
        {
            [XmlAttribute]
            public uint Index;

            public SerializableVector3 Position;
            public SerializableVector3 Up;
            public SerializableVector3? Tangent;
        }

        [XmlElement("Node")]
        public NodePose[] Overrides;
    }
}