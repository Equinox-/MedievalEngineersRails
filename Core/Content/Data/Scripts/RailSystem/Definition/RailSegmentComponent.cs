using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Bendy;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Network;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.RailSystem.Definition
{
    [MyComponent(typeof(MyObjectBuilder_RailSegmentComponent))]
    [MyDefinitionRequired(typeof(RailSegmentDefinition))]
    [MyDependency(typeof(BendyComponent))]
    [ReplicatedComponent]
    public class RailSegmentComponent : MyEntityComponent, IMyEventProxy, IComponentDebugDraw
    {
        public RailSegmentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            Definition = (RailSegmentDefinition) definition;
        }

        private BendyComponent _bendy;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _bendy = Container.Get<BendyComponent>();
            
            AddScheduledCallback(FirstUpdate);
        }

        [Update(false)]
        private void FirstUpdate(long dt)
        {
            // Apply switch table a bit in the future (once the nodes have settled)
            AddScheduledCallback(ApplySwitchTableFuture, MyEngineConstants.UPDATE_STEP_SIZE_IN_MILLISECONDS * 4);
        }

        [Update(false)]
        private void ApplySwitchTableFuture(long dt)
        {
            ApplySwitchTable();
        }

        public void DebugDraw()
        {
            if (((IMyUtilities) MyAPIUtilities.Static).IsDedicated)
                return;
            if (!RailConstants.Debug.DrawSwitchControllers)
                return;
            foreach (var node in _bendy.Nodes)
            {
                var data = node.Get<SwitchableNodeData>();
                data?.DebugDraw();
            }
        }

        internal static void RequestSwitchNet(Node junction, Edge target)
        {
            var ownerBendy = target.Owner;
            var ownerSegment = ownerBendy?.Container?.Get<RailSegmentComponent>();
            if (ownerSegment == null)
                return;

            var targetNode = target.Opposition(junction);

            var junctionIndex = ownerBendy.IndexOfNode(junction);
            var targetIndex = ownerBendy.IndexOfNode(targetNode);
            if (junctionIndex == -1 || targetIndex == -1)
                return;

            MyMultiplayerModApi.Static.RaiseEvent(ownerSegment, x => x.SwitchNet, junctionIndex, targetIndex);
        }

        [Event]
        [Server]
        [Broadcast]
        private void SwitchNet(int junction, int target)
        {
            if (_bendy?.Nodes == null)
            {
                MyEventContext.ValidationFailed();
                return;
            }
            if (junction >= _bendy.Nodes.Length)
            {
                MyEventContext.ValidationFailed();
                return;
            }
            if (target >= _bendy.Nodes.Length)
            {
                MyEventContext.ValidationFailed();
                return;
            }
            var junctionNode = _bendy.Nodes[junction];
            var targetNode = _bendy.Nodes[target];
            var edge = junctionNode.ConnectionTo(targetNode);
            if (edge == null)
            {
                MyEventContext.ValidationFailed();
                return;
            }
            SwitchableNodeData.GetOrCreate(junctionNode).SideOrCreateFor(targetNode).SwitchToInternal(edge);
        }

        #region Save

        private List<MyObjectBuilder_RailSegmentComponent.SwitchedNode> _nodeSwitchData;

        private void ApplySwitchTable()
        {
            if (_nodeSwitchData == null)
                return;
            if (_bendy == null)
                return;
            foreach (var k in _nodeSwitchData)
            {
                var junction = _bendy.Nodes.AccessSafe(k.Junction);
                var dest = _bendy.Nodes.AccessSafe(k.Destination);
                if (junction != null && dest != null)
                    junction.SwitchTo(dest);
            }

            _nodeSwitchData = null;
        }

        public override bool IsSerialized
        {
            get
            {
                if (_nodeSwitchData != null)
                    return true;
                if (_bendy == null)
                    return false;
                foreach (var k in _bendy.Nodes)
                {
                    var switchData = k.Get<SwitchableNodeData>();
                    if (switchData?.Positive != null && switchData.Positive.IsRequired && _bendy.IndexOfEdge(switchData.Positive.SwitchedEdge) != -1)
                        return true;
                    if (switchData?.Negative != null && switchData.Negative.IsRequired && _bendy.IndexOfEdge(switchData.Negative.SwitchedEdge) != -1)
                        return true;
                }

                return false;
            }
        }

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_RailSegmentComponent) base.Serialize(copy);
            ob.Switched = new List<MyObjectBuilder_RailSegmentComponent.SwitchedNode>();

            if (_bendy == null)
                return ob;
            for (var junctionIndex = 0; junctionIndex < _bendy.Nodes.Length; junctionIndex++)
            {
                var junction = _bendy.Nodes[junctionIndex];
                var switchData = junction.Get<SwitchableNodeData>();
                if (switchData?.Positive != null && switchData.Positive.IsRequired)
                {
                    var target = _bendy.IndexOfNode(switchData.Positive.Target);
                    if (target >= 0)
                    {
                        ob.Switched.Add(new MyObjectBuilder_RailSegmentComponent.SwitchedNode
                        {
                            Junction = junctionIndex,
                            Destination = target
                        });
                    }
                }

                // ReSharper disable once InvertIf
                if (switchData?.Negative != null && switchData.Negative.IsRequired)
                {
                    var target = _bendy.IndexOfNode(switchData.Negative.Target);
                    if (target >= 0)
                    {
                        ob.Switched.Add(new MyObjectBuilder_RailSegmentComponent.SwitchedNode
                        {
                            Junction = junctionIndex,
                            Destination = target
                        });
                    }
                }
            }

            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            _nodeSwitchData = ((MyObjectBuilder_RailSegmentComponent) builder).Switched;
            ApplySwitchTable();
        }

        #endregion
    }


    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailSegmentComponent : MyObjectBuilder_EntityComponent
    {
        public struct SwitchedNode
        {
            public int Junction;
            public int Destination;
        }

        [XmlElement("Switch")]
        public List<SwitchedNode> Switched;
    }
}