using System;
using System.Collections.Generic;
using Equinox76561198048419394.RailSystem.Util;
using VRage;
using VRage.Components.Entity.Camera;
using VRage.Game;
using VRage.Library.Collections;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Voxel.Shape
{
    public class MeshGradeShape : IGradeShape
    {
        public const int DefaultDensityIterations = 3;

        internal readonly Vector3D Offset;
        private readonly TriangleUtil.Triangle[] _tris;
        private readonly object _cacheKey;
        private readonly BoundingBox _localBox;
        private BoundingBoxD WorldBox => new BoundingBoxD(_localBox.Min + Offset, _localBox.Max + Offset);

        private MeshGradeShape(Vector3D offset, TriangleUtil.Triangle[] tri, object cacheKey)
        {
            _cacheKey = cacheKey;
            var box = BoundingBox.CreateInvalid();
            foreach (var k in tri)
            {
                box.Include(k.Origin);
                box.Include(k.Origin + k.Edge1);
                box.Include(k.Origin + k.Edge2);
            }
            box.Inflate(0.5f);
            _localBox = box;
            Offset = offset;
            _tris = tri;
        }

        public struct GradingParams
        {
            public float Width;
            public float RelaxAngleRadians;
            public float ShiftUp;
            public int Segments;
            public float Height;
            public float EndPadding;
        }

        public static MeshGradeShape CreateTunnelShape(EdgeBlit edge, GradingParams args)
        {
            using (PoolManager.Get(out List<TriangleUtil.Triangle> triangles))
                return CreateTunnelShapeInternal(edge, args, triangles);
        }

        private static MeshGradeShape CreateTunnelShapeInternal(EdgeBlit edge, GradingParams args, List<TriangleUtil.Triangle> tris) {
            var curve = edge.Curve.Convert();
            var pivot = curve.Sample(0.5f);
            // {-Level, -OuterEdge, -Ceiling, +Level, +OuterEdge, +Ceiling}
            var lastCross = new Vector3[6];
            var currCross = new Vector3[6];
            var halfHeight = args.Height / 2;
            var dropTop = args.Height / 5;
            var ceilingPrevious = Vector3.Zero;

            for (var i = 0; i <= args.Segments; i++)
            {
                var t = i / (float) args.Segments;
                var loc = (Vector3) (curve.Sample(t) - pivot);
                var tangent = (Vector3) curve.SampleDerivative(t);
                tangent.Normalize();
                if (i == 0)
                    loc -= tangent * args.EndPadding;
                if (i == args.Segments)
                    loc += tangent * args.EndPadding;
                var up = Vector3.Lerp(edge.FromUp, edge.ToUp, t);
                up.Normalize();
                loc += args.ShiftUp * up;
                var ceilingTotal = loc - up * args.Height;

                var normal = Vector3.Cross(up, tangent);
                normal.Normalize();


                var perturbLow = Vector3.Zero;
                if (i == 0 || i == args.Segments)
                {
                    var n = i == 0 ? -tangent : tangent;
                    perturbLow = (float) Math.Cos(args.RelaxAngleRadians) * n * Math.Abs(halfHeight);
                }

                for (var j = -1; j <= 1; j += 2)
                {
                    var offset = (j + 1) * 3 / 2;

                    var origin = loc + (args.Width / 2) * j * normal;
                    var rot = Matrix.CreateFromAxisAngle(tangent, -j * Math.Sign(halfHeight) * args.RelaxAngleRadians);
                    var pnorm = Vector3.TransformNormal(up, rot);

                    var cotan = Vector3.Cross(pnorm, tangent);
                    cotan.Normalize();
                    cotan *= -Math.Sign(Vector3.Dot(cotan, up));
                    var p2 = origin + halfHeight * cotan + perturbLow;

                    var ceilingEdge = origin - up * (args.Height - dropTop);

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
                if (i == 0 || i == args.Segments)
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
                MyUtils.Swap(ref lastCross, ref currCross);
                ceilingPrevious = ceilingTotal;
            }

            return new MeshGradeShape(pivot, tris.ToArray(), MyTuple.Create(edge, args));
        }

        public static MeshGradeShape CreateGradeShape(EdgeBlit edge, GradingParams args)
        {
            using (PoolManager.Get(out List<TriangleUtil.Triangle> triangles))
                return CreateGradeShapeInternal(edge, args, triangles);
        }

        private static MeshGradeShape CreateGradeShapeInternal(EdgeBlit edge, GradingParams args, List<TriangleUtil.Triangle> tris) {
            var curve = edge.Curve.Convert();
            var pivot = curve.Sample(0.5f);
            var lastCross = new Vector3[4];
            var currCross = new Vector3[4];

            for (var i = 0; i <= args.Segments; i++)
            {
                var t = i / (float) args.Segments;
                var loc = (Vector3) (curve.Sample(t) - pivot);
                var tangent = (Vector3) curve.SampleDerivative(t);
                tangent.Normalize();
                if (i == 0)
                    loc -= tangent * args.EndPadding;
                if (i == args.Segments)
                    loc += tangent * args.EndPadding;
                var up = Vector3.Lerp(edge.FromUp, edge.ToUp, t);
                up.Normalize();
                loc += args.ShiftUp * up;

                var normal = Vector3.Cross(up, tangent);
                normal.Normalize();


                var perturbLow = Vector3.Zero;
                if (i == 0 || i == args.Segments)
                {
                    var n = i == 0 ? -tangent : tangent;
                    perturbLow = (float) Math.Cos(args.RelaxAngleRadians) * n * Math.Abs(args.Height);
                }

                for (var j = -1; j <= 1; j += 2)
                {
                    var offset = j + 1;

                    var origin = loc + (args.Width / 2) * j * normal;
                    var rot = Matrix.CreateFromAxisAngle(tangent, -j * Math.Sign(args.Height) * args.RelaxAngleRadians);
                    var pnorm = Vector3.TransformNormal(up, rot);

                    var cotan = Vector3.Cross(pnorm, tangent);
                    cotan.Normalize();
                    cotan *= -Math.Sign(Vector3.Dot(cotan, up));
                    var p2 = origin + args.Height * cotan + perturbLow;

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

                if (i == 0 || i == args.Segments)
                {
                    var n = i == 0 ? -tangent : tangent;
                    tris.Add(new TriangleUtil.Triangle(currCross[0], currCross[2], currCross[3], n));
                    tris.Add(new TriangleUtil.Triangle(currCross[0], currCross[3], currCross[1], n));
                }
                MyUtils.Swap(ref lastCross, ref currCross);
            }

            return new MeshGradeShape(pivot, tris.ToArray(), MyTuple.Create(edge, args));
        }

        public float GetDensity(in Vector3D worldCoord)
        {
            return GetDensity(in worldCoord, DefaultDensityIterations);
        }

        public bool IsInside(Vector3D pt)
        {
            return IsInsideLocal((Vector3)(pt - Offset), null);
        }

        internal static readonly Vector3 QueryDir = Vector3.Up;

        internal bool IsInsideLocal(Vector3 pt, List<int> selector)
        {
            // ReSharper disable once ImpureMethodCallOnReadonlyValueField
            if (_localBox.Contains(pt) == ContainmentType.Disjoint)
                return false;
            var ray = new Ray(pt, QueryDir);
            var intersections = 0;
            if (selector != null)
            {
                foreach (var id in selector)
                {
                    if (_tris[id].Intersects(ref ray, out _))
                        intersections++;
                }
            }
            else
            {
                for (var i =0; i<_tris.Length; i++)
                    if (_tris[i].Intersects(ref ray, out _))
                        intersections++;
            }

            return (intersections & 1) == 1;
        }

        internal bool IntersectsSurfaceLocal(in Vector3 localCoord)
        {
            if (_localBox.Contains(localCoord) == ContainmentType.Disjoint)
                return false;
            var inflatedBox = new BoundingBox(localCoord - 0.5f, localCoord + 0.5f);
            foreach (var k in _tris)
                if (inflatedBox.IntersectsTriangle(k.Origin, k.Origin + k.Edge1, k.Origin + k.Edge2))
                    return true;
            return false;
        }

        internal void QueryTriangles(BoundingBox localBox, List<int> indices)
        {
            for (var index = 0; index < _tris.Length; index++)
            {
                var k = _tris[index];
                if (localBox.IntersectsTriangle(k.Origin, k.Origin + k.Edge1, k.Origin + k.Edge2))
                    indices.Add(index);
            }
        }

        private float GetDensity(in Vector3D coord, int iterations)
        {
            var localCoord = (Vector3)(coord - Offset);
            // ReSharper disable once ImpureMethodCallOnReadonlyValueField
            if (_localBox.Contains(localCoord) == ContainmentType.Disjoint)
                return 0f;
            var localBox = new BoundingBox(localCoord - 0.5f, localCoord + 0.5f);
            var queryBox = new BoundingBox(localBox.Min - 0.1f, localBox.Max + 0.1f + QueryDir * 1000);

            {
                var hits = 0;
                foreach (var k in _tris)
                {
                    if (!localBox.IntersectsTriangle(k.Origin, k.Origin + k.Edge1, k.Origin + k.Edge2))
                        continue;
                    hits++;
                    break;
                }

                if (hits == 0)
                    return IsInside(coord) ? 1 : 0;
            }

            var hit = 0;
            using (PoolManager.Get(out List<int> tmp))
            {
                QueryTriangles(queryBox, tmp);
                var max = new Vector3I(iterations, iterations, iterations);
                for (var itr = new Vector3I_RangeIterator(ref Vector3I.Zero, ref max); itr.IsValid(); itr.MoveNext())
                {
                    var sample = localBox.Min + localBox.Extents * itr.Current / iterations;
                    if (IsInsideLocal(sample, tmp))
                        hit++;
                }
            }

            return hit / (float) ((iterations + 1) * (iterations + 1) * (iterations + 1));
        }

        public void Draw(MyStringId material)
        {
            if (MyCameraComponent.ActiveCamera != null && MyCameraComponent.ActiveCamera.GetCameraFrustum().Contains(WorldBox) == ContainmentType.Disjoint)
                return;
            foreach (var t in _tris)
            {
                var p0 = t.Origin + Offset;
                var p1 = p0 + t.Edge1;
                var p2 = p0 + t.Edge2;
                var n = Vector3.Cross(t.Edge1, t.Edge2);
                MyTransparentGeometry.AddTriangleBillboard(p0, p1, p2, n, n, n, Vector2.Zero, new Vector2(1, 0), new Vector2(0, 1), material, 0,
                    (p0 + p1 + p2) / 3);
            }
        }

        public void CollectCacheKeys(Action<BoundingBoxD, object> consumer) => consumer(WorldBox, _cacheKey);
    }
}