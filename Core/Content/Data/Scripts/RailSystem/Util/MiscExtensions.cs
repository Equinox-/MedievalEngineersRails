using System;
using VRage.Components.Entity.CubeGrid;
using VRage.Definitions.Grid;
using VRage.Game;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util
{
    public static class MiscExtensions
    {
        public static bool AnyBlocksInAABB(this MyGridDataComponent g, BoundingBoxD box)
        {
            var e = g.Entity;
            if (e.PositionComp == null)
                return false;
            if (box.Contains(e.PositionComp.WorldAABB) == ContainmentType.Contains)
                return g.BlockCount > 0;

            var orientedBoundingBoxD = OrientedBoundingBoxD.Create(box, e.PositionComp.WorldMatrixNormalizedInv);
            var sizeR = 1f / g.Size;
            orientedBoundingBoxD.Center *= sizeR;
            orientedBoundingBoxD.HalfExtent *= sizeR;
            box = box.TransformFast(e.PositionComp.WorldMatrixNormalizedInv);
            var min = box.Min;
            var max = box.Max;
            var obbPt1 = new Vector3I((int) Math.Round(min.X * sizeR), (int) Math.Round(min.Y * sizeR), (int) Math.Round(min.Z * sizeR));
            var obbPt2 = new Vector3I((int) Math.Round(max.X * sizeR), (int) Math.Round(max.Y * sizeR), (int) Math.Round(max.Z * sizeR));
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
                if (g.GetAnyBlock(next) != null)
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