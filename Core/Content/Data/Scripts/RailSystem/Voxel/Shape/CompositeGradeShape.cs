using System;
using System.Collections.Generic;
using VRage.Library.Collections;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.RailSystem.Voxel.Shape
{
    public class CompositeGradeShape : IGradeShape
    {
        private readonly IGradeShape[] _shapes;

        private CompositeGradeShape(IGradeShape[] shapes)
        {
            _shapes = shapes;
        }

        public static IGradeShape Composite(IEnumerable<IGradeShape> shapes)
        {
            using (PoolManager.Get(out List<IGradeShape> flat))
            {
                foreach (var shape in shapes)
                    switch (shape)
                    {
                        case CompositeGradeShape composite:
                            flat.AddCollection(composite._shapes, composite._shapes.Length);
                            break;
                        default:
                            flat.Add(shape);
                            break;
                    }

                return flat.Count == 1 ? flat[0] : new CompositeGradeShape(flat.ToArray());
            }
        }

        public void CollectCacheKeys(Action<BoundingBoxD, object> consumer)
        {
            foreach (var shape in _shapes)
                shape.CollectCacheKeys(consumer);
        }

        public float GetDensity(in Vector3D worldCoord)
        {
            var density = 0f;
            foreach (var shape in _shapes)
            {
                density = Math.Max(density, shape.GetDensity(in worldCoord));
                if (density >= 0.9999f) return density;
            }
            return density;
        }

        public bool IsInside(Vector3D targetPosition)
        {
            foreach (var c in _shapes)
                if (c.IsInside(targetPosition))
                    return true;
            return false;
        }

        public void DrawShape(MyStringId material)
        {
            foreach (var c in _shapes)
                c.DrawShape(material);
        }

        public void DrawQuery(Vector3D query)
        {
            foreach (var c in _shapes)
                c.DrawQuery(query);
            var density = GetDensity(query);
            MyRenderProxy.DebugDrawText3D(query, $"{(byte)(density * byte.MaxValue)}", density > 0 ? Color.Red : Color.White, 0.75f, false);
        }
    }
}