using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.RailSystem.Util;
using VRage;
using VRage.Collections;
using VRage.Components.Entity.Camera;
using VRage.Game;
using VRage.Library.Collections;
using VRage.Logging;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.RailSystem.Voxel.Shape
{
    public class MeshGradeShape : IGradeShape
    {
        public const int DefaultDensityIterations = 3;

        internal readonly Vector3D Offset;
        private readonly Vector3[] _vertexPos;
        private readonly Vector3[] _vertexNorm;
        private readonly Tri[] _tri;
        private readonly object _cacheKey;
        private readonly BoundingBox _localBox;
        private BoundingBoxD WorldBox => new BoundingBoxD(_localBox.Min + Offset, _localBox.Max + Offset);

        private struct Tri
        {
            /// <summary>
            /// Index of each vertex in the triangle.
            /// </summary>
            public ushort V0, V1, V2;

            /// <summary>
            /// Index of triangle opposing each edge.
            /// </summary>
            public ushort F01, F12, F20;

            /// <summary>
            /// Plane representing the triangle.
            /// </summary>
            public Plane Plane;

            /// <summary>
            /// Edge lengths
            /// </summary>
            public float L01, L02, L12;
        }

        private MeshGradeShape(Vector3D offset, MeshBuffer buffer, object cacheKey)
        {
            Offset = offset;
            _cacheKey = cacheKey;
            _vertexPos = new Vector3[buffer.Vertices.Count];
            _vertexNorm = new Vector3[buffer.Vertices.Count];
            var box = BoundingBox.CreateInvalid();
            for (var i = 0; i < buffer.Vertices.Count; i++)
                box.Include(_vertexPos[i] = buffer.Vertices[i]);
            box.Inflate(0.5f);
            _localBox = box;
            _tri = new Tri[buffer.Indices.Count / 3];

            using (PoolManager.Get(out Dictionary<uint, ushort> edgeToTri))
            {
                for (ushort i = 0; i < _tri.Length; i++)
                {
                    ref var t = ref _tri[i];
                    var off = i * 3;
                    t.V0 = buffer.Indices[off];
                    t.V1 = buffer.Indices[off + 1];
                    t.V2 = buffer.Indices[off + 2];
                    var p0 = _vertexPos[t.V0];
                    var p1 = _vertexPos[t.V1];
                    var p2 = _vertexPos[t.V2];

                    // Compute triangle normal
                    var n = Vector3.Cross(p1 - p0, p2 - p0);
                    n.Normalize();
                    t.Plane = new Plane(p0, n);

                    // Store mapping from half edge to triangle ID
                    void AddFaceMapping(uint edge, ushort idx)
                    {
                        if (edgeToTri.TryGetValue(edge, out var other))
                        {
                            ref var ct = ref _tri[idx];
                            ref var ot = ref _tri[other];
                            MyLog.Default.WriteLine($"Triangle already existed in face mapping... ({ct.V0} {ct.V1} {ct.V2}) and ({ot.V0} {ot.V1} {ot.V2})");
                        }

                        edgeToTri[edge] = idx;
                    }

                    AddFaceMapping(PackEdge(t.V0, t.V1), i);
                    AddFaceMapping(PackEdge(t.V1, t.V2), i);
                    AddFaceMapping(PackEdge(t.V2, t.V0), i);

                    // Compute edges
                    var e01 = p1 - p0;
                    var e02 = p2 - p0;
                    var e12 = p2 - p1;

                    // Compute edge lengths
                    t.L01 = e01.Length();
                    t.L02 = e02.Length();
                    t.L12 = e12.Length();

                    // Compute interior angles
                    var v0W = (float)Math.Acos(Vector3.Dot(e01, e02) / (t.L01 * t.L02));
                    var v1W = (float)Math.Acos(Vector3.Dot(-e01, e12) / (t.L01 * t.L12));
                    var v2W = MathHelper.Pi - v0W - v1W;

                    // Accumulate angle weighted pseudo-normals
                    _vertexNorm[t.V0] += n * v0W;
                    _vertexNorm[t.V1] += n * v1W;
                    _vertexNorm[t.V2] += n * v2W;
                }

                for (ushort i = 0; i < _tri.Length; i++)
                {
                    ref var t = ref _tri[i];

                    // Pull the triangle for the opposite half edge for each triangle
                    ushort GetOpposing(uint edge, ushort def)
                    {
                        if (edgeToTri.TryGetValue(edge, out var idx))
                            return idx;
                        ref var ct = ref _tri[def];
                        MyLog.Default.WriteLine($"Edge not in table {edge & 0xFFFF},{edge >> 16} for ({ct.V0} {ct.V1} {ct.V2})");
                        return def;
                    }

                    t.F01 = GetOpposing(PackEdge(t.V1, t.V0), i);
                    t.F12 = GetOpposing(PackEdge(t.V2, t.V1), i);
                    t.F20 = GetOpposing(PackEdge(t.V0, t.V2), i);
                }
            }
        }

        private static uint PackEdge(ushort a, ushort b) => ((uint)a << 16) | b;

        public struct GradingParams
        {
            public float Width;
            public float RelaxAngleRadians;
            public float ShiftUp;
            public int Segments;
            public float Height;
            public float EndPadding;
        }

        private sealed class MeshBuffer
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            private readonly List<ushort> PosContributors = new List<ushort>();
            public readonly List<ushort> Indices = new List<ushort>();

            public void Clear()
            {
                Vertices.Clear();
                PosContributors.Clear();
                Indices.Clear();
            }

            public ushort AddVertex(Vector3 pt)
            {
                Vertices.Add(pt);
                PosContributors.Add(1);
                return (ushort)(Vertices.Count - 1);
            }

            public void AddTriangle(ushort v0, ushort v1, ushort v2, bool flip = false)
            {
                if (v0 == v1 || v1 == v2 || v2 == v0) return;
                if (flip) MyUtils.Swap(ref v1, ref v2);
                Indices.Add(v0);
                Indices.Add(v1);
                Indices.Add(v2);
            }

            public bool ShouldReuse(Vector3 pos, Vector3 tangent, ushort prev, bool allowRejectionCollapse)
            {
                var prevPos = Vertices[prev];
                if (allowRejectionCollapse)
                {
                    var err = prevPos - pos;
                    var project = err.Dot(tangent) * tangent;
                    if (Vector3.DistanceSquared(err, project) < 0.0025) return true;
                }

                var prevDist = prevPos.Dot(tangent);
                var currDist = pos.Dot(tangent);
                if (currDist > prevDist + 0.025) return false;
                var currContrib = PosContributors[prev];
                var newContrib = (ushort)(currContrib + 1);
                Vertices[prev] = (prevPos * currContrib + pos) / newContrib;
                PosContributors[prev] = newContrib;
                return true;
            }
        }

        public static MeshGradeShape CreateTunnelShape(EdgeBlit edge, GradingParams args)
        {
            using (PoolManager.Get(out MeshBuffer meshBuffer))
                return CreateTunnelShapeInternal(edge, args, meshBuffer);
        }

        private struct TunnelSideState
        {
            public ushort Level;
            public ushort OuterEdge;
            public ushort Ceiling;

            public Vector3 Center(MeshBuffer buffer) => (buffer.Vertices[Level] + buffer.Vertices[OuterEdge] + buffer.Vertices[Ceiling]) / 3;
        }

        private struct TunnelState
        {
            public TunnelSideState Negative;
            public TunnelSideState Positive;
            public ushort CeilingTotal;
            public Vector3 Center(MeshBuffer buffer) => (Negative.Center(buffer) * 3 + Positive.Center(buffer) * 3 + buffer.Vertices[CeilingTotal]) / 7;
        }

        private static MeshGradeShape CreateTunnelShapeInternal(EdgeBlit edge, GradingParams args, MeshBuffer meshBuffer)
        {
            meshBuffer.Clear();
            var curve = edge.Curve.Convert();
            var pivot = curve.Sample(0.5f);
            // {-Level, -OuterEdge, -Ceiling, +Level, +OuterEdge, +Ceiling}
            TunnelState lastCross = default;
            TunnelState currCross = default;
            var halfHeight = args.Height / 2;
            var dropTop = args.Height / 5;

            for (var i = 0; i <= args.Segments; i++)
            {
                var t = i / (float)args.Segments;
                var loc = (Vector3)(curve.Sample(t) - pivot);
                var tangent = (Vector3)curve.SampleDerivative(t);
                tangent.Normalize();
                if (i == 0)
                    loc -= tangent * args.EndPadding;
                if (i == args.Segments)
                    loc += tangent * args.EndPadding;
                var up = Vector3.Lerp(edge.FromUp, edge.ToUp, t);
                up.Normalize();
                loc += args.ShiftUp * up;
                var startOrEnd = i == 0 || i == args.Segments;
                var ceilingTotalLoc = loc - up * args.Height;

                bool ShouldReuse(Vector3 pos, Vector3 tan, ushort prev)
                {
                    return i > 0 && meshBuffer.ShouldReuse(pos, tan, prev, !startOrEnd);
                }

                var dissolveCeilingTotal = ShouldReuse(ceilingTotalLoc, tangent, lastCross.CeilingTotal);
                currCross.CeilingTotal = dissolveCeilingTotal ? lastCross.CeilingTotal : meshBuffer.AddVertex(ceilingTotalLoc);

                var normal = Vector3.Cross(up, tangent);
                normal.Normalize();

                var perturbLow = Vector3.Zero;
                if (startOrEnd)
                {
                    var n = i == 0 ? -tangent : tangent;
                    perturbLow = (float)Math.Cos(args.RelaxAngleRadians) * n * Math.Abs(halfHeight);
                }

                for (var j = -1; j <= 1; j += 2)
                {
                    ref var currSide = ref j == -1 ? ref currCross.Negative : ref currCross.Positive;
                    ref var lastSide = ref j == -1 ? ref lastCross.Negative : ref lastCross.Positive;

                    var origin = loc + args.Width / 2 * j * normal;
                    var dissolveOrigin = ShouldReuse(origin, tangent, lastSide.Level);
                    currSide.Level = dissolveOrigin ? lastSide.Level : meshBuffer.AddVertex(origin);
                    var rot = Matrix.CreateFromAxisAngle(tangent, -j * Math.Sign(halfHeight) * args.RelaxAngleRadians);
                    var pnorm = Vector3.TransformNormal(up, rot);

                    var cotan = Vector3.Cross(pnorm, tangent);
                    cotan.Normalize();
                    cotan *= -Math.Sign(Vector3.Dot(cotan, up));
                    var p2Loc = origin + halfHeight * cotan + perturbLow;
                    var dissolvedOuterEdge = ShouldReuse(p2Loc, tangent, lastSide.OuterEdge);
                    currSide.OuterEdge = dissolvedOuterEdge ? lastSide.OuterEdge : meshBuffer.AddVertex(p2Loc);

                    var ceilingEdgeLoc = origin - up * (args.Height - dropTop);
                    var dissolvedCeilingEdge = ShouldReuse(ceilingEdgeLoc, tangent, lastSide.Ceiling);
                    currSide.Ceiling = dissolvedCeilingEdge ? lastSide.Ceiling : meshBuffer.AddVertex(ceilingEdgeLoc);

                    if (i <= 0)
                        continue;
                    var flip = j == 1;
                    // side faces, lower
                    meshBuffer.AddTriangle(currSide.Level, lastSide.OuterEdge, lastSide.Level, flip);
                    meshBuffer.AddTriangle(currSide.Level, currSide.OuterEdge, lastSide.OuterEdge, flip);

                    // side faces, upper:
                    meshBuffer.AddTriangle(lastSide.OuterEdge, currSide.Ceiling, lastSide.Ceiling, flip);
                    meshBuffer.AddTriangle(lastSide.OuterEdge, currSide.OuterEdge, currSide.Ceiling, flip);
                }

                if (i > 0)
                {
                    // top
                    meshBuffer.AddTriangle(currCross.Negative.Level, lastCross.Negative.Level, lastCross.Positive.Level);
                    meshBuffer.AddTriangle(currCross.Negative.Level, lastCross.Positive.Level, currCross.Positive.Level);

                    // bottom
                    meshBuffer.AddTriangle(currCross.Negative.Ceiling, lastCross.CeilingTotal, lastCross.Negative.Ceiling);
                    meshBuffer.AddTriangle(currCross.Negative.Ceiling, currCross.CeilingTotal, lastCross.CeilingTotal);

                    meshBuffer.AddTriangle(currCross.Positive.Ceiling, lastCross.Positive.Ceiling, lastCross.CeilingTotal);
                    meshBuffer.AddTriangle(currCross.Positive.Ceiling, lastCross.CeilingTotal, currCross.CeilingTotal);
                }

                // end caps
                if (startOrEnd)
                {
                    var avgId = meshBuffer.AddVertex(currCross.Center(meshBuffer));

                    var flip = i == args.Segments;
                    meshBuffer.AddTriangle(currCross.Negative.Level, currCross.Negative.OuterEdge, avgId, flip);
                    meshBuffer.AddTriangle(currCross.Negative.OuterEdge, currCross.Negative.Ceiling, avgId, flip);
                    meshBuffer.AddTriangle(currCross.Negative.Ceiling, currCross.CeilingTotal, avgId, flip);
                    meshBuffer.AddTriangle(currCross.CeilingTotal, currCross.Positive.Ceiling, avgId, flip);
                    meshBuffer.AddTriangle(currCross.Positive.Ceiling, currCross.Positive.OuterEdge, avgId, flip);
                    meshBuffer.AddTriangle(currCross.Positive.OuterEdge, currCross.Positive.Level, avgId, flip);
                    meshBuffer.AddTriangle(currCross.Positive.Level, currCross.Negative.Level, avgId, flip);
                }

                MyUtils.Swap(ref lastCross, ref currCross);
            }

            return new MeshGradeShape(pivot, meshBuffer, MyTuple.Create(edge, args));
        }

        public static MeshGradeShape CreateGradeShape(EdgeBlit edge, GradingParams args)
        {
            using (PoolManager.Get(out MeshBuffer meshBuffer))
                return CreateGradeShapeInternal(edge, args, meshBuffer);
        }

        private static MeshGradeShape CreateGradeShapeInternal(EdgeBlit edge, GradingParams args, MeshBuffer meshBuffer)
        {
            meshBuffer.Clear();
            var curve = edge.Curve.Convert();
            var pivot = curve.Sample(0.5f);
            var lastCross = new StackArray<ushort>();
            var currCross = new StackArray<ushort>();

            for (var i = 0; i <= args.Segments; i++)
            {
                var t = i / (float)args.Segments;
                var loc = (Vector3)(curve.Sample(t) - pivot);
                var tangent = (Vector3)curve.SampleDerivative(t);
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
                var startOrEnd = i == 0 || i == args.Segments;
                if (startOrEnd)
                {
                    var n = i == 0 ? -tangent : tangent;
                    perturbLow = (float)Math.Cos(args.RelaxAngleRadians) * n * Math.Abs(args.Height);
                }

                bool ShouldReuse(Vector3 pos, Vector3 tan, ushort prev)
                {
                    return i > 0 && meshBuffer.ShouldReuse(pos, tan, prev, !startOrEnd);
                }

                for (var j = -1; j <= 1; j += 2)
                {
                    var offset = j + 1;

                    var origin = loc + args.Width / 2 * j * normal;
                    var reuseOrigin = ShouldReuse(origin, tangent, lastCross[offset]);
                    var originId = reuseOrigin ? lastCross[offset] : meshBuffer.AddVertex(origin);
                    var rot = Matrix.CreateFromAxisAngle(tangent, -j * Math.Sign(args.Height) * args.RelaxAngleRadians);
                    var pnorm = Vector3.TransformNormal(up, rot);

                    var cotan = Vector3.Cross(pnorm, tangent);
                    cotan.Normalize();
                    cotan *= -Math.Sign(Vector3.Dot(cotan, up));
                    var p2 = origin + args.Height * cotan + perturbLow;
                    var reuseP2 = ShouldReuse(p2, tangent, lastCross[offset + 1]);
                    var p2Id = reuseP2 ? lastCross[offset + 1] : meshBuffer.AddVertex(p2);

                    if (i > 0)
                    {
                        var flip = j == 1;
                        meshBuffer.AddTriangle(originId, lastCross[offset], lastCross[offset + 1], flip);
                        meshBuffer.AddTriangle(originId, lastCross[offset + 1], p2Id, flip);
                    }

                    currCross[offset] = originId;
                    currCross[offset + 1] = p2Id;
                }

                if (i > 0)
                {
                    meshBuffer.AddTriangle(currCross[0], lastCross[2], lastCross[0]);
                    meshBuffer.AddTriangle(currCross[0], currCross[2], lastCross[2]);

                    meshBuffer.AddTriangle(currCross[1], lastCross[1], lastCross[3]);
                    meshBuffer.AddTriangle(currCross[1], lastCross[3], currCross[3]);
                }

                if (startOrEnd)
                {
                    var flip = i == args.Segments;
                    meshBuffer.AddTriangle(currCross[0], currCross[2], currCross[3], flip);
                    meshBuffer.AddTriangle(currCross[0], currCross[3], currCross[1], flip);
                }

                MyUtils.Swap(ref lastCross, ref currCross);
            }

            return new MeshGradeShape(pivot, meshBuffer, MyTuple.Create(edge, args));
        }

        private static float SafeSign(float f) => f < 0 ? -1 : 1;

        public float GetDensity(in Vector3D worldCoord)
        {
            if (WorldBox.Inflate(1).Contains(worldCoord) == ContainmentType.Disjoint) return 0;
            var signedDistance = SignedDistance(in worldCoord, out _, out _, out _, out _);

            signedDistance = MathHelper.Clamp(signedDistance, -1, 1);
            var rawDensity = (1 - signedDistance) / 2;
            return rawDensity > 0.01f ? rawDensity : 0;
        }

        private float SignedDistance(in Vector3D worldCoord, out Vector3D nearestPos, out Vector3 pseudonormal, out ushort t1, out ushort t2)
        {
            var localPos = (Vector3)(worldCoord - Offset);
            UnsignedDist(localPos, out var nearest);
            ref var tri = ref _tri[nearest.TriangleIndex];
            t1 = t2 = nearest.TriangleIndex;
            switch (nearest.Entity)
            {
                case NearestEntity.V0:
                    pseudonormal = _vertexNorm[tri.V0];
                    break;
                case NearestEntity.V1:
                    pseudonormal = _vertexNorm[tri.V1];
                    break;
                case NearestEntity.V2:
                    pseudonormal = _vertexNorm[tri.V2];
                    break;
                case NearestEntity.E01:
                    pseudonormal = tri.Plane.Normal + _tri[tri.F01].Plane.Normal;
                    t2 = tri.F01;
                    break;
                case NearestEntity.E02:
                    pseudonormal = tri.Plane.Normal + _tri[tri.F20].Plane.Normal;
                    t2 = tri.F20;
                    break;
                case NearestEntity.E12:
                    pseudonormal = tri.Plane.Normal + _tri[tri.F12].Plane.Normal;
                    t2 = tri.F12;
                    break;
                case NearestEntity.F:
                    pseudonormal = tri.Plane.Normal;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var u = localPos - nearest.NearestPos;
            nearestPos = Offset + nearest.NearestPos;
            return (float)Math.Sqrt(nearest.DistanceSq) * SafeSign(u.Dot(pseudonormal));
        }

        public bool IsInside(Vector3D pt) => GetDensity(pt) > 0;

        public void DrawShape(MyStringId material)
        {
            if (MyCameraComponent.ActiveCamera != null && MyCameraComponent.ActiveCamera.GetCameraFrustum().Contains(WorldBox) == ContainmentType.Disjoint)
                return;
            for (ushort i = 0; i < _tri.Length; i++)
            {
                ref var t = ref _tri[i];
                var p0 = Offset + _vertexPos[t.V0];
                var p1 = Offset + _vertexPos[t.V1];
                var p2 = Offset + _vertexPos[t.V2];
                var n = t.Plane.Normal;
                var center = (p0 + p1 + p2) / 3;
                MyTransparentGeometry.AddTriangleBillboard(p0, p1, p2, n, n, n, Vector2.Zero, new Vector2(1, 0), new Vector2(0, 1), material, 0, center);
            }
        }

        public void DrawQuery(Vector3D query)
        {
            if (MyCameraComponent.ActiveCamera != null && MyCameraComponent.ActiveCamera.GetCameraFrustum().Contains(WorldBox) == ContainmentType.Disjoint)
                return;
            var msg = MyRenderProxy.DebugDrawLine3DOpenBatch(true);
            var signedDist = SignedDistance(query, out var nearestPos, out var nearestNorm, out var t1, out var t2);
            foreach (var pos in AxisOptions)
                msg.AddLine(nearestPos + pos * 0.25f, Color.Aqua, nearestPos - pos * 0.25f, Color.Aqua);
            nearestNorm.Normalize();
            msg.AddLine(nearestPos, Color.Azure, nearestPos + nearestNorm, Color.Azure);

            MyRenderProxy.DebugDrawText3D(WorldBox.Center, $"{signedDist:F2}\n{(byte)(GetDensity(query) * byte.MaxValue)}", signedDist > 0 ? Color.White : Color.Red, 0.75f, false);

            for (ushort i = 0; i < _tri.Length; i++)
            {
                ref var t = ref _tri[i];
                var p0 = Offset + _vertexPos[t.V0];
                var p1 = Offset + _vertexPos[t.V1];
                var p2 = Offset + _vertexPos[t.V2];
                var n = t.Plane.Normal;
                var center = (p0 + p1 + p2) / 3;
                var color = Color.White;
                msg.AddLine(center, Color.Red, center + n, Color.Blue);
                msg.AddLine(p0, color, p1, color);
                msg.AddLine(p1, color, p2, color);
                msg.AddLine(p2, color, p0, color);
                if (i == t1 || i == t2)
                {
                    color = Color.Gold;
                    p0 = center + (p0 - center) * 0.75f;
                    p1 = center + (p1 - center) * 0.75f;
                    p2 = center + (p2 - center) * 0.75f;
                    msg.AddLine(p0, color, p1, color);
                    msg.AddLine(p1, color, p2, color);
                    msg.AddLine(p2, color, p0, color);
                }
            }

            MyRenderProxy.DebugDrawLine3DSubmitBatch(msg);
        }

        private static readonly ListReader<Vector3> AxisOptions = new List<Vector3>
        {
            Vector3.Right,
            Vector3.Up,
            Vector3.Forward,
        };

        public void CollectCacheKeys(Action<BoundingBoxD, object> consumer) => consumer(WorldBox, _cacheKey);

        private enum NearestEntity
        {
            V0,
            V1,
            V2,
            E01,
            E02,
            E12,
            F
        }

        private struct NearestEntityData
        {
            public ushort TriangleIndex;
            public float DistanceSq;
            public NearestEntity Entity;
            public Vector3 NearestPos;
        }

        private const float UseNormalScoreEpsilon = 0f;//.05f * .05f;

        private void UnsignedDist(Vector3 pt, out NearestEntityData nearest)
        {
            nearest = default;
            nearest.DistanceSq = float.PositiveInfinity;
            var nearestDotNorm = 0f;
            for (ushort i = 0; i < _tri.Length; i++)
            {
                ref var t = ref _tri[i];

                var planeDist = t.Plane.DotCoordinate(pt);
                if (planeDist * planeDist > nearest.DistanceSq + UseNormalScoreEpsilon) continue;
                UnsignedTriangleDist(ref t, pt, out var test);
                if (test.DistanceSq > nearest.DistanceSq + UseNormalScoreEpsilon) continue;
                nearest = test;
                nearest.TriangleIndex = i;
            }
        }

        private void UnsignedTriangleDist(ref Tri tri, Vector3 pt, out NearestEntityData nearest)
        {
            nearest = default;
            var v0 = _vertexPos[tri.V0];
            var diff = v0 - pt;
            var v1 = _vertexPos[tri.V1];
            var v2 = _vertexPos[tri.V2];
            var edge0 = v1 - v0;
            var edge1 = v2 - v0;
            var a00 = edge0.LengthSquared();
            var a01 = edge0.Dot(edge1);
            var a11 = edge1.LengthSquared();
            var b0 = diff.Dot(edge0);
            var b1 = diff.Dot(edge1);
            var c = diff.LengthSquared();
            var det = Math.Abs(a00 * a11 - a01 * a01);
            var s = a01 * b1 - a11 * b0;
            var t = a01 * b0 - a00 * b1;
            float d2;

            if (s + t <= det)
            {
                if (s < 0)
                {
                    if (t < 0) // region 4
                    {
                        if (b0 < 0)
                        {
                            t = 0;
                            if (-b0 >= a00)
                            {
                                nearest.Entity = NearestEntity.V1;
                                s = 1;
                                d2 = a00 + 2 * b0 + c;
                            }
                            else
                            {
                                nearest.Entity = NearestEntity.E01;
                                s = -b0 / a00;
                                d2 = b0 * s + c;
                            }
                        }
                        else
                        {
                            s = 0;
                            if (b1 >= 0)
                            {
                                nearest.Entity = NearestEntity.V0;
                                t = 0;
                                d2 = c;
                            }
                            else if (-b1 >= a11)
                            {
                                nearest.Entity = NearestEntity.V2;
                                t = 1;
                                d2 = a11 + 2 * b1 + c;
                            }
                            else
                            {
                                nearest.Entity = NearestEntity.E02;
                                t = -b1 / a11;
                                d2 = b1 * t + c;
                            }
                        }
                    }
                    else // region 3
                    {
                        s = 0;
                        if (b1 >= 0)
                        {
                            nearest.Entity = NearestEntity.V0;
                            t = 0;
                            d2 = c;
                        }
                        else if (-b1 >= a11)
                        {
                            nearest.Entity = NearestEntity.V2;
                            t = 1;
                            d2 = a11 + 2 * b1 + c;
                        }
                        else
                        {
                            nearest.Entity = NearestEntity.E02;
                            t = -b1 / a11;
                            d2 = b1 * t + c;
                        }
                    }
                }
                else if (t < 0) // region 5
                {
                    t = 0;
                    if (b0 >= 0)
                    {
                        nearest.Entity = NearestEntity.V0;
                        s = 0;
                        d2 = c;
                    }
                    else if (-b0 >= a00)
                    {
                        nearest.Entity = NearestEntity.V1;
                        s = 1;
                        d2 = a00 + 2 * b0 + c;
                    }
                    else
                    {
                        nearest.Entity = NearestEntity.E01;
                        s = -b0 / a00;
                        d2 = b0 * s + c;
                    }
                }
                else // region 0 
                {
                    nearest.Entity = NearestEntity.F;
                    // minimum at interior point
                    var invDet = 1 / det;
                    s *= invDet;
                    t *= invDet;
                    d2 = s * (a00 * s + a01 * t + 2 * b0) +
                         t * (a01 * s + a11 * t + 2 * b1) + c;
                }
            }
            else
            {
                float tmp0, tmp1, numer, denom;

                if (s < 0) // region 2
                {
                    tmp0 = a01 + b0;
                    tmp1 = a11 + b1;
                    if (tmp1 > tmp0)
                    {
                        numer = tmp1 - tmp0;
                        denom = a00 - 2 * a01 + a11;
                        if (numer >= denom)
                        {
                            nearest.Entity = NearestEntity.V1;
                            s = 1;
                            t = 0;
                            d2 = a00 + 2 * b0 + c;
                        }
                        else
                        {
                            nearest.Entity = NearestEntity.E12;
                            s = numer / denom;
                            t = 1 - s;
                            d2 = s * (a00 * s + a01 * t + 2 * b0) +
                                 t * (a01 * s + a11 * t + 2 * b1) + c;
                        }
                    }
                    else
                    {
                        s = 0;
                        if (tmp1 <= 0)
                        {
                            nearest.Entity = NearestEntity.V2;
                            t = 1;
                            d2 = a11 + 2 * b1 + c;
                        }
                        else if (b1 >= 0)
                        {
                            nearest.Entity = NearestEntity.V0;
                            t = 0;
                            d2 = c;
                        }
                        else
                        {
                            nearest.Entity = NearestEntity.E02;
                            t = -b1 / a11;
                            d2 = b1 * t + c;
                        }
                    }
                }
                else if (t < 0) // region 6
                {
                    tmp0 = a01 + b1;
                    tmp1 = a00 + b0;
                    if (tmp1 > tmp0)
                    {
                        numer = tmp1 - tmp0;
                        denom = a00 - 2 * a01 + a11;
                        if (numer >= denom)
                        {
                            nearest.Entity = NearestEntity.V2;
                            t = 1;
                            s = 0;
                            d2 = a11 + 2 * b1 + c;
                        }
                        else
                        {
                            nearest.Entity = NearestEntity.E12;
                            t = numer / denom;
                            s = 1 - t;
                            d2 = s * (a00 * s + a01 * t + 2 * b0) +
                                 t * (a01 * s + a11 * t + 2 * b1) + c;
                        }
                    }
                    else
                    {
                        t = 0;
                        if (tmp1 <= 0)
                        {
                            nearest.Entity = NearestEntity.V1;
                            s = 1;
                            d2 = a00 + 2 * b0 + c;
                        }
                        else if (b0 >= 0)
                        {
                            nearest.Entity = NearestEntity.V0;
                            s = 0;
                            d2 = c;
                        }
                        else
                        {
                            nearest.Entity = NearestEntity.E01;
                            s = -b0 / a00;
                            d2 = b0 * s + c;
                        }
                    }
                }
                else // region 1
                {
                    numer = a11 + b1 - a01 - b0;
                    if (numer <= 0)
                    {
                        nearest.Entity = NearestEntity.V2;
                        s = 0;
                        t = 1;
                        d2 = a11 + 2 * b1 + c;
                    }
                    else
                    {
                        denom = a00 - 2 * a01 + a11;
                        if (numer >= denom)
                        {
                            nearest.Entity = NearestEntity.V1;
                            s = 1;
                            t = 0;
                            d2 = a00 + 2 * b0 + c;
                        }
                        else
                        {
                            nearest.Entity = NearestEntity.E12;
                            s = numer / denom;
                            t = 1 - s;
                            d2 = s * (a00 * s + a01 * t + 2 * b0) +
                                 t * (a01 * s + a11 * t + 2 * b1) + c;
                        }
                    }
                }
            }

            nearest.NearestPos = v0 + s * edge0 + t * edge1;
            // Account for numerical round-off error.
            nearest.DistanceSq = d2 < 0 ? 0 : d2;
        }
    }
}