using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Util;
using VRage;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Scene;
using VRage.Scene;
using PoolManager = VRage.Library.Collections.PoolManager;

namespace Equinox76561198048419394.RailSystem.Physics
{
    [MyGroup(typeof(MyObjectBuilder_RailwayPhysicsGroup))]
    public sealed class RailwayPhysicsGroup : MyGroupMultiLink<RailwayPhysicsLinkData>
    {
        public new HashSetReader<RailwayPhysicsLinkData> GetLinks(MyEntity left, MyEntity right)
        {
            return LinkData.TryGet(new Edge(left, right), out var links) ? links : default(HashSetReader<RailwayPhysicsLinkData>);
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailwayPhysicsGroup : MyObjectBuilder_GroupMultiLink<RailwayPhysicsLinkData>
    {
    }

    [RpcSerializable]
    public struct RailwayPhysicsLinkData : IEquatableAndHash<RailwayPhysicsLinkData>
    {
        [DefaultComparer]
        public static readonly IEqualityComparer<RailwayPhysicsLinkData> Comparer = EquatableAndHashComparer<RailwayPhysicsLinkData>.Instance;

        [XmlAttribute("F")]
        public long FromQualifier;

        [XmlAttribute("T")]
        public long ToQualifier;

        public bool Equals(RailwayPhysicsLinkData other) => FromQualifier == other.FromQualifier && ToQualifier == other.ToQualifier;

        public override bool Equals(object obj) => obj is RailwayPhysicsLinkData other && Equals(other);

        public override int GetHashCode() => (FromQualifier.GetHashCode() * 137) ^ ToQualifier.GetHashCode();

        [Pure]
        public bool TryGetOpposition(long entityId, out long opposingEntityId)
        {
            if (FromQualifier == entityId)
            {
                opposingEntityId = ToQualifier;
                return true;
            }

            if (ToQualifier == entityId)
            {
                opposingEntityId = FromQualifier;
                return true;
            }

            opposingEntityId = default;
            return false;
        }
    }

    public interface IRailPhysicsComponent
    {
        RailPhysicsNode PhysicsNode { get; }
    }

    public sealed class RailPhysicsNode
    {
        private readonly MyHierarchyComponent.ParentChangedEvent _rootParentChanged;

        private readonly MyEntityComponent _target;
        private RootEntityRef _rootRef;

        private static bool IsAuthority => MyMultiplayer.IsServer;

        public RailPhysicsNode(MyEntityComponent target)
        {
            _rootParentChanged = RootRefChanged;
            _target = target;
        }

        public void AddToContainer()
        {
            _rootRef = new RootEntityRef(_target.Entity.Hierarchy);
            if (!IsAuthority)
                return;
            _rootRef.RootParentChanged += _rootParentChanged;
        }

        public void RemoveFromContainer()
        {
            _rootRef.RootParentChanged -= _rootParentChanged;
            _rootRef = null;
        }

        private RailwayPhysicsLinkData GetLinkData(RailPhysicsNode other)
        {
            return new RailwayPhysicsLinkData
            {
                FromQualifier = Math.Min(_target.Entity.EntityId, other._target.Entity.EntityId),
                ToQualifier = Math.Max(_target.Entity.EntityId, other._target.Entity.EntityId)
            };
        }

        public void Link(RailPhysicsNode other)
        {
            if (!IsAuthority)
                return;
            var selfRoot = _rootRef?.RootParent?.Entity;
            var otherRoot = other._rootRef?.RootParent?.Entity;
            if (selfRoot == null || otherRoot == null)
                return;
            var linkData = GetLinkData(other);
            _target.Scene.AddLink<RailwayPhysicsGroup, MyEntity, RailwayPhysicsLinkData>(selfRoot, otherRoot, linkData);
        }

        public void Unlink(RailPhysicsNode other)
        {
            if (!IsAuthority)
                return;
            var selfRoot = _rootRef?.RootParent?.Entity;
            var otherRoot = other._rootRef?.RootParent?.Entity;
            if (selfRoot == null || otherRoot == null)
                return;
            var linkData = GetLinkData(other);
            _target.Scene.RemoveLink<RailwayPhysicsGroup, MyEntity, RailwayPhysicsLinkData>(selfRoot, otherRoot, linkData);
        }

        public void GetNeighbors(HashSet<RailPhysicsNode> neighbors)
        {
            var selfRoot = _rootRef?.RootParent?.Entity;
            if (selfRoot == null)
                return;
            var selfGroup = _target.Scene.GetGroup<RailwayPhysicsGroup, MyEntity>(selfRoot);
            if (selfGroup == null)
                return;
            var eid = _target.Entity.EntityId;
            foreach (var neighbor in selfGroup.GetNeighbors(selfRoot))
            foreach (var link in selfGroup.GetLinks(selfRoot, neighbor))
                if (link.TryGetOpposition(eid, out var opposing)
                    && _target.Scene.TryGetEntity(opposing, out var entity)
                    && entity.Components.TryGet(out IRailPhysicsComponent component))
                    neighbors.Add(component.PhysicsNode);
        }

        private void RootRefChanged(MyHierarchyComponent target, MyHierarchyComponent oldParent, MyHierarchyComponent newParent)
        {
            if (!IsAuthority)
                return;
            using (PoolManager.Get(out List<MyTuple<MyEntity, RailwayPhysicsLinkData>> moving))
            {
                var oldRoot = oldParent?.Entity;
                if (oldRoot != null)
                {
                    var oldGroup = _target.Scene.GetGroup<RailwayPhysicsGroup, MyEntity>(oldRoot);
                    if (oldGroup != null)
                    {
                        foreach (var neighbor in oldGroup.GetNeighbors(oldRoot))
                        foreach (var link in oldGroup.GetLinks(oldRoot, neighbor))
                            moving.Add(MyTuple.Create(neighbor, link));

                        foreach (var link in moving)
                            _target.Scene.RemoveLink<RailwayPhysicsGroup, MyEntity, RailwayPhysicsLinkData>(oldRoot, link.Item1, link.Item2);
                    }
                }

                var newRoot = newParent?.Entity;
                if (newRoot == null) return;
                foreach (var link in moving)
                    _target.Scene.AddLink<RailwayPhysicsGroup, MyEntity, RailwayPhysicsLinkData>(newRoot, link.Item1,
                        link.Item2);
            }
        }
    }
}