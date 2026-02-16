using System;
using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.Core.UI;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.RailSystem.Util;
using Equinox76561198048419394.RailSystem.Voxel.Shape;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Entity.Camera;
using VRage.Components.Entity.CubeGrid;
using VRage.Components.Session;
using VRage.Entities.Gravity;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.Logging;
using VRage.Network;
using VRage.Scene;
using VRage.Serialization;
using VRage.Session;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;
using MySession = VRage.Session.MySession;

namespace Equinox76561198048419394.RailSystem.Voxel
{
    [MySessionComponent(AllowAutomaticCreation = true, AlwaysOn = true)]
    [StaticEventOwner]
    public sealed class RailGraderSystem : MySessionComponent, IComponentDebugDraw
    {
        private static NamedLogger Log
        {
            get
            {
                var namedLogger = MySession.Static.Log;
                return new NamedLogger(in namedLogger, nameof(RailGraderSystem));
            }
        }

        private readonly MyStorageData _storage = new MyStorageData();

        // ~4MB of SDF data
        private readonly LruCache<CachedSdf.CachedSdfKey, CachedSdf> _sdfCache = new LruCache<CachedSdf.CachedSdfKey, CachedSdf>(1024);

        private const double GradeScanDistance = 25D;

        internal SimpleDropdownDataSourceFactory<MyStringHash> SupportPresets { get; private set; }
        internal SimpleDropdownDataSourceFactory<MyStringHash> ExcavationPresets { get; private set; }

        protected override void OnSessionReady()
        {
            base.OnSessionReady();

            // Index presets.
            var allSupport = new List<RailGradeComponentDefinition.RailGradePreset>();
            var allExcavation = new List<RailGradeComponentDefinition.RailGradePreset>();
            var seenSupport = new HashSet<MyStringHash>();
            var seenExcavation = new HashSet<MyStringHash>();
            foreach (var def in MyDefinitionManager.GetOfType<RailGradeComponentDefinition>())
            {
                foreach (var preset in def.SupportPresets.Values)
                    if (seenSupport.Add(preset.Name))
                        allSupport.Add(preset);
                foreach (var preset in def.ExcavatePresets.Values)
                    if (seenExcavation.Add(preset.Name))
                        allExcavation.Add(preset);
            }

            SupportPresets = PresetDataSource(allSupport);
            ExcavationPresets = PresetDataSource(allExcavation);
            return;

            SimpleDropdownDataSourceFactory<MyStringHash> PresetDataSource(ListReader<RailGradeComponentDefinition.RailGradePreset> presets)
                => SimpleDataSources.DropdownFactory(presets.Select(x => MyTuple.Create(
                    x.Name, new ContextMenuDropdownDataSource.DropdownItem(MyStringId.GetOrCompute(x.Name.String),
                        string.IsNullOrEmpty(x.Tooltip) ? (MyStringId?)null : MyStringId.GetOrCompute(x.Tooltip)))));
        }

        protected override void OnUnload()
        {
            _sdfCache.Clear();
            base.OnUnload();
        }

        public static void GatherGradeComponents(in Vector3D pos, List<RailGradeComponent> output, float radius)
        {
            using (PoolManager.Get(out List<MyEntity> entities))
            {
                MyGamePruningStructure.GetTopMostEntitiesInSphere(
                    // Gather for a larger radius so we minimize the invalidation performed on the SDF cache
                    new BoundingSphereD(pos, Math.Max(radius, CachedSdf.CellSize) + GradeScanDistance), entities);
                foreach (var ent in entities)
                    GatherGradeComponents(in pos, ent, output, radius);
            }
        }

        private static void GatherGradeComponents(in Vector3D pos, MyEntity entity, List<RailGradeComponent> output, float radius)
        {
            foreach (var c in entity.Components)
                if (c is RailGradeComponent k)
                    output.Add(k);
            if (!entity.Components.TryGet(out MyGridHierarchyComponent hierarchyComponent)) return;
            using (PoolManager.Get(out List<MyEntity> children))
            {
                hierarchyComponent.QuerySphere(new BoundingSphereD(pos, radius + GradeScanDistance), children);
                foreach (var child in children)
                    GatherGradeComponents(pos, child, output, radius);
            }
        }

        public void DebugDraw()
        {
            var posNullable = MyAPIGateway.Session?.ControlledObject?.GetPosition() ?? MyCameraComponent.ActiveCamera?.GetPosition();
            if (!posNullable.HasValue) return;
            var pos = posNullable.Value;
            if (RailConstants.Debug.DrawGradingFill.Preset != default || RailConstants.Debug.DrawGradingCut.Preset != default)
            {
                using (PoolManager.Get(out List<RailGradeComponent> components))
                {
                    GatherGradeComponents(pos, components, 5);
                    foreach (var gradeComp in components)
                    {
                        if (RailConstants.Debug.DrawGradingFill.Preset != default)
                            gradeComp.GetSupport(RailConstants.Debug.DrawGradingFill)?.DrawShape(FillColor);
                        if (RailConstants.Debug.DrawGradingCut.Preset != default)
                            gradeComp.GetExcavate(RailConstants.Debug.DrawGradingCut)?.DrawShape(ExcavateColor);
                    }
                }
            }

            IReadOnlyList<CachedSdf> forDrawing;
            if (RailConstants.Debug.DrawSdfCacheCut)
                forDrawing = _prevCutSdf.CellTable;
            else if (RailConstants.Debug.DrawSdfCacheFill)
                forDrawing = _prevFillSdf.CellTable;
            else
                forDrawing = null;
            if (forDrawing == null) return;
            foreach (var cell in forDrawing)
                cell.DebugDraw();
        }

        internal static readonly MyStringId FillColor = MyStringId.GetOrCompute("RailGradeFill");
        internal static readonly MyStringId ExcavateColor = MyStringId.GetOrCompute("RailGradeExcavate");

        [RpcSerializable]
        public struct GradingOptions
        {
            public OrientedBoundingBoxD Box;
            public uint AvailableForDeposit;
            public uint AvailableForExcavate;
            public byte MaterialToDeposit;
            public RailGradeComponentDefinition.ShapeOptions Support;
            public RailGradeComponentDefinition.ShapeOptions Excavate;
            public bool ReplaceSurfaceMaterial;

            public void BoxFromRadius(ListReader<RailGradeComponent> component, in Vector3D center, float radius)
            {
                Box.Center = center;
                Box.HalfExtent = new Vector3D(radius, radius, radius);
                Box.Orientation = Quaternion.Identity;
                var bestDist2 = double.PositiveInfinity;
                foreach (var comp in component)
                {
                    if (!comp.TryGetNearestCurveOrientation(in center, out var dist2, out _, out var rot)) continue;
                    if (dist2 >= bestDist2) continue;
                    Box.Orientation = rot;
                    bestDist2 = dist2;
                }
            }
        }

        public bool DoGrading(
            ListReader<RailGradeComponent> components, in GradingOptions options,
            VoxelMiningBuffer excavatedByMaterial, out uint totalDeposited, out uint totalExcavated,
            out bool triedToChange, out bool intersectedDynamic, out List<OrientedBoundingBoxD> dynamicEntities,
            out MyVoxelBase voxel, out int voxelRadius)
        {
            var halfExtent = options.Box.HalfExtent;
            voxelRadius = (int)Math.Ceiling(halfExtent.Length());
            dynamicEntities = new List<OrientedBoundingBoxD>();
            var sphere = new BoundingSphereD(options.Box.Center, voxelRadius + 2);
            var tmp = MyEntities.GetTopMostEntitiesInSphere(ref sphere);
            foreach (var e in tmp)
            {
                if (e.Physics != null && !e.Physics.IsStatic)
                    dynamicEntities.Add(new OrientedBoundingBoxD(e.PositionComp.LocalAABB, e.PositionComp.WorldMatrix));
            }

            tmp.Clear();
            totalDeposited = 0;
            totalExcavated = 0;
            triedToChange = false;
            intersectedDynamic = false;
            voxel = MyGamePruningStructureSandbox.GetClosestPlanet(options.Box.Center)?.RootVoxel;
            if (voxel == null)
                return false;
            return DoGradingInternal(components, dynamicEntities, voxel, voxelRadius, in options,
                excavatedByMaterial, out totalDeposited, out totalExcavated,
                out triedToChange, out intersectedDynamic);
        }

        private CachedSdf.CachedSdfAccessor _prevFillSdf;
        private CachedSdf.CachedSdfAccessor _prevCutSdf;

        private bool DoGradingInternal<T>(
            ListReader<T> components,
            List<OrientedBoundingBoxD> dynamicEntities,
            MyVoxelBase voxel,
            int voxelRadius, in GradingOptions options,
            VoxelMiningBuffer excavatedByMaterial, out uint totalDeposited, out uint totalExcavated,
            out bool triedToChange, out bool intersectedDynamic) where T : IRailGradeComponent
        {
            totalDeposited = 0;
            totalExcavated = 0;
            triedToChange = false;
            intersectedDynamic = false;
            var targetBox = options.Box;
            MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref targetBox.Center, out var center);
            var voxMin = center - voxelRadius - 1;
            var voxMax = center + voxelRadius + 1;
            var gravity = Vector3D.Normalize(MyGravityProviderSystem.CalculateNaturalGravityInPoint(targetBox.Center));
            _storage.Resize(voxMin, voxMax);
            voxel.Storage.ReadRange(_storage, MyStorageDataTypeFlags.ContentAndMaterial, 0, voxMin, voxMax);

            CachedSdf.CachedSdfAccessor fillSdf, excavateSdf;
            using (PoolManager.Get(out List<IGradeShape> fill))
            using (PoolManager.Get(out List<IGradeShape> excavate))
            {
                foreach (var comp in components)
                {
                    var fillTmp = comp.GetSupport(in options.Support);
                    if (fillTmp != null)
                        fill.Add(fillTmp);
                    var excavateTmp = comp.GetExcavate(in options.Excavate);
                    if (excavateTmp != null)
                        excavate.Add(excavateTmp);
                }

                fillSdf = new CachedSdf.CachedSdfAccessor(_sdfCache, voxel, CachedSdf.CachedSdfGroup.Fill, in voxMin, in voxMax, fill);
                excavateSdf = new CachedSdf.CachedSdfAccessor(_sdfCache, voxel, CachedSdf.CachedSdfGroup.Cut, in voxMin, in voxMax, excavate);
                _prevFillSdf = fillSdf;
                _prevCutSdf = excavateSdf;
            }

            var changed = false;

            #region Mutate

            var availableForExcavate = options.AvailableForExcavate;
            var availableForDeposit = options.AvailableForDeposit;

            for (var i = 0;
                 i <= voxelRadius && (!triedToChange || availableForExcavate > 0 || availableForDeposit > 0);
                 i++)
            for (var e = new ShellEnumerator(center - i, center + i);
                 e.MoveNext() && (!triedToChange || availableForExcavate > 0 || availableForDeposit > 0);)
            {
                var vCoord = e.Current;
                var dataCoord = e.Current - voxMin;
                MyVoxelCoordSystems.VoxelCoordToWorldPosition(voxel.PositionLeftBottomCorner, ref vCoord, out var worldCoord);
                if (!targetBox.Contains(ref worldCoord)) continue;
                var cval = _storage.Get(MyStorageDataTypeEnum.Content, ref dataCoord);

                var excavateDensity = (byte)~excavateSdf.QuerySdf(vCoord);
                var fillDensity = fillSdf.QuerySdf(vCoord);
                byte desiredDensity;
                {
                    if (fillDensity > 0 && excavateDensity < byte.MaxValue)
                        desiredDensity = excavateDensity <= cval ? excavateDensity : Math.Max(cval, fillDensity);
                    else if (fillDensity > 0)
                        desiredDensity = Math.Max(cval, fillDensity);
                    else if (excavateDensity < byte.MaxValue)
                        desiredDensity = Math.Min(cval, excavateDensity);
                    else
                        continue;
                }


                if (desiredDensity < cval)
                {
                    triedToChange = true;
                    var toExtract = (uint)Math.Min(availableForExcavate, cval - desiredDensity);
                    if (toExtract > 0)
                    {
                        var mid = _storage.Get(MyStorageDataTypeEnum.Material, ref dataCoord);
                        excavatedByMaterial?.Add(mid, (int)toExtract);
                        DisableFarming(worldCoord, voxel);
                        _storage.Set(MyStorageDataTypeEnum.Content, ref dataCoord, (byte)(cval - toExtract));
                        totalExcavated += toExtract;
                        availableForExcavate -= toExtract;
                        changed = true;
                    }

                    continue;
                }

                var forceChangeMaterial = options.ReplaceSurfaceMaterial
                                          && fillDensity > 0
                                          && _storage.Get(MyStorageDataTypeEnum.Material, ref dataCoord) != options.MaterialToDeposit
                                          && cval < byte.MaxValue;
                if (cval >= desiredDensity && !forceChangeMaterial)
                    continue;

                triedToChange = true;
                var newValue = Math.Max(cval, desiredDensity);
                var usedMaterial = forceChangeMaterial ? newValue : (uint) (newValue - cval);
                if (availableForDeposit < usedMaterial) continue;

                if (newValue != cval)
                {
                    // would this deposit in midair?
                    var test = worldCoord + 2 * gravity;
                    MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref test, out var vtest);
                    vtest = Vector3I.Clamp(vtest, voxMin, voxMax) - voxMin;
                    if (vtest != vCoord && _storage.Get(MyStorageDataTypeEnum.Content, ref vtest) == 0)
                        continue;

                    // would it touch something dynamic?
                    var box = new BoundingBoxD(worldCoord - 0.25, worldCoord + 0.25);
                    var bad = false;
                    foreach (var obb in dynamicEntities)
                    {
                        if (!obb.Intersects(ref box)) continue;
                        bad = true;
                        break;
                    }

                    if (bad)
                    {
                        intersectedDynamic = true;
                        continue;
                    }
                }

                changed = true;
                DisableFarming(worldCoord, voxel);
                availableForDeposit -= usedMaterial;
                totalDeposited += usedMaterial;
                _storage.Set(MyStorageDataTypeEnum.Content, ref dataCoord, newValue);
                if (desiredDensity <= cval * 1.25f && !forceChangeMaterial) continue;
                for (var itrContent = new Vector3I_RangeIterator(ref _negativeOne, ref Vector3I.One);
                     itrContent.IsValid();
                     itrContent.MoveNext())
                {
                    var mtlPos = dataCoord + itrContent.Current;
                    var state = _storage.Get(MyStorageDataTypeEnum.Content, ref mtlPos);
                    if (itrContent.Current == Vector3I.Zero || state == 0)
                        _storage.Set(MyStorageDataTypeEnum.Material, ref mtlPos, options.MaterialToDeposit);
                }
            }

            #endregion Mutate

            if (changed)
                voxel.Storage.WriteRange(_storage, MyStorageDataTypeFlags.ContentAndMaterial, voxMin, voxMax);

            return changed;
        }

        private static Vector3I _negativeOne = -Vector3I.One;

        private static void DisableFarming(Vector3D pos, MyVoxelBase vox)
        {
            var box = new BoundingBoxD(pos - 1, pos + 1);
            vox.DisableFarmingItemsIn(box);
        }

        private const int GradingDesyncTol = 25;

        [RpcSerializable]
        private struct GradingConfig
        {
            public GradingOptions Options;
            public int VoxelRadius;
            public uint DepositExpected;
            public uint ExcavateExpected;
        }

        private DateTime _lastGradingDesync = new DateTime(0);
        private long _sentGradingTasks = 0;

        public void RaiseDoGrade(IReadOnlyList<RailGradeComponent> components,
            in GradingOptions options, int voxelRadius, uint totalExcavated, uint totalDeposited, EntityId voxelId,
            List<OrientedBoundingBoxD> dynamicEntities)
        {
            const int gradeComponentBatchSize = 5;
            const int dynamicEntityBatchSize = 10;
            var taskId = _sentGradingTasks++;
            var chunks = 0;
            for (var i = 0; i < components.Count; i += gradeComponentBatchSize)
            {
                var slice = new RailGradeComponentBlit[Math.Min(gradeComponentBatchSize, components.Count - i)];
                for (var j = 0; j < slice.Length; j++)
                    slice[j] = components[j + i].Blit();
                MyMultiplayerModApi.Static.RaiseEvent(this, x => x.DoGradeStageComponents, taskId, slice);
                chunks++;
            }

            for (var i = 1; i < dynamicEntities.Count; i += dynamicEntityBatchSize)
            {
                var slice = new OrientedBoundingBoxBlit[Math.Min(dynamicEntityBatchSize, dynamicEntities.Count - i)];
                for (var j = 0; j < slice.Length; j++)
                {
                    var dyn = dynamicEntities[i + j];
                    slice[j] = new OrientedBoundingBoxBlit(dyn);
                }

                MyMultiplayerModApi.Static.RaiseEvent(this, x => x.DoGradeStageEntities, taskId, slice);
                chunks++;
            }

            MyMultiplayerModApi.Static.RaiseEvent(this, x => x.DoGradeFinish,
                taskId,
                new GradingConfig
                {
                    Options = options,
                    VoxelRadius = voxelRadius,
                    ExcavateExpected = totalExcavated,
                    DepositExpected = totalDeposited,
                },
                voxelId,
                chunks,
                dynamicEntities.Count > 0 ? (OrientedBoundingBoxBlit?)new OrientedBoundingBoxBlit(dynamicEntities[0]) : null);
        }

        private sealed class StagedGradingTask
        {
            internal long TaskId;
            internal int ChunksReceived;
            internal int? ChunksTotal;
            internal GradingConfig? Config;
            internal MyVoxelBase Voxel;
            internal readonly List<IRailGradeComponent> GradeComponents = new List<IRailGradeComponent>();
            internal readonly List<OrientedBoundingBoxD> DynamicEntities = new List<OrientedBoundingBoxD>();

            internal void Reset()
            {
                TaskId = -1;
                ChunksReceived = 0;
                ChunksTotal = null;
                Config = null;
                Voxel = null;
                GradeComponents.Clear();
                DynamicEntities.Clear();
            }
        }

        private readonly Dictionary<byte, StagedGradingTask> _stagedGradeTasks = new Dictionary<byte, StagedGradingTask>();

        private StagedGradingTask GetStagingTask(long taskId)
        {
            var key = (byte)(taskId & 0xFF);
            if (_stagedGradeTasks.TryGetValue(key, out var task))
            {
                if (task.TaskId == taskId) return task;
                Log.Warning($"Discarding old task {task.TaskId} because new task {taskId} fills same slot");
                _stagedGradeTasks.Remove(key);
            }

            task = PoolManager.Get<StagedGradingTask>();
            task.TaskId = taskId;
            _stagedGradeTasks.Add(key, task);
            return task;
        }

        [Event]
        [Broadcast]
        [Reliable]
        private void DoGradeFinish(long taskId, GradingConfig config, EntityId voxelId, int totalChunks,
            OrientedBoundingBoxBlit? dynamicEntity)
        {
            var task = GetStagingTask(taskId);
            task.Config = config;
            Scene.TryGetEntity(voxelId, out task.Voxel);
            task.ChunksTotal = totalChunks;
            if (dynamicEntity.HasValue)
                task.DynamicEntities.Add(dynamicEntity.Value.Unblit());
            task.ChunksReceived++;
            MaybeFinishTask(taskId, task);
        }

        [Event]
        [Broadcast]
        [Reliable]
        private void DoGradeStageComponents(long taskId, RailGradeComponentBlit[] components)
        {
            var task = GetStagingTask(taskId);
            foreach (var comp in components)
                task.GradeComponents.Add(comp);
            task.ChunksReceived++;
            MaybeFinishTask(taskId, task);
        }

        [Event]
        [Broadcast]
        [Reliable]
        private void DoGradeStageEntities(long taskId, OrientedBoundingBoxBlit[] dynamic)
        {
            var task = GetStagingTask(taskId);
            if (dynamic != null)
                foreach (var dyn in dynamic)
                    task.DynamicEntities.Add(dyn.Unblit());
            task.ChunksReceived++;
            MaybeFinishTask(taskId, task);
        }

        private void MaybeFinishTask(long taskId, StagedGradingTask task)
        {
            if (!task.ChunksTotal.HasValue || task.ChunksReceived < task.ChunksTotal) return;
            try
            {
                if (task.Voxel == null || task.Config == null) return;
                var config = task.Config.Value;
                DoGradingInternal<IRailGradeComponent>(task.GradeComponents, task.DynamicEntities, task.Voxel, config.VoxelRadius, in config.Options,
                    null, out var deposited, out var excavated, out _, out _);

                if (Math.Abs(config.DepositExpected - (int)deposited) <= GradingDesyncTol
                    && Math.Abs(config.ExcavateExpected - (int)excavated) <= GradingDesyncTol) return;

                var playerPos = MySession.Static.PlayerEntity?.GetPosition();
                if (playerPos == null || Vector3D.DistanceSquared(playerPos.Value, config.Options.Box.Center) > 100 * 100) return;

                Log.Warning($"Grading desync occured!  {config.DepositExpected} != {deposited}, {config.ExcavateExpected} != {excavated}");
                var time = DateTime.Now;
                if ((time - _lastGradingDesync) <= TimeSpan.FromSeconds(30)) return;
                var red = new Vector4(1, 0, 0, 1);
                MyAPIGateway.Utilities.ShowNotification("Grading desync occured!  If you experience movement problems try reconnecting.", 5000, null, red);
                _lastGradingDesync = time;
            }
            finally
            {
                var key = (byte)(taskId & 0xFF);
                _stagedGradeTasks.Remove(key);
                task.Reset();
                PoolManager.Return(ref task);
            }
        }

        [RpcSerializable]
        private struct OrientedBoundingBoxBlit
        {
            public Vector3D Center;
            public Vector3 HalfExtents;

            [Serialize(MyPrimitiveFlags.Normalized)]
            public Quaternion Orientation;

            internal OrientedBoundingBoxBlit(in OrientedBoundingBoxD box)
            {
                Center = box.Center;
                HalfExtents = (Vector3)box.HalfExtent;
                Orientation = box.Orientation;
            }

            public OrientedBoundingBoxD Unblit() => new OrientedBoundingBoxD(Center, HalfExtents, Orientation);
        }
    }
}