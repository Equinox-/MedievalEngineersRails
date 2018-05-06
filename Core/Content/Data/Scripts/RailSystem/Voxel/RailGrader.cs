using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Equinox76561198048419394.RailSystem.Bendy;
using Equinox76561198048419394.RailSystem.Util;
using Medieval.Constants;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Inventory;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using VRage.Definitions;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRage.Network;
using VRage.Session;
using VRage.Systems;
using VRage.Voxels;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Voxel
{
    public static class RailGrader
    {
        public static void Grade(Edge edge, bool fill, bool excavate)
        {
            if (!fill && !excavate) return;

            var box = BoundingBoxD.CreateInvalid();
            RailGradeShape shapeFill = null;

            var edgeBlit = new EdgeBlit(edge);

            if (fill)
            {
                shapeFill = new RailGradeShape(edgeBlit, 3.5f, (float) Math.PI / 3f, 0.125f, 8, 10f);
                box = box.Include(shapeFill.Box);
            }

            RailGradeShape shapeEmpty = null;
            if (excavate)
            {
                shapeEmpty = new RailGradeShape(edgeBlit, 3.5f, (float) Math.PI / 2.2f, 0.125f, 8, -3f);
                box = box.Include(shapeEmpty.Box);
            }

            var planet = MyGamePruningStructure.GetClosestPlanet(box.Center);
            var stor = (IMyStorage) planet.Storage;
            var tmp = new MyStorageData();
            Vector3I minMutable, maxMutable;
            var minWorld = box.Min;
            var maxWorld = box.Max;
            MyVoxelCoordSystems.WorldPositionToVoxelCoord(planet.PositionLeftBottomCorner, ref minWorld, out minMutable);
            MyVoxelCoordSystems.WorldPositionToVoxelCoord(planet.PositionLeftBottomCorner, ref maxWorld, out maxMutable);
            var min = minMutable - Vector3I.One;
            var max = maxMutable + Vector3I.One;

            var mtl = MyDefinitionManager.Get<MyVoxelMaterialDefinition>("Rocks_moss").Index;
            tmp.Resize(Vector3I.One + max - min);
            stor.ReadRange(tmp, MyStorageDataTypeFlags.ContentAndMaterial, 0, min, max);

            for (var itr = new Vector3I_RangeIterator(ref minMutable, ref maxMutable); itr.IsValid(); itr.MoveNext())
            {
                var vpo = itr.Current;
                var lclVpo = vpo - min;
                Vector3D wpo;
                MyVoxelCoordSystems.VoxelCoordToWorldPosition(planet.PositionLeftBottomCorner, ref vpo, out wpo);
                var cval = tmp.Get(MyStorageDataTypeEnum.Content, ref lclVpo);

                var excavationDensity = (byte) (MathHelper.Clamp(1 - (shapeEmpty?.GetDensity(ref wpo) ?? 0), 0, 1) * byte.MaxValue);
                var fillDensity = (byte) (MathHelper.Clamp(shapeFill?.GetDensity(ref wpo) ?? 0, 0, 1) * byte.MaxValue);
                if (cval >= fillDensity && cval <= excavationDensity)
                    continue;

                if (excavationDensity < cval)
                {
                    tmp.Set(MyStorageDataTypeEnum.Content, ref lclVpo, excavationDensity);
                    continue;
                }

                if (fillDensity <= cval)
                    continue;
                tmp.Set(MyStorageDataTypeEnum.Content, ref lclVpo, fillDensity);
                if (fillDensity <= cval * 1.5f) continue;
                var t = -Vector3I.One;
                for (var itrContent = new Vector3I_RangeIterator(ref t, ref Vector3I.One); itrContent.IsValid(); itrContent.MoveNext())
                {
                    var tpos = lclVpo + itrContent.Current;
                    tmp.Set(MyStorageDataTypeEnum.Material, ref tpos, mtl);
                }
            }

            stor.WriteRange(tmp, MyStorageDataTypeFlags.ContentAndMaterial, min, max);
        }


        private static readonly MyStorageData _storage = new MyStorageData();

        public static bool DoGrading(
            IReadOnlyList<IRailGradeComponent> components, Vector3D target, float radius, bool isExcavating, uint availableForDeposit, uint availableForExcavate,
            uint[] excavatedByMaterial, byte materialToDeposit, out uint totalDeposited, out uint totalExcavated, bool testDynamic, out bool triedToChange, out bool intersectedDynamic)
        {
            totalDeposited = 0;
            totalExcavated = 0;
            triedToChange = false;
            intersectedDynamic = false;

            var fill = new RailGradeShape[components.Count];
            var excavate = new RailGradeShape[components.Count];
            for (var i = 0; i < components.Count; i++)
                components[i].Unblit(out fill[i], out excavate[i]);

            var voxel = MyGamePruningStructure.GetClosestPlanet(target)?.RootVoxel;
            if (voxel == null)
                return false;
            var voxelRadius = (int) Math.Ceiling(radius);
            Vector3I center;
            MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref target, out center);
            var voxMin = center - voxelRadius - 1;
            var voxMax = center + voxelRadius + 1;
            _storage.Resize(voxMin, voxMax);
            voxel.Storage.ReadRange(_storage, MyStorageDataTypeFlags.ContentAndMaterial, 0, voxMin, voxMax);

            bool changed = false;

            #region Mutate

            for (var i = 0; i <= voxelRadius && (!triedToChange || (availableForExcavate > 0 && availableForDeposit > 0)); i++)
            for (var e = new ShellEnumerator(center - i, center + i); e.MoveNext() && (!triedToChange || (availableForExcavate > 0 && availableForDeposit > 0));)
            {
                var vCoord = e.Current;
                var dataCoord = e.Current - voxMin;
                Vector3D worldCoord;
                MyVoxelCoordSystems.VoxelCoordToWorldPosition(voxel.PositionLeftBottomCorner, ref vCoord, out worldCoord);
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

                if ((!fillDensity.HasValue || cval >= fillDensity.Value) && (!excavationDensity.HasValue || cval <= excavationDensity.Value))
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
                    test += 2 * Vector3D.Normalize(MyGravityProviderSystem.CalculateNaturalGravityInPoint(worldCoord));
                    Vector3I vtest;
                    MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref test, out vtest);
                    vtest = Vector3I.Clamp(vtest, voxMin, voxMax) - voxMin;
                    if (vtest != vCoord && _storage.Get(MyStorageDataTypeEnum.Content, ref vtest) == 0)
                        continue;
                }

                // would it touch something dynamic?
                if (testDynamic)
                {
                    var box = new BoundingBoxD(worldCoord - 0.25, worldCoord + 0.25);
                    var bad = false;
                    foreach (var k in MyEntities.GetEntitiesInAABB(ref box))
                    {
                        if (k.Physics == null || k.Physics.IsStatic)
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
                for (var itrContent = new Vector3I_RangeIterator(ref t, ref Vector3I.One); itrContent.IsValid(); itrContent.MoveNext())
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

        private static void DisableFarming(Vector3D pos)
        {
            FarmingExtensions.DisableItemsIn(new BoundingBoxD(pos - 1, pos + 1));
        }
    }
}