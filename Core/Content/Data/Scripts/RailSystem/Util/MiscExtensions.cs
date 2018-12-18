using System;
using Sandbox.Game.Entities;
using VRageMath;

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

            var orientedBoundingBoxD = OrientedBoundingBoxD.Create(box, g.PositionComp.WorldMatrixNormalizedInv);
            orientedBoundingBoxD.Center *= g.GridSizeR;
            orientedBoundingBoxD.HalfExtent *= g.GridSizeR;
            box = box.TransformFast(g.PositionComp.WorldMatrixNormalizedInv);
            var min = box.Min;
            var max = box.Max;
            var obbPt1 = new Vector3I((int) Math.Round(min.X * g.GridSizeR), (int) Math.Round(min.Y * g.GridSizeR), (int) Math.Round(min.Z * g.GridSizeR));
            var obbPt2 = new Vector3I((int) Math.Round(max.X * g.GridSizeR), (int) Math.Round(max.Y * g.GridSizeR), (int) Math.Round(max.Z * g.GridSizeR));
            var obbMin = Vector3I.Min(obbPt1, obbPt2);
            var obbMax = Vector3I.Max(obbPt1, obbPt2);
            var start = Vector3I.Max(obbMin, g.Min);
            var end = Vector3I.Min(obbMax, g.Max);
            if (start.X > end.X || start.Y > end.Y || start.Z > end.Z)
                return false;
            var vector3IRangeIterator = new Vector3I_RangeIterator(ref start, ref end);
            var next = vector3IRangeIterator.Current;

            while (vector3IRangeIterator.IsValid())
            {
                if (g.GetCubeBlock(next) != null)
                    return true;
                vector3IRangeIterator.GetNext(out next);
            }

            return false;
        }

        public static T AccessSafe<T>(this T[] array, int index)
        {
            return index >= 0 && index < array.Length ? array[index] : default(T);
        }
    }
}