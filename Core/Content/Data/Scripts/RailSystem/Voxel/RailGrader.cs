using System;
using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.RailSystem.Bendy;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using VRage.Definitions;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Library.Logging;
using VRage.ModAPI;
using VRage.Network;
using VRage.Voxels;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Voxel
{
    [StaticEventOwner]
    public static class RailGraderSystem
    {
        private static readonly MyStorageData _storage = new MyStorageData();
        private static readonly List<MyVoxelBase> _workingVoxels = new List<MyVoxelBase>();
        private static readonly List<MyEntity> _dynamicEntities = new List<MyEntity>();

        public static bool DoGrading(
            IReadOnlyList<IRailGradeComponent> components, Vector3D target, float radius, uint availableForDeposit,
            uint availableForExcavate,
            uint[] excavatedByMaterial, byte materialToDeposit, out uint totalDeposited, out uint totalExcavated,
            bool testDynamic, out bool triedToChange, out bool intersectedDynamic)
        {
            try
            {
                var voxelRadius = (int) Math.Ceiling(radius);
                {
                    _dynamicEntities.Clear();
                    _workingVoxels.Clear();
                    var sphere = new BoundingSphereD(target, voxelRadius + 2);
                    foreach (var e in MyEntities.GetEntitiesInSphere(ref sphere))
                    {
                        var vox = e as MyVoxelBase;
                        if (vox != null)
                            _workingVoxels.Add(vox);
                        if (e.Physics != null && !e.Physics.IsStatic)
                            _dynamicEntities.Add(e);
                    }
                }


                totalDeposited = 0;
                totalExcavated = 0;
                triedToChange = false;
                intersectedDynamic = false;

                var fill = new IGradeShape[components.Count];
                var excavate = new IGradeShape[components.Count];
                for (var i = 0; i < components.Count; i++)
                    components[i].Unblit(out fill[i], out excavate[i]);

                var voxel = MyGamePruningStructure.GetClosestPlanet(target)?.RootVoxel;
                if (voxel == null)
                    return false;
                Vector3I center;
                MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref target, out center);
                var voxMin = center - voxelRadius - 1;
                var voxMax = center + voxelRadius + 1;
                _storage.Resize(voxMin, voxMax);
                voxel.Storage.ReadRange(_storage, MyStorageDataTypeFlags.ContentAndMaterial, 0, voxMin, voxMax);

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

                    byte? excavationDensity = null;
                    if (cval > 0 && (!triedToChange || availableForExcavate > 0))
                    {
                        float density = 0;
                        foreach (var c in excavate.Where(x => x != null))
                            density = Math.Max(density, c.GetDensity(ref worldCoord));
                        if (density > 0)
                            excavationDensity = (byte) ((1 - density) * byte.MaxValue);
                    }

                    byte? fillDensity = null;
                    if (cval < byte.MaxValue && (!triedToChange || availableForDeposit > 0))
                    {
                        float density = 0;
                        foreach (var c in fill.Where(x => x != null))
                            density = Math.Max(density, c.GetDensity(ref worldCoord));
                        if (density > 0)
                            fillDensity = (byte) (density * byte.MaxValue);
                    }

                    if ((!fillDensity.HasValue || cval >= fillDensity.Value) &&
                        (!excavationDensity.HasValue || cval <= excavationDensity.Value))
                        continue;

                    if (excavationDensity.HasValue && excavationDensity.Value < cval)
                    {
                        triedToChange = true;
                        var toExtract = (uint) Math.Min(availableForExcavate, cval - excavationDensity.Value);
                        if (toExtract > 0)
                        {
                            var mid = _storage.Get(MyStorageDataTypeEnum.Material, ref dataCoord);
                            if (excavatedByMaterial != null && mid < excavatedByMaterial.Length)
                                excavatedByMaterial[mid] += toExtract;
                            DisableFarming(worldCoord);
                            _storage.Set(MyStorageDataTypeEnum.Content, ref dataCoord, (byte) (cval - toExtract));
                            totalExcavated += toExtract;
                            availableForExcavate -= toExtract;
                            changed = true;
                        }

                        continue;
                    }

                    if (!fillDensity.HasValue || fillDensity.Value <= cval)
                        continue;
                    triedToChange = true;
                    var toFill = Math.Min(availableForDeposit, fillDensity.Value - cval);
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
                    if (testDynamic)
                    {
                        var box = new BoundingBoxD(worldCoord - 0.25, worldCoord + 0.25);
                        var bad = false;
                        foreach (var k in _dynamicEntities)
                        {
                            if (k.PositionComp.WorldAABB.Contains(box) == ContainmentType.Disjoint)
                                continue;
                            var obb = new OrientedBoundingBoxD(k.PositionComp.LocalAABB, k.WorldMatrix);
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
                    DisableFarming(worldCoord);
                    availableForDeposit = (uint) (availableForDeposit - toFill);
                    totalDeposited += (uint) toFill;
                    _storage.Set(MyStorageDataTypeEnum.Content, ref dataCoord, (byte) (cval + toFill));
                    if (fillDensity.Value <= cval * 1.25f) continue;
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

                return changed;
            }
            finally
            {
                _dynamicEntities.Clear();
                _workingVoxels.Clear();
            }
        }

        private static void DisableFarming(Vector3D pos)
        {
            var box = new BoundingBoxD(pos - 1, pos + 1);
            foreach (var vox in _workingVoxels)
                vox.DisableFarmingItemsIn(box);
        }


        private const int GradingDesyncTol = 25;

        private struct GradingConfig
        {
            public float Radius;
            public uint DepositAvailable;
            public uint ExcavateAvailable;
            public byte MaterialToDeposit;
            public uint DepositExpected;
            public uint ExcavateExpected;
        }

        private static DateTime _lastGradingDesync = new DateTime(0);

        public static void RaiseDoGrade(IEnumerable<RailGradeComponent> components, Vector3D pos, float radius, uint availableForDeposit,
            uint availableForExcavate, byte fillMaterial, uint totalExcavated, uint totalDeposited)
        {
            MyMultiplayerModApi.Static.RaiseStaticEvent((x) => DoGrade, components.Where(x => x.IsValid).Select(x => x.Blit()).ToArray(), pos,
                new GradingConfig()
                {
                    Radius = radius,
                    DepositAvailable = availableForDeposit,
                    ExcavateAvailable = availableForExcavate,
                    MaterialToDeposit = fillMaterial,
                    ExcavateExpected = totalExcavated,
                    DepositExpected = totalDeposited
                });
        }

        [Event]
        [Broadcast]
        private static void DoGrade(RailGradeComponentBlit[] components, Vector3D target, GradingConfig config)
        {
            uint deposited;
            uint excavated;
            bool triedToChange;
            bool intersectedDynamic;
            var cbox = new IRailGradeComponent[components.Length];
            for (var i = 0; i < components.Length; i++)
                cbox[i] = components[i];
            DoGrading(cbox, target, config.Radius, config.DepositAvailable, config.ExcavateAvailable, null, config.MaterialToDeposit, out deposited,
                out excavated, true, out triedToChange, out intersectedDynamic);

            if (Math.Abs(config.DepositExpected - deposited) <= GradingDesyncTol && Math.Abs(config.ExcavateExpected - excavated) <= GradingDesyncTol) return;
            
            MyLog.Default.Warning($"Grading desync occured!  {config.DepositExpected} != {deposited}, {config.ExcavateExpected} != {excavated}");
            var time = DateTime.Now;
            if ((time - _lastGradingDesync) <= TimeSpan.FromSeconds(30)) return;
            var red = new Vector4(1, 0, 0, 1);
            MyAPIGateway.Utilities.ShowNotification("Grading desync occured!  If you experience movement problems try reconnecting.", 5000, null, red);
            _lastGradingDesync = time;
        }
    }
}