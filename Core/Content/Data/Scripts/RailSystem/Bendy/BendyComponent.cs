using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.ModAPI;
using VRage;
using VRage.Components.Entity.Animations;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Logging;
using VRage.ObjectBuilders;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy
{
    [MyComponent(typeof(MyObjectBuilder_BendyComponent))]
    [MyDefinitionRequired]
    [MyDependency(typeof(MySkeletonComponent), Critical = false)]
    public class BendyComponent : MyEntityComponent
    {
        public BendyComponentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (BendyComponentDefinition) def;
            _nodes = new Node[Definition.Nodes.Count];
            _edges = new Edge[Definition.Edges.Count];
        }

        #region Serialization

        public override bool IsSerialized => Definition?.Nodes != null && Definition.Nodes.Any(x => x.Movable);

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            if (Entity != null && Entity.InScene)
                CacheMovableData();
            return new MyObjectBuilder_BendyComponent
            {
                Overrides = _movableNodeData.Select(x => new MyObjectBuilder_BendyComponent.NodePose
                {
                    Index = x.Key,
                    Position = x.Value.Position,
                    Up = x.Value.Up
                }).ToArray()
            };
        }

        private void CacheMovableData()
        {
            if (_nodes == null || Definition == null || _nodes.All(x => x == null))
                return;
            _movableNodeData.Clear();
            for (var i = 0; i < Math.Min(Definition.Nodes.Count, _nodes.Length); i++)
                if (Definition.Nodes[i].Movable && _nodes[i] != null)
                {
                    var inv = Entity.PositionComp.WorldMatrixInvScaled;
                    _movableNodeData[(uint) i] = new MyObjectBuilder_BendyComponent.NodePose
                    {
                        Index = (uint) i,
                        Position = (Vector3) Vector3D.Transform(_nodes[i].Position, inv),
                        Up = (Vector3) Vector3D.TransformNormal(_nodes[i].Up, inv)
                    };
                }
        }

        private readonly Dictionary<uint, MyObjectBuilder_BendyComponent.NodePose> _movableNodeData =
            new Dictionary<uint, MyObjectBuilder_BendyComponent.NodePose>();

        public override void Deserialize(MyObjectBuilder_EntityComponent bbase)
        {
            base.Deserialize(bbase);
            var ob = (MyObjectBuilder_BendyComponent) bbase;
            _movableNodeData.Clear();
            if (ob.Overrides != null)
                foreach (var k in ob.Overrides)
                    _movableNodeData[k.Index] = k;

            if (Entity != null && Entity.InScene)
                ReloadNodesAndEdges();
        }

        #endregion

        public IReadOnlyList<Node> Nodes => _nodes;
        public IReadOnlyList<Edge> Edges => _edges;

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
            ReloadNodesAndEdges();
            if (_skeletonComponent != null)
                _skeletonComponent.OnReloadBones += OnBonesReloaded;
        }

        private Node[] _nodes;
        private Edge[] _edges;

        private void ReloadNodesAndEdges()
        {
            CloseNodesAndEdges();
            if (Graph == null)
                return;
            var entityMatrix = Entity.PositionComp.WorldMatrix;

            for (var i = 0; i < _nodes.Length; i++)
            {
                MyObjectBuilder_BendyComponent.NodePose data;
                if (_movableNodeData.TryGetValue((uint) i, out data))
                {
                    _nodes[i] = Graph.GetOrCreateNode(Vector3D.Transform((Vector3) data.Position, ref entityMatrix),
                        Vector3D.Transform((Vector3) data.Up, ref entityMatrix));
                }
                else
                {
                    Assert.False(Definition.Nodes[i].Movable,
                        $"Creating movable bendy node {i} for entity {Entity}, component def {Definition.Id} without movable data");

                    var nodeMatrix = Definition.Nodes[i].Position * entityMatrix;
                    _nodes[i] = Graph.GetOrCreateNode(nodeMatrix.Translation, nodeMatrix.Up);
                    if (!Definition.Nodes[i].Movable)
                        _nodes[i].Pin(nodeMatrix);
                }

                if (_nodes[i] != null)
                    NodeAdded?.Invoke(this, _nodes[i]);
            }

            for (var i = 0; i < _edges.Length; i++)
            {
                var def = Definition.Edges[i];
                var from = _nodes[def.From];
                var to = _nodes[def.To];
                _edges[i] = Graph.GetEdge(from, to) ?? Graph.CreateEdge(this, from, to, def.Mode);
                if (_edges[i] != null)
                {
                    EdgeAdded?.Invoke(this, _edges[i]);
                    _edges[i].CurveUpdated += OnCurveUpdated;
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
            if (_edges == null)
                return;
            EdgeSetupChanged?.Invoke(this);
            for (var i = 0; i < _edges.Length; i++)
                if (_edges[i] == e)
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
                            var edge = _edges?[meta.Edge];
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
            base.OnRemovedFromScene();
            _skeletonComponent = null;
            CloseNodesAndEdges();
            Graph = null;
        }

        private void CloseNodesAndEdges()
        {
            CacheMovableData();
            if (_edges != null)
                for (var i = 0; i < _edges.Length; i++)
                {
                    if (_edges[i] != null)
                    {
                        _edges[i].CurveUpdated -= OnCurveUpdated;
                        EdgeRemoved?.Invoke(this, _edges[i]);
                        _edges[i].Close();
                    }

                    _edges[i] = null;
                }

            // ReSharper disable once InvertIf
            if (_nodes != null)
                for (var i = 0; i < _nodes.Length; i++)
                {
                    if (_nodes[i] != null)
                    {
                        NodeRemoved?.Invoke(this, _nodes[i]);
                        if (!Definition.Nodes[i].Movable)
                            _nodes[i].UnpinTangent();
                    }

                    _nodes[i] = null;
                }

            EdgeSetupChanged?.Invoke(this);
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
        }

        [XmlElement("Node")]
        public NodePose[] Overrides;
    }
}