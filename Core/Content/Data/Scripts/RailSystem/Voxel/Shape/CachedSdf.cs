using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Equinox76561198048419394.Core.Debug;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.RailSystem.Voxel.Shape;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Library.Collections;
using VRage.Library.Threading;
using VRage.Logging;
using VRage.Session;
using VRage.Voxels;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Voxel
{
    sealed class CachedSdf
    {
        private const int CellSizeBits = 3;
        public const int CellSize = 1 << CellSizeBits;

        private static NamedLogger Log => new NamedLogger(nameof(CachedSdf), MySession.Static.Log.Target);

        public enum CachedSdfGroup
        {
            Fill,
            Cut,
        }
        
        public readonly struct CachedSdfKey : IEquatable<CachedSdfKey>
        {
            public readonly MyVoxelBase Voxel;
            public readonly Vector3I Cell;
            public readonly CachedSdfGroup Group;

            public CachedSdfKey(MyVoxelBase voxel, Vector3I cell, CachedSdfGroup group)
            {
                Voxel = voxel;
                Cell = cell;
                Group = group;
            }

            public bool Equals(CachedSdfKey other) => Equals(Voxel, other.Voxel) && Cell.Equals(other.Cell) && Group == other.Group;

            public override bool Equals(object obj) => obj is CachedSdfKey other && Equals(other);

            public override int GetHashCode()
            {
                var hashCode = (Voxel != null ? Voxel.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Cell.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Group;
                return hashCode;
            }
        }

        private readonly HashSet<object> _shapeCacheKeys = new HashSet<object>();
        private readonly MyStorageData _sdf = new MyStorageData(MyStorageDataTypeFlags.Content);
        private readonly CachedSdfKey _key;

        private CachedSdf(CachedSdfKey key)
        {
            _key = key;
            _sdf.Resize(new Vector3I(CellSize));
        }

        private double GetTimestamp()
        {
            // return Stopwatch.GetTimestamp() / Stopwatch.Frequency;
            return 0;
        }

        private void SetContents(IGradeShape[] shapes)
        {
            var voxelMin = CellSize * _key.Cell;
            var voxelMax = CellSize * (_key.Cell + 1); // exclusive
            MyVoxelCoordSystems.VoxelCoordToWorldPosition(_key.Voxel.PositionLeftBottomCorner, ref voxelMin, out var worldMin);
            MyVoxelCoordSystems.VoxelCoordToWorldPosition(_key.Voxel.PositionLeftBottomCorner, ref voxelMax, out var worldMax);
            var worldBounds = new BoundingBoxD(worldMin, worldMax);
            var recalculating = _shapeCacheKeys.Count > 0;
            using (PoolManager.Get(out HashSet<object> relevantCacheKeys))
            {
                foreach (var shape in shapes)
                    shape.CollectCacheKeys((box, key) =>
                    {
                        if (box.Intersects(worldBounds))
                            relevantCacheKeys.Add(key);
                    });
                if (relevantCacheKeys.Count == _shapeCacheKeys.Count)
                {
                    var perfectMatch = true;
                    foreach (var key in relevantCacheKeys)
                        if (!_shapeCacheKeys.Contains(key))
                        {
                            perfectMatch = false;
                            break;
                        }

                    if (perfectMatch)
                    {
                        if (DebugFlags.Trace(typeof(CachedSdf)))
                            Log.Info($"Cached SDF reuse for {_key.Voxel.Id} at {_key.Cell} ({worldBounds.Center})");
                        return;
                    }
                }

                _shapeCacheKeys.Clear();
                foreach (var key in relevantCacheKeys)
                    _shapeCacheKeys.Add(key);
            }

            var start = GetTimestamp();
            var compositeHelper = CompositeGradeShape.Composite(shapes);
            var nonZeroPoints = 0;
            foreach (var localPoint in new BoundingBoxI(Vector3I.Zero, new Vector3I(CellSize)).EnumeratePoints())
            {
                var localPointRef = localPoint;
                var voxelPoint = voxelMin + localPoint;
                MyVoxelCoordSystems.VoxelCoordToWorldPosition(_key.Voxel.PositionLeftBottomCorner, ref voxelPoint, out var worldCoord);
                var density = (byte)(compositeHelper.GetDensity(worldCoord) * byte.MaxValue);
                _sdf.Set(MyStorageDataTypeEnum.Content, ref localPointRef, density);
                if (density != 0)
                    nonZeroPoints++;
            }

            if (DebugFlags.Debug(typeof(CachedSdf)))
            {
                var dt = (GetTimestamp() - start) * 1000.0;
                var type = recalculating ? "recalculate" : "calculate";
                Log.Info($"Cached SDF {type} for {_key.Voxel.Id} at {_key.Cell} ({worldBounds.Center}, {_shapeCacheKeys.Count} shapes, {nonZeroPoints} non-zero points, {dt} ms");
            }
        }

        public readonly struct CachedSdfAccessor
        {
            private readonly Vector3I _cellMin;
            private readonly Vector3I _cellTableSize;
            private readonly CachedSdf[] _cellTable;

            private int CellIndex(Vector3I cell)
            {
                var relCell = cell - _cellMin;
                return relCell.X + _cellTableSize.X * (relCell.Y + _cellTableSize.Y * relCell.Z);
            }
            
            public CachedSdfAccessor(
                LruCache<CachedSdfKey, CachedSdf> cache,
                MyVoxelBase voxel,
                CachedSdfGroup group,
                in Vector3I voxelMin,
                in Vector3I voxelMax,
                IGradeShape[] shapes)
            {
                _cellMin = voxelMin >> CellSizeBits;
                var cellMax = voxelMax >> CellSizeBits;
                _cellTableSize = cellMax + 1 - _cellMin;
                _cellTable = new CachedSdf[_cellTableSize.Volume()];
                foreach (var cell in new BoundingBoxI(_cellMin, cellMax + 1).EnumeratePoints())
                    _cellTable[CellIndex(cell)] = cache.GetOrCreate(new CachedSdfKey(voxel, cell, group), key => new CachedSdf(key));
                // Initialize SDF contents in parallel
                using (var waiter = new FastResourceLock())
                {
                    waiter.AcquireExclusive();
                    var waiting = _cellTable.Length;
                    MyAPIGateway.Parallel.ForEach(_cellTable, sdf =>
                    {
                        lock (sdf)
                        {
                            sdf.SetContents(shapes);
                        }
                        if (Interlocked.Decrement(ref waiting) == 0)
                            waiter.ReleaseExclusive();
                    });
                    waiter.AcquireExclusive();
                }
            }

            public byte QuerySdf(Vector3I voxelPoint)
            {
                var cell = voxelPoint >> CellSizeBits;
                var sdf = _cellTable[CellIndex(cell)];
                var localCoord = voxelPoint & (CellSize - 1);
                return sdf._sdf.Get(MyStorageDataTypeEnum.Content, ref localCoord);
            }
        }
    }
}