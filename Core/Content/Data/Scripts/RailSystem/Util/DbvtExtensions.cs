using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Collections;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util
{
    public static class DbvtExtensions
    {
        private static readonly MyConcurrentQueue<NearestNodeQuery> _cache = new MyConcurrentQueue<NearestNodeQuery>();

        public static IEnumerator<KeyValuePair<int, double>> SortedByDistance(this MyDynamicAABBTreeD tree,
            Vector3D test)
        {
            NearestNodeQuery query;
            if (!_cache.TryDequeue(out query))
                query = new NearestNodeQuery(_cache);
            query.Init(test, tree);
            return query;
        }

        private class NearestNodeQuery : IEnumerator<KeyValuePair<int, double>>
        {
            private Vector3D _vec;
            private MyDynamicAABBTreeD _tree;
            private readonly MyConcurrentQueue<NearestNodeQuery> _cache;
            private readonly MyBinaryHeap<double, int> _tmp = new MyBinaryHeap<double, int>();

            public NearestNodeQuery(MyConcurrentQueue<NearestNodeQuery> cache)
            {
                _cache = cache;
            }

            public void Init(Vector3D v, MyDynamicAABBTreeD tree)
            {
                _vec = v;
                _tree = tree;
                Reset();
            }

            private void Insert(int node)
            {
                var box = _tree.GetAabb(node);
                Vector3D tmp;
                Vector3D.Clamp(ref _vec, ref box.Min, ref box.Max, out tmp);
                var dist = Vector3D.DistanceSquared(tmp, _vec);
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
                        Current = new KeyValuePair<int, double>(min, minKey);
                        return true;
                    }

                    Insert(child1);
                    Insert(child2);
                }

                Current = default(KeyValuePair<int, double>);
                return false;
            }

            public void Reset()
            {
                _tmp.Clear();
                var rt = _tree.GetRoot();
                if (rt >= 0)
                    Insert(rt);
            }

            public KeyValuePair<int, double> Current { get; private set; }

            object IEnumerator.Current => Current;
        }
    }
}