using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.RailSystem.Util;
using VRage.Library.Collections;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Voxel.Shape
{
    public class CompositeGradeShape : IGradeShape
    {
        private readonly MeshGradeShape[] _meshShapes;
        private readonly IGradeShape[] _genericShapes;

        private CompositeGradeShape(MeshGradeShape[] meshShapes, IGradeShape[] genericShapes)
        {
            _meshShapes = meshShapes;
            _genericShapes = genericShapes;
        }

        public static IGradeShape Composite(params IGradeShape[] shapes)
        {
            if (shapes.Length == 1)
                return shapes[0];
            using (PoolManager.Get(out List<MeshGradeShape> meshShapes))
            using (PoolManager.Get(out List<IGradeShape> generics))
            {
                foreach (var shape in shapes)
                    switch (shape)
                    {
                        case CompositeGradeShape composite:
                            meshShapes.AddCollection(composite._meshShapes, composite._meshShapes.Length);
                            generics.AddCollection(composite._genericShapes, composite._genericShapes.Length);
                            break;
                        case MeshGradeShape mesh:
                            meshShapes.Add(mesh);
                            break;
                        default:
                            generics.Add(shape);
                            break;
                    }

                return new CompositeGradeShape(
                    meshShapes.Count > 0 ? meshShapes.ToArray() : Array.Empty<MeshGradeShape>(),
                    generics.Count > 0 ? generics.ToArray() : Array.Empty<IGradeShape>());
            }
        }

        public void CollectCacheKeys(Action<BoundingBoxD, object> consumer)
        {
            foreach (var shape in _meshShapes)
                shape.CollectCacheKeys(consumer);
            foreach (var shape in _genericShapes)
                shape.CollectCacheKeys(consumer);
        }

        public float GetDensity(in Vector3D worldCoord)
        {
            var density = 0f;
            foreach (var shape in _genericShapes)
            {
                density = Math.Max(density, shape.GetDensity(in worldCoord));
                if (density >= 0.9999f) return density;
            }

            density = Math.Max(density, GetMeshDensity(in worldCoord));
            return density;
        }

        private float GetMeshDensity(in Vector3D worldCoord) {
            using (PoolManager.Get(out List<MeshGradeShape> relMesh))
            {
                const int iterations = MeshGradeShape.DefaultDensityIterations;
                var intersectsSurface = false;
                foreach (var c in _meshShapes)
                {
                    var local = (Vector3)(worldCoord - c.Offset);
                    if (c.IntersectsSurfaceLocal(local))
                    {
                        intersectsSurface = true;
                        relMesh.Add(c);
                    }
                    else if (c.IsInsideLocal(local, null))
                        return 1f;
                }

                if (!intersectsSurface)
                    return 0f;

                using (PoolManager.Get(out List<List<int>> triIndex))
                {
                    while (triIndex.Count < relMesh.Count)
                        triIndex.Add(PoolManager.Get<List<int>>());

                    var inflatedBox = new BoundingBoxD(worldCoord - 0.5f, worldCoord + 0.5f);
                    var queryBox = new BoundingBoxD(inflatedBox.Min - 0.1f, inflatedBox.Max + 0.1f + MeshGradeShape.QueryDir * 1000);
                    for (var i = 0; i < relMesh.Count; i++)
                    {
                        var localQuery = new BoundingBox((Vector3)(queryBox.Min - relMesh[i].Offset), (Vector3)(queryBox.Max - relMesh[i].Offset));
                        relMesh[i].QueryTriangles(localQuery, triIndex[i]);
                    }

                    var max = new Vector3I(iterations, iterations, iterations);
                    var hit = 0;
                    for (var itr = new Vector3I_RangeIterator(ref Vector3I.Zero, ref max); itr.IsValid(); itr.MoveNext())
                    {
                        var sample = inflatedBox.Min + inflatedBox.Extents * itr.Current / iterations;
                        for (var i = 0; i < relMesh.Count; i++)
                            if (relMesh[i].IsInsideLocal((Vector3)(sample - relMesh[i].Offset), triIndex[i]))
                            {
                                hit++;
                                break;
                            }
                    }

                    foreach (var entry in triIndex)
                    {
                        var copy = entry;
                        PoolManager.Return(ref copy);
                    }

                    triIndex.Clear();

                    return hit / (float)((iterations + 1) * (iterations + 1) * (iterations + 1));
                }
            }
        }

        public bool IsInside(Vector3D targetPosition)
        {
            foreach (var c in _genericShapes)
                if (c.IsInside(targetPosition))
                    return true;
            foreach (var c in _meshShapes)
                if (c.IsInside(targetPosition))
                    return true;
            return false;
        }

        public void Draw(MyStringId material)
        {
            foreach (var c in _genericShapes)
                c.Draw(material);
            foreach (var c in _meshShapes)
                c.Draw(material);
        }
    }
}