using System;
using System.Collections;
using System.Collections.Generic;
using VRage.Collections;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util
{
    public static class DbvtExtensions
    {
        private static readonly MyConcurrentQueue<NearestNodeQuery> _cache = new MyConcurrentQueue<NearestNodeQuery>();

        public struct NearestNodeResult
        {
            public readonly int NodeIndex;
            public readonly object UserData;
            public readonly double DistanceSquared;

            public NearestNodeResult(int idx, double distSq, object userData)
            {
                NodeIndex = idx;
                UserData = userData;
                DistanceSquared = distSq;
            }
        }

        public static IEnumerator<NearestNodeResult> SortedByDistance(this MyDynamicAABBTreeD tree, Vector3D test, double maxDistanceSq = double.PositiveInfinity)
        {
            NearestNodeQuery query;
            if (!_cache.TryDequeue(out query))
                query = new NearestNodeQuery(_cache);
            query.Init(test, tree, maxDistanceSq);
            return query;
        }

        private class NearestNodeQuery : IEnumerator<NearestNodeResult>
        {
            private Vector3D _vec;
            private MyDynamicAABBTreeD _tree;
            private double _maxDistanceSq;
            private readonly MyConcurrentQueue<NearestNodeQuery> _cache;
            private readonly MyBinaryHeap<double, int> _tmp = new MyBinaryHeap<double, int>();

            public NearestNodeQuery(MyConcurrentQueue<NearestNodeQuery> cache)
            {
                _cache = cache;
            }

            public void Init(Vector3D v, MyDynamicAABBTreeD tree, double maxDistanceSq)
            {
                _vec = v;
                _tree = tree;
                _maxDistanceSq = maxDistanceSq;
                Reset();
            }

            private void Insert(int node)
            {
                var box = _tree.GetAabb(node);
                Vector3D tmp;
                Vector3D.Clamp(ref _vec, ref box.Min, ref box.Max, out tmp);
                var dist = Vector3D.DistanceSquared(tmp, _vec);
                if (dist <= _maxDistanceSq)
                    _tmp.Insert(node, dist);
            }

            public void Dispose()
            {
                _tmp.Clear();
                _tree = null;
                if (_cache.Count < 12)
                    _cache.Enqueue(this);
            }

            public bool MoveNext()
            {
                while (_tmp.Count > 0)
                {
                    var minKey = _tmp.MinKey();
                    var min = _tmp.RemoveMin();
                    int child1, child2;
                    _tree.GetChildren(min, out child1, out child2);
                    if (child1 == -1)
                    {
                        Current = new NearestNodeResult(min, minKey, _tree.GetUserData<object>(min));
                        return true;
                    }

                    Insert(child1);
                    Insert(child2);
                }

                Current = default(NearestNodeResult);
                return false;
            }

            public void Reset()
            {
                _tmp.Clear();
                var rt = _tree.GetRoot();
                if (rt >= 0)
                    Insert(rt);
            }

            public NearestNodeResult Current { get; private set; }

            object IEnumerator.Current => Current;
        }
    }
}