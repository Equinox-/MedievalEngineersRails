using System;
using System.Collections.Generic;
using System.Threading;
using Equinox76561198048419394.Core.Debug;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.RailSystem.Voxel.Shape;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Library.Collections;
using VRage.Library.Threading;
using VRage.Logging;
using VRage.Session;
using VRage.Voxels;
using VRageMath;
using VRageRender;

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

            public override string ToString() => $"[{Voxel?.EntityId ?? default}/{Group}/{Cell}]";
        }

        private readonly HashSet<object> _shapeCacheKeys = new HashSet<object>();
        private readonly List<BoundingBoxD> _shapeCacheBoxes = new List<BoundingBoxD>();
        private readonly MyStorageData _sdf = new MyStorageData(MyStorageDataTypeFlags.Content);
        private readonly CachedSdfKey _key;
        private readonly List<IGradeShape> _shapes = new List<IGradeShape>();
        private int _generation;

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

        private static readonly ListReader<Vector3> AxisOptions = new List<Vector3>
        {
            Vector3.Right,
            Vector3.Up,
            Vector3.Forward,
        };

        public void DebugDraw()
        {
            if (_shapeCacheBoxes.Count == 0) return;
            var voxelMin = CellSize * _key.Cell;
            var worldPos = MyAPIGateway.Session?.ControlledObject?.GetPosition();
            if (worldPos.HasValue)
            {
                var worldPosVal = worldPos.Value;
                MyVoxelCoordSystems.WorldPositionToVoxelCoord(_key.Voxel.PositionLeftBottomCorner, ref worldPosVal, out var voxelCoord);
                foreach (var voxelPt in new BoundingBoxI(voxelCoord - 1, voxelCoord + 2).EnumeratePoints())
                {
                    var localPt = voxelPt - voxelMin;
                    if (!new BoundingBoxI(Vector3I.Zero, new Vector3I(CellSize - 1)).Contains(localPt))
                        continue;
                    var voxelPtTmp = voxelPt;
                    MyVoxelCoordSystems.VoxelCoordToWorldPosition(_key.Voxel.PositionLeftBottomCorner, ref voxelPtTmp, out var queryCoord);
                    var density = _sdf.Get(MyStorageDataTypeEnum.Content, ref localPt);
                    if (density == 0) continue;
                    foreach (var shape in _shapes)
                        shape.DrawQuery(queryCoord);
                    break;
                }
            }
            if (RailConstants.Debug.DrawSdfDensityField)
            {
                using (var batch = MyRenderProxy.DebugDrawLine3DOpenBatch(true))
                {
                    foreach (var localPoint in new BoundingBoxI(Vector3I.Zero, new Vector3I(CellSize)).EnumeratePoints())
                    {
                        var localPointRef = localPoint;
                        var voxelPoint = voxelMin + localPoint;
                        MyVoxelCoordSystems.VoxelCoordToWorldPosition(_key.Voxel.PositionLeftBottomCorner, ref voxelPoint, out var worldCoord);
                        var density = _sdf.Get(MyStorageDataTypeEnum.Content, ref localPointRef);
                        if (density <= 0) continue;
                        var size = 0.25f * density / 255;
                        foreach (var axis in AxisOptions)
                        {
                            var space = size * axis;
                            batch.AddLine(worldCoord - space, Color.Aqua, worldCoord + space, Color.Aqua);
                        }
                    }
                }
                return;
            }
            var voxelMax = CellSize * (_key.Cell + 1); // exclusive
            MyVoxelCoordSystems.VoxelCoordToWorldPosition(_key.Voxel.PositionLeftBottomCorner, ref voxelMin, out var worldMin);
            MyVoxelCoordSystems.VoxelCoordToWorldPosition(_key.Voxel.PositionLeftBottomCorner, ref voxelMax, out var worldMax);
            var worldBounds = new BoundingBoxD(Vector3D.Min(worldMin, worldMax), Vector3D.Max(worldMin, worldMax));
            MyRenderProxy.DebugDrawAABB(worldBounds, Color.Green);
            foreach (var shape in _shapeCacheBoxes)
                MyRenderProxy.DebugDrawAABB(shape, Color.Red);
            MyRenderProxy.DebugDrawText3D(worldBounds.Center, $"Gen {_generation}", Color.Blue, 0.5f);
        }

        private void SetContents(List<IGradeShape> shapes)
        {
            var voxelMin = CellSize * _key.Cell;
            var voxelMax = CellSize * (_key.Cell + 1); // exclusive
            MyVoxelCoordSystems.VoxelCoordToWorldPosition(_key.Voxel.PositionLeftBottomCorner, ref voxelMin, out var worldMin);
            MyVoxelCoordSystems.VoxelCoordToWorldPosition(_key.Voxel.PositionLeftBottomCorner, ref voxelMax, out var worldMax);
            var worldBounds = new BoundingBoxD(Vector3D.Min(worldMin, worldMax), Vector3D.Max(worldMin, worldMax));
            var recalculating = _shapeCacheKeys.Count > 0;
            using (PoolManager.Get(out HashSet<object> relevantCacheKeys))
            {
                _shapeCacheBoxes.Clear();
                foreach (var shape in shapes)
                    shape.CollectCacheKeys((box, key) =>
                    {
                        if (box.Intersects(worldBounds))
                        {
                            _shapeCacheBoxes.Add(box);
                            relevantCacheKeys.Add(key);
                        }
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
                            Log.Info($"Cached SDF reuse for {_key} ({worldBounds.Center})");
                        return;
                    }
                }

                _generation++;
                _shapeCacheKeys.Clear();
                foreach (var key in relevantCacheKeys)
                    _shapeCacheKeys.Add(key);
            }
            _shapes.Clear();
            _shapes.AddCollection(shapes);

            if (_shapeCacheKeys.Count == 0)
            {
                _sdf.ClearContent(0);
                return;
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
                Log.Info($"Cached SDF {type} for {_key} ({worldBounds.Center}, {_shapeCacheKeys.Count} shapes, {nonZeroPoints} non-zero points, {dt} ms");
            }
        }

        private static readonly MyParallelTask Parallel = new MyParallelTask();

        public readonly struct CachedSdfAccessor
        {
            private readonly Vector3I _cellMin;
            private readonly Vector3I _cellTableSize;
            private readonly CachedSdf[] _cellTable;

            public IReadOnlyList<CachedSdf> CellTable => _cellTable;

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
                List<IGradeShape> shapes)
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
                    Parallel.ForEach(_cellTable, sdf =>
                    {
                        try
                        {
                            lock (sdf)
                            {
                                sdf.SetContents(shapes);
                            }
                        }
                        catch (Exception err)
                        {
                            Log.Warning($"Failed to generate SDF {sdf._key} for {shapes}: {err}");
                        }
                        finally
                        {
                            if (Interlocked.Decrement(ref waiting) == 0)
                                waiter.ReleaseExclusive();
                        }
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