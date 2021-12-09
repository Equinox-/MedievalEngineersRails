using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Util.EqMath;
using Equinox76561198048419394.RailSystem.Util;
using Equinox76561198048419394.RailSystem.Util.Curve;
using VRage.Components.Entity.Camera;
using VRage.Game;
using VRage.Library.Utils;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Voxel
{
    public interface IGradeShape
    {
        float GetDensity(ref Vector3D worldCoord);
        bool IsInside(Vector3D targetPosition);

        void Draw(MyStringId material);
    }

    public static class GradeShapeHelpers
    {
        public static bool IsInside(this IGradeShape shape, Vector3D position, float margin)
        {
            if (shape.IsInside(position))
                return true;
            if (margin < 0.01f)
                return false;
            const int lenSteps = 2;
            const int dirSteps = 16;
            for (var i = lenSteps; i >= 1; i--)
            for (var j = 0; j < dirSteps; j++)
                if (shape.IsInside(position + (margin * i / lenSteps) * GetRandomVector3Normalized()))
                    return true;
            return false;
        }
        private static float GetRandomFloat(float minValue, float maxValue)
        {
            return MyRandom.Instance.NextFloat() * (maxValue - minValue) + minValue;
        }

        private static float GetRandomRadian()
        {
            return GetRandomFloat(0.0f, 6.283186f);
        }
        private static Vector3 GetRandomVector3Normalized()
        {
            float randomRadian = GetRandomRadian();
            float randomFloat = GetRandomFloat(-1f, 1f);
            float num = (float) Math.Sqrt(1.0 - (double) randomFloat * (double) randomFloat);
            return new Vector3((double) num * Math.Cos((double) randomRadian), (double) num * Math.Sin((double) randomRadian), (double) randomFloat);
        }
    }

    public class CompositeGradeShape : IGradeShape
    {
        private readonly MeshGradeShape[] _components;

        private CompositeGradeShape(params MeshGradeShape[] components)
        {
            _components = components;
        }

        public static IGradeShape Composite(params MeshGradeShape[] shapes)
        {
            return shapes.Length == 1 ? (IGradeShape) shapes[0] : new CompositeGradeShape(shapes);
        }

        public float GetDensity(ref Vector3D worldCoord)
        {
            
            List<MeshGradeShape> maskedBorrow;
            using (PoolManager.Get(out maskedBorrow))
            {
                const int iterations = MeshGradeShape.DefaultDensityIterations;
                var intersectsSurface = false;
                foreach (var c in _components)
                    if (c.IntersectsSurface(ref worldCoord))
                    {
                        intersectsSurface = true;
                        maskedBorrow.Add(c);
                    }
                    else if (c.IsInside(worldCoord))
                        return 1f;

                if (!intersectsSurface)
                    return 0f;

                List<List<int>> borrowed;
                using (PoolManager.Get(out borrowed))
                {
                    foreach (var k in borrowed)
                        k.Clear();
                    while (borrowed.Count < maskedBorrow.Count)
                        borrowed.Add(new List<int>());

                    var inflatedBox = new BoundingBoxD(worldCoord - 0.5f, worldCoord + 0.5f);
                    var queryBox = new BoundingBoxD(inflatedBox.Min - 0.1f, inflatedBox.Max + 0.1f + MeshGradeShape.QueryDir * 1000);
                    for (var i = 0; i < maskedBorrow.Count; i++)
                        maskedBorrow[i].QueryTriangles(queryBox, borrowed[i]);

                    var max = new Vector3I(iterations, iterations, iterations);
                    var hit = 0;
                    for (var itr = new Vector3I_RangeIterator(ref Vector3I.Zero, ref max); itr.IsValid(); itr.MoveNext())
                    {
                        var sample = inflatedBox.Min + inflatedBox.Extents * itr.Current / iterations;
                        for (var i = 0; i < maskedBorrow.Count; i++)
                            if (maskedBorrow[i].IsInsideHelper(sample, borrowed[i]))
                            {
                                hit++;
                                break;
                            }
                    }

                    return hit / (float) ((iterations + 1) * (iterations + 1) * (iterations + 1));
                }
            }
        }

        public bool IsInside(Vector3D targetPosition)
        {
            foreach (var c in _components)
                if (c.IsInside(targetPosition))
                    return true;
            return false;
        }

        public void Draw(MyStringId material)
        {
            foreach (var c in _components)
                c.Draw(material);
        }
    }

    public class MeshGradeShape : IGradeShape
    {
        public const int DefaultDensityIterations = 3;

        private readonly EdgeBlit _edge;
        private readonly ICurve _curve;
        private readonly TriangleUtil.Triangle[] _tris;
        public readonly BoundingBoxD Box;

        public MeshGradeShape(EdgeBlit edge, ICurve curve, TriangleUtil.Triangle[] tri)
        {
            var box = BoundingBoxD.CreateInvalid();
            foreach (var k in tri)
            {
               box.Include(k.Origin);
               box.Include(k.Origin + k.Edge1);
               box.Include(k.Origin + k.Edge2);
            }

            Box = box.Inflate(0.5f);
            _tris = tri;
            _edge = edge;
            _curve = curve;
        }

        public static MeshGradeShape CreateTunnelShape(EdgeBlit edge, float width, float relaxAngle, float shiftUp, int segments, float height, float endPadding)
        {
            var curve = edge.Curve.Convert();

            var tris = new List<TriangleUtil.Triangle>();
            // {-Level, -OuterEdge, -Ceiling, +Level, +OuterEdge, +Ceiling}
            var lastCross = new Vector3D[6];
            var currCross = new Vector3D[6];
            var halfHeight = height / 2;
            var dropTop = height / 5;
            var ceilingPrevious = Vector3D.Zero;

            for (var i = 0; i <= segments; i++)
            {
                var t = i / (float) segments;
                var loc = curve.Sample(t);
                var tangent = (Vector3) curve.SampleDerivative(t);
                tangent.Normalize();
                if (i == 0)
                    loc -= tangent * endPadding;
                if (i == segments)
                    loc += tangent * endPadding;
                var up = Vector3.Lerp(edge.FromUp, edge.ToUp, t);
                up.Normalize();
                loc += shiftUp * up;
                var ceilingTotal = loc - up * height;

                var normal = Vector3.Cross(up, tangent);
                normal.Normalize();


                var perturbLow = Vector3.Zero;
                if (i == 0 || i == segments)
                {
                    var n = i == 0 ? -tangent : tangent;
                    perturbLow = (float) Math.Cos(relaxAngle) * n * Math.Abs(halfHeight);
                }

                for (var j = -1; j <= 1; j += 2)
                {
                    var offset = (j + 1) * 3 / 2;

                    var origin = loc + (width / 2) * j * normal;
                    var rot = Matrix.CreateFromAxisAngle(tangent, -j * Math.Sign(halfHeight) * relaxAngle);
                    var pnorm = Vector3.TransformNormal(up, rot);

                    var cotan = Vector3.Cross(pnorm, tangent);
                    cotan.Normalize();
                    cotan *= -Math.Sign(Vector3.Dot(cotan, up));
                    var p2 = origin + halfHeight * cotan + perturbLow;

                    var ceilingEdge = origin - up * (height - dropTop);

                    if (i > 0)
                    {
                        // side faces, lower
                        tris.Add(new TriangleUtil.Triangle(origin, lastCross[offset], lastCross[offset + 1], pnorm));
                        
                        var dissolvedOuterEdge = (p2 - lastCross[offset + 1]).Dot(tangent) <= 0; 
                        if (dissolvedOuterEdge)
                            p2 = lastCross[offset + 1];
                        else
                            tris.Add(new TriangleUtil.Triangle(origin, lastCross[offset + 1], p2, pnorm));

                        var dissolvedCeilingEdge = (ceilingEdge - lastCross[offset + 2]).Dot(tangent) <= 0;
                        if (dissolvedCeilingEdge)
                            ceilingEdge = lastCross[offset + 2];
                        
                        // side faces, upper:
                        if (!dissolvedCeilingEdge)
                            tris.Add(new TriangleUtil.Triangle(lastCross[offset + 1], lastCross[offset + 2], ceilingEdge, pnorm));
                        if (!dissolvedOuterEdge)
                            tris.Add(new TriangleUtil.Triangle(lastCross[offset + 1],  p2, ceilingEdge, pnorm));
                    }

                    currCross[offset] = origin;
                    currCross[offset + 1] = p2;
                    currCross[offset + 2] = ceilingEdge;
                }

                if (i > 0)
                {
                    // top
                    tris.Add(new TriangleUtil.Triangle(currCross[0], lastCross[0], lastCross[3], -up));
                    tris.Add(new TriangleUtil.Triangle(currCross[0], lastCross[3], currCross[3], -up));

                    // bottom
                    tris.Add(new TriangleUtil.Triangle(currCross[2], lastCross[2], ceilingPrevious, up));
                    tris.Add(new TriangleUtil.Triangle(currCross[2], ceilingPrevious, ceilingTotal, up));
                    
                    tris.Add(new TriangleUtil.Triangle(currCross[5], lastCross[5], ceilingPrevious, up));
                    tris.Add(new TriangleUtil.Triangle(currCross[5], ceilingPrevious, ceilingTotal, up));
                }

                // end caps
                if (i == 0 || i == segments)
                {
                    var n = i == 0 ? -tangent : tangent;
                    var avg = (currCross[0] + currCross[1] + currCross[2] + currCross[3] + currCross[4] + currCross[5]) / 6;
                    tris.Add(new TriangleUtil.Triangle(currCross[0], currCross[1], avg, n));
                    tris.Add(new TriangleUtil.Triangle(currCross[1], currCross[2], avg, n));
                    tris.Add(new TriangleUtil.Triangle(currCross[2], ceilingTotal, avg, n));
                    tris.Add(new TriangleUtil.Triangle(ceilingTotal, currCross[5], avg, n));
                    tris.Add(new TriangleUtil.Triangle(currCross[5], currCross[4], avg, n));
                    tris.Add(new TriangleUtil.Triangle(currCross[4], currCross[3], avg, n));
                    tris.Add(new TriangleUtil.Triangle(currCross[3], currCross[0], avg, n));
                }
                var tmp = lastCross;
                lastCross = currCross;
                currCross = tmp;
                ceilingPrevious = ceilingTotal;
            }

            return new MeshGradeShape(edge, curve, tris.ToArray());
        }
        
        public static MeshGradeShape CreateGradeShape(EdgeBlit edge, float width, float relaxAngle, float shiftUp, int segments, float maxDepth, float endPadding)
        {
            var curve = edge.Curve.Convert();

            var tris = new List<TriangleUtil.Triangle>();
            var lastCross = new Vector3D[4];
            var currCross = new Vector3D[4];

            for (var i = 0; i <= segments; i++)
            {
                var t = i / (float) segments;
                var loc = curve.Sample(t);
                var tangent = (Vector3) curve.SampleDerivative(t);
                tangent.Normalize();
                if (i == 0)
                    loc -= tangent * endPadding;
                if (i == segments)
                    loc += tangent * endPadding;
                var up = Vector3.Lerp(edge.FromUp, edge.ToUp, t);
                up.Normalize();
                loc += shiftUp * up;

                var normal = Vector3.Cross(up, tangent);
                normal.Normalize();


                var perturbLow = Vector3.Zero;
                if (i == 0 || i == segments)
                {
                    var n = i == 0 ? -tangent : tangent;
                    perturbLow = (float) Math.Cos(relaxAngle) * n * Math.Abs(maxDepth);
                }

                for (var j = -1; j <= 1; j += 2)
                {
                    var offset = j + 1;

                    var origin = loc + (width / 2) * j * normal;
                    var rot = Matrix.CreateFromAxisAngle(tangent, -j * Math.Sign(maxDepth) * relaxAngle);
                    var pnorm = Vector3.TransformNormal(up, rot);

                    var cotan = Vector3.Cross(pnorm, tangent);
                    cotan.Normalize();
                    cotan *= -Math.Sign(Vector3.Dot(cotan, up));
                    var p2 = origin + maxDepth * cotan + perturbLow;

                    if (i > 0)
                    {
                        tris.Add(new TriangleUtil.Triangle(origin, lastCross[offset], lastCross[offset + 1], pnorm));
                        if ((p2 - lastCross[offset + 1]).Dot(tangent) > 0)
                            tris.Add(new TriangleUtil.Triangle(origin, lastCross[offset + 1], p2, pnorm));
                        else
                            p2 = lastCross[offset + 1];
                    }

                    currCross[offset] = origin;
                    currCross[offset + 1] = p2;
                }

                if (i > 0)
                {
                    tris.Add(new TriangleUtil.Triangle(currCross[0], lastCross[0], lastCross[2], up));
                    tris.Add(new TriangleUtil.Triangle(currCross[0], lastCross[2], currCross[2], up));

                    tris.Add(new TriangleUtil.Triangle(currCross[1], lastCross[1], lastCross[3], -up));
                    tris.Add(new TriangleUtil.Triangle(currCross[1], lastCross[3], currCross[3], -up));
                }

                if (i == 0 || i == segments)
                {
                    var n = i == 0 ? -tangent : tangent;
                    tris.Add(new TriangleUtil.Triangle(currCross[0], currCross[2], currCross[3], n));
                    tris.Add(new TriangleUtil.Triangle(currCross[0], currCross[3], currCross[1], n));
                }
                var tmp = lastCross;
                lastCross = currCross;
                currCross = tmp;
            }

            return new MeshGradeShape(edge, curve, tris.ToArray());
        }

        public float GetDensity(ref Vector3D worldCoord)
        {
            return GetDensity(ref worldCoord, DefaultDensityIterations);
        }

        public bool IsInside(Vector3D pt)
        {
            return IsInsideHelper(pt, null);
        }

        internal static readonly Vector3D QueryDir = Vector3D.Up;

        internal bool IsInsideHelper(Vector3D pt, List<int> selector)
        {
            // ReSharper disable once ImpureMethodCallOnReadonlyValueField
            if (Box.Contains(pt) == ContainmentType.Disjoint)
                return false;
            var ray = new RayD(pt, QueryDir);
            double dist;
            var i = 0;
            if (selector != null)
            {
                foreach (var id in selector)
                {
                    var k = _tris[id];
                    if (k.Intersects(ref ray, out dist))
                        i++;
                }
            }
            else
            {
                foreach (var k in _tris)
                    if (k.Intersects(ref ray, out dist))
                        i++;
            }

            return (i & 1) == 1;
        }

        private static readonly ConcurrentStack<List<int>> _stackBorrow = new ConcurrentStack<List<int>>();

        internal bool IntersectsSurface(ref Vector3D voxelCoord)
        {
            if (Box.Contains(voxelCoord) == ContainmentType.Disjoint)
                return false;
            var inflatedBox = new BoundingBoxD(voxelCoord - 0.5f, voxelCoord + 0.5f);
            foreach (var k in _tris)
                if (inflatedBox.IntersectsTriangle(k.Origin, k.Origin + k.Edge1, k.Origin + k.Edge2))
                    return true;
            return false;
        }

        internal void QueryTriangles(BoundingBoxD queryBox, List<int> indices)
        {
            for (var index = 0; index < _tris.Length; index++)
            {
                var k = _tris[index];
                if (queryBox.IntersectsTriangle(k.Origin, k.Origin + k.Edge1, k.Origin + k.Edge2))
                    indices.Add(index);
            }
        }

        private float GetDensity(ref Vector3D voxelCoord, int iterations)
        {
            // ReSharper disable once ImpureMethodCallOnReadonlyValueField
            if (Box.Contains(voxelCoord) == ContainmentType.Disjoint)
                return 0f;
            var inflatedBox = new BoundingBoxD(voxelCoord - 0.5f, voxelCoord + 0.5f);
            var queryBox = new BoundingBoxD(inflatedBox.Min - 0.1f, inflatedBox.Max + 0.1f + QueryDir * 1000);

            {
                var hits = 0;
                foreach (var k in _tris)
                {
                    if (!inflatedBox.IntersectsTriangle(k.Origin, k.Origin + k.Edge1, k.Origin + k.Edge2))
                        continue;
                    hits++;
                    break;
                }

                if (hits == 0)
                    return IsInside(voxelCoord) ? 1 : 0;
            }

            List<int> tmp;
            if (!_stackBorrow.TryPop(out tmp))
                tmp = new List<int>();
            else
                tmp.Clear();
            QueryTriangles(queryBox, tmp);

            var max = new Vector3I(iterations, iterations, iterations);
            var hit = 0;
            for (var itr = new Vector3I_RangeIterator(ref Vector3I.Zero, ref max); itr.IsValid(); itr.MoveNext())
            {
                var sample = inflatedBox.Min + inflatedBox.Extents * itr.Current / iterations;
                if (IsInsideHelper(sample, tmp))
                    hit++;
            }

            _stackBorrow.Push(tmp);
            return hit / (float) ((iterations + 1) * (iterations + 1) * (iterations + 1));
        }

        public void Draw(MyStringId material)
        {
            if (MyCameraComponent.ActiveCamera != null && MyCameraComponent.ActiveCamera.GetCameraFrustum().Contains(Box) == ContainmentType.Disjoint)
                return;
            foreach (var t in _tris)
            {
                var p0 = t.Origin;
                var p1 = t.Origin + t.Edge1;
                var p2 = t.Origin + t.Edge2;
                var n = Vector3.Cross(t.Edge1, t.Edge2);
                MyTransparentGeometry.AddTriangleBillboard(p0, p1, p2, n, n, n, Vector2.Zero, new Vector2(1, 0), new Vector2(0, 1), material, 0,
                    (p0 + p1 + p2) / 3);
            }
        }
    }
}