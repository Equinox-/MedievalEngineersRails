using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using VRage.Game.ModAPI;
using VRageMath;
using System.Linq;
using VRage.Game.Components;
using VRage.Game.Entity;

namespace Equinox76561198048419394.RailSystem.Util
{
    public static class MiscExtensions
    {
        public static bool AnyBlocksInAABB(this MyCubeGrid g, BoundingBoxD box)
        {
            if (g.PositionComp == null)
                return false;
            if (box.Contains(g.PositionComp.WorldAABB) == ContainmentType.Contains)
                return g.GetBlocks().Count > 0;

            OrientedBoundingBoxD orientedBoundingBoxD = OrientedBoundingBoxD.Create(box, g.PositionComp.WorldMatrixNormalizedInv);
            orientedBoundingBoxD.Center *= g.GridSizeR;
            orientedBoundingBoxD.HalfExtent *= g.GridSizeR;
            box = box.TransformFast(g.PositionComp.WorldMatrixNormalizedInv);
            Vector3D min = box.Min;
            Vector3D max = box.Max;
            Vector3I obbPt1 = new Vector3I((int) Math.Round(min.X * g.GridSizeR), (int) Math.Round(min.Y * g.GridSizeR),
                (int) Math.Round(min.Z * g.GridSizeR));
            Vector3I obbPt2 = new Vector3I((int) Math.Round(max.X * g.GridSizeR), (int) Math.Round(max.Y * g.GridSizeR),
                (int) Math.Round(max.Z * g.GridSizeR));
            Vector3I obbMin = Vector3I.Min(obbPt1, obbPt2);
            Vector3I obbMax = Vector3I.Max(obbPt1, obbPt2);
            Vector3I start = Vector3I.Max(obbMin, g.Min);
            Vector3I end = Vector3I.Min(obbMax, g.Max);
            if (start.X > end.X || start.Y > end.Y || start.Z > end.Z)
                return false;
            Vector3I_RangeIterator vector3IRangeIterator = new Vector3I_RangeIterator(ref start, ref end);
            Vector3I next = vector3IRangeIterator.Current;

            while (vector3IRangeIterator.IsValid())
            {
                if (g.GetCubeBlock(next) != null)
                    return true;
                vector3IRangeIterator.GetNext(out next);
            }

            return false;
        }

        public static double GetElevation(this Vector3D v)
        {
            var planet = MyGamePruningStructure.GetClosestPlanet(v);
            return planet != null ? Vector3D.Distance(v, planet.PositionComp.WorldVolume.Center) : v.Length();
        }
    }
}