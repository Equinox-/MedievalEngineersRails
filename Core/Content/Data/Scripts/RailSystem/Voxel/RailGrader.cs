using System;
using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.RailSystem.Util;
using Equinox76561198048419394.RailSystem.Voxel.Shape;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Components.Entity.CubeGrid;
using VRage.Components.Session;
using VRage.Entities.Gravity;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.Logging;
using VRage.Network;
using VRage.Scene;
using VRage.Session;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Voxel
{
    [MySessionComponent(AllowAutomaticCreation = true, AlwaysOn = true)]
    public sealed class RailGraderSystem : MySessionComponent
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

        protected override void OnUnload()
        {
            _sdfCache.Clear();
            base.OnUnload();
        }

        public static void GatherGradeComponents(Vector3D pos, List<RailGradeComponent> output, float radius)
        {
            using (PoolManager.Get(out List<MyEntity> entities))
            {
                MyGamePruningStructure.GetTopMostEntitiesInSphere(
                    // Gather for a larger radius so we minimize the invalidation performed on the SDF cache
                    new BoundingSphereD(pos, Math.Max(radius, CachedSdf.CellSize) + GradeScanDistance), entities);
                foreach (var ent in entities)
                    GatherGradeComponents(pos, ent, output, radius);
            }
        }

        private static void GatherGradeComponents(Vector3D pos, MyEntity entity, List<RailGradeComponent> output, float radius)
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

        private static readonly MyStringId FillColor = MyStringId.GetOrCompute("RailGradeFill");
        private static readonly MyStringId ExcavateColor = MyStringId.GetOrCompute("RailGradeExcavate");

        public static void DebugDrawGradeComponents(Vector3D position)
        {
            using (PoolManager.Get(out List<RailGradeComponent> components))
            {
                GatherGradeComponents(position, components, 5);
                foreach (var gradeComp in components)
                {
                    gradeComp.Support?.Draw(FillColor);
                    gradeComp.Excavation?.Draw(ExcavateColor);
                }
            }
        }

        public bool DoGrading(
            IReadOnlyList<IRailGradeComponent> components, Vector3D target, float radius, uint availableForDeposit,
            uint availableForExcavate,
            VoxelMiningBuffer excavatedByMaterial, byte materialToDeposit, out uint totalDeposited, out uint totalExcavated,
            out bool triedToChange, out bool intersectedDynamic, out List<OrientedBoundingBoxD> dynamicEntities,
            out MyVoxelBase voxel, out int voxelRadius)
        {
            voxelRadius = (int)Math.Ceiling(radius);
            dynamicEntities = new List<OrientedBoundingBoxD>();
            var sphere = new BoundingSphereD(target, voxelRadius + 2);
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
            voxel = MyGamePruningStructureSandbox.GetClosestPlanet(target)?.RootVoxel;
            if (voxel == null)
                return false;
            return DoGradingInternal(components, dynamicEntities, voxel, target, voxelRadius, availableForDeposit,
                availableForExcavate, excavatedByMaterial, materialToDeposit, out totalDeposited, out totalExcavated,
                out triedToChange, out intersectedDynamic);
        }

        private bool DoGradingInternal(
            IReadOnlyList<IRailGradeComponent> components,
            List<OrientedBoundingBoxD> dynamicEntities,
            MyVoxelBase voxel,
            Vector3D target, int voxelRadius, uint availableForDeposit,
            uint availableForExcavate,
            VoxelMiningBuffer excavatedByMaterial, byte materialToDeposit, out uint totalDeposited, out uint totalExcavated,
            out bool triedToChange, out bool intersectedDynamic)
        {
            totalDeposited = 0;
            totalExcavated = 0;
            triedToChange = false;
            intersectedDynamic = false;
            Vector3I center;
            MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref target, out center);
            var voxMin = center - voxelRadius - 1;
            var voxMax = center + voxelRadius + 1;
            _storage.Resize(voxMin, voxMax);
            voxel.Storage.ReadRange(_storage, MyStorageDataTypeFlags.ContentAndMaterial, 0, voxMin, voxMax);
            CachedSdf.CachedSdfAccessor fillSdf, excavateSdf;
            {
                var fill = new IGradeShape[components.Count];
                var excavate = new IGradeShape[components.Count];
                for (var i = 0; i < components.Count; i++)
                    components[i].Unblit(out fill[i], out excavate[i]);
                fillSdf = new CachedSdf.CachedSdfAccessor(_sdfCache, voxel, CachedSdf.CachedSdfGroup.Fill, in voxMin, in voxMax, fill);
                excavateSdf = new CachedSdf.CachedSdfAccessor(_sdfCache, voxel, CachedSdf.CachedSdfGroup.Cut, in voxMin, in voxMax, excavate);
            }

            var changed = false;

            #region Mutate

            for (var i = 0;
                 i <= voxelRadius && (!triedToChange || availableForExcavate > 0 || availableForDeposit > 0);
                 i++)
            for (var e = new ShellEnumerator(center - i, center + i);
                 e.MoveNext() && (!triedToChange || availableForExcavate > 0 || availableForDeposit > 0);)
            {
                var vCoord = e.Current;
                var dataCoord = e.Current - voxMin;
                Vector3D worldCoord;
                MyVoxelCoordSystems.VoxelCoordToWorldPosition(voxel.PositionLeftBottomCorner, ref vCoord,
                    out worldCoord);
                var cval = _storage.Get(MyStorageDataTypeEnum.Content, ref dataCoord);

                byte desiredDensity;
                {
                    var excavateDensity = (byte)~excavateSdf.QuerySdf(vCoord);
                    var fillDensity = fillSdf.QuerySdf(vCoord);
                    if (fillDensity > 0 && excavateDensity < byte.MaxValue)
                        desiredDensity = excavateDensity <= cval ? excavateDensity : Math.Max(cval, fillDensity);
                    else if (fillDensity > 0)
                        desiredDensity = Math.Max(cval, fillDensity);
                    else if (excavateDensity < byte.MaxValue)
                        desiredDensity = Math.Min(cval, excavateDensity);
                    else
                        continue;
                }

                if (desiredDensity == cval) continue;

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

                triedToChange = true;
                var toFill = Math.Min(availableForDeposit, desiredDensity - cval);
                if (toFill <= 0)
                    continue;

                // would this deposit in midair?
                {
                    var test = worldCoord;
                    test += 2 * Vector3D.Normalize(
                        MyGravityProviderSystem.CalculateNaturalGravityInPoint(worldCoord));
                    Vector3I vtest;
                    MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref test,
                        out vtest);
                    vtest = Vector3I.Clamp(vtest, voxMin, voxMax) - voxMin;
                    if (vtest != vCoord && _storage.Get(MyStorageDataTypeEnum.Content, ref vtest) == 0)
                        continue;
                }

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

                changed = true;
                DisableFarming(worldCoord, voxel);
                availableForDeposit = (uint)(availableForDeposit - toFill);
                totalDeposited += (uint)toFill;
                _storage.Set(MyStorageDataTypeEnum.Content, ref dataCoord, (byte)(cval + toFill));
                if (desiredDensity <= cval * 1.25f) continue;
                var t = -Vector3I.One;
                for (var itrContent = new Vector3I_RangeIterator(ref t, ref Vector3I.One);
                     itrContent.IsValid();
                     itrContent.MoveNext())
                {
                    var tpos = dataCoord + itrContent.Current;
//                    var state = _storage.Get(MyStorageDataTypeEnum.Content, ref tpos);
//                    if (itrContent.Current == Vector3I.Zero || state == 0)
                    _storage.Set(MyStorageDataTypeEnum.Material, ref tpos, materialToDeposit);
                }
            }

            #endregion Mutate

            if (changed)
                voxel.Storage.WriteRange(_storage, MyStorageDataTypeFlags.ContentAndMaterial, voxMin, voxMax);

            MyLog.Default.WriteLine($"Graded {components.Count}, {totalDeposited}, {totalExcavated}, {changed}");
            MyLog.Default.Flush();

            return changed;
        }

        private static void DisableFarming(Vector3D pos, MyVoxelBase vox)
        {
            var box = new BoundingBoxD(pos - 1, pos + 1);
            vox.DisableFarmingItemsIn(box);
        }


        private const int GradingDesyncTol = 25;

        private struct GradingConfig
        {
            public int Radius;
            public uint DepositAvailable;
            public uint ExcavateAvailable;
            public byte MaterialToDeposit;
            public uint DepositExpected;
            public uint ExcavateExpected;
        }

        private static DateTime _lastGradingDesync = new DateTime(0);

        public void RaiseDoGrade(IEnumerable<RailGradeComponent> components, Vector3D pos, int radius, uint availableForDeposit,
            uint availableForExcavate, byte fillMaterial, uint totalExcavated, uint totalDeposited, EntityId voxelId,
            List<OrientedBoundingBoxD> dynamicEntities)
        {
            MyMultiplayerModApi.Static.RaiseEvent(this, x => x.DoGrade,
                components.Where(x => x.IsValid).Select(x => x.Blit()).ToArray(),
                pos,
                new GradingConfig()
                {
                    Radius = radius,
                    DepositAvailable = availableForDeposit,
                    ExcavateAvailable = availableForExcavate,
                    MaterialToDeposit = fillMaterial,
                    ExcavateExpected = totalExcavated,
                    DepositExpected = totalDeposited
                },
                voxelId,
                dynamicEntities.Select(x => new OrientedBoundingBoxBlit
                {
                    Center = x.Center,
                    HalfExtents = (Vector3)x.HalfExtent,
                    Orientation = x.Orientation
                }).ToArray());
        }

        [Event]
        [Broadcast]
        private void DoGrade(RailGradeComponentBlit[] components, Vector3D target, GradingConfig config, EntityId voxelId, OrientedBoundingBoxBlit[] dynamic)
        {
            uint deposited;
            uint excavated;
            bool triedToChange;
            bool intersectedDynamic;
            if (Scene.TryGetEntity(voxelId, out MyVoxelBase voxel)) return;
            var cbox = new IRailGradeComponent[components.Length];
            for (var i = 0; i < components.Length; i++)
                cbox[i] = components[i];
            using (PoolManager.Get(out List<OrientedBoundingBoxD> dynamicEntities))
            {
                if (dynamic != null)
                    foreach (var dyn in dynamic)
                        dynamicEntities.Add(new OrientedBoundingBoxD(dyn.Center, dyn.HalfExtents, dyn.Orientation));
                DoGradingInternal(cbox, dynamicEntities, voxel, target, config.Radius, config.DepositAvailable, config.ExcavateAvailable,
                    null, config.MaterialToDeposit, out deposited,
                    out excavated, out triedToChange, out intersectedDynamic);
            }

            if (Math.Abs(config.DepositExpected - deposited) <= GradingDesyncTol
                && Math.Abs(config.ExcavateExpected - excavated) <= GradingDesyncTol) return;

            var playerPos = MySession.Static.PlayerEntity?.GetPosition();
            if (playerPos == null || Vector3D.DistanceSquared(playerPos.Value, target) > 100 * 100) return;

            Log.Warning($"Grading desync occured!  {config.DepositExpected} != {deposited}, {config.ExcavateExpected} != {excavated}");
            var time = DateTime.Now;
            if ((time - _lastGradingDesync) <= TimeSpan.FromSeconds(30)) return;
            var red = new Vector4(1, 0, 0, 1);
            MyAPIGateway.Utilities.ShowNotification("Grading desync occured!  If you experience movement problems try reconnecting.", 5000, null, red);
            _lastGradingDesync = time;
        }

        private struct OrientedBoundingBoxBlit
        {
            public Vector3D Center;
            public Vector3 HalfExtents;
            public Quaternion Orientation;
        }
    }
}