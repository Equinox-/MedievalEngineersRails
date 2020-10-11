using System;
using Equinox76561198048419394.Core.Util;
using VRage.Game.Components;

namespace Equinox76561198048419394.RailSystem.Util
{
    public sealed class RootEntityRef : IDisposable
    {
        private readonly MyHierarchyComponent.ParentChangedEvent _markDirty;
        private int _observingCount;
        private StackArray<MyHierarchyComponent> _observing;

        public event MyHierarchyComponent.ParentChangedEvent RootParentChanged;
        public MyHierarchyComponent RootParent { get; private set; }
        public MyHierarchyComponent Self { get; }

        public RootEntityRef(MyHierarchyComponent self)
        {
            Self = self;
            _markDirty = (target, oldParent, newParent) => RecomputeRootParent();
            RecomputeRootParent();
        }

        private void RecomputeRootParent()
        {
            var depth = 0;
            var node = Self;
            var root = Self;
            while (node != null)
            {
                root = node;
                var currentAtLevel = _observing[depth];
                if (currentAtLevel != node)
                {
                    if (currentAtLevel != null)
                        currentAtLevel.ParentChanged -= _markDirty;
                    _observing[depth] = node;
                    node.ParentChanged += _markDirty;
                }
                node = node.Parent;
                depth++;
            }

            for (var i = depth; i < _observingCount; i++)
            {
                var currentAtLevel = _observing[depth];
                // Will never be null since we're still less than observer count
                currentAtLevel.ParentChanged -= _markDirty;
            }
            _observingCount = depth;

            var prevRoot = RootParent;
            if (root == prevRoot) return;
            RootParent = root;
            RootParentChanged?.Invoke(Self, prevRoot, root);
        }

        public void Dispose()
        {
            for (var i = 0; i < _observingCount; i++)
                _observing[i].ParentChanged -= _markDirty;
            _observingCount = 0;
            RootParent = Self;
        }
    }
}