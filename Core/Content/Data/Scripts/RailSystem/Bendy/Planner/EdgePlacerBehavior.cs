using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Bendy.Shape;
using Equinox76561198048419394.RailSystem.Util;
using Equinox76561198048419394.RailSystem.Util.Curve;
using Medieval.Constants;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components.Entity.Camera;
using VRage.Entities.Gravity;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.GUI.Crosshair;
using VRage.Input.Devices.Keyboard;
using VRage.Library.Collections;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Equipment;
using VRage.Session;
using VRage.Systems;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy.Planner
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_EdgePlacerBehaviorDefinition))]
    public class EdgePlacerBehavior : MyToolBehaviorBase
    {
        protected override bool ValidateTarget()
        {
            return true;
        }

        private readonly List<EdgePlacerSystem.AnnotatedNode> _vertices = new List<EdgePlacerSystem.AnnotatedNode>();
        private readonly HashSet<long> _remove = new HashSet<long>();
        private long _lastRemove;


        private EdgePlacerSystem.AnnotatedNode CreateVertex(Vector3D worldPos)
        {
            var node = Graph.GetNode(worldPos);
            Vector3D up;
            if (node != null)
            {
                up = node.Up;
                worldPos = node.Position;
            }
            else
            {
                up = Vector3D.Normalize(-MyGravityProviderSystem.CalculateNaturalGravityInPoint(worldPos));
                if (!up.IsValid() || up.LengthSquared() < 1e-3f)
                    up = Vector3D.Up;
            }

            return new EdgePlacerSystem.AnnotatedNode { Position = worldPos, Up = (Vector3)up, Existing = node, Tangent = node?.Tangent ?? Vector3.Zero };
        }

        private const float _edgeWidth = 0.05f;
        private const float _nodeWidth = 0.01f;
        private static readonly Vector4 _edgeColor = new Vector4(0, 0, 1, 0.1f);
        private static readonly Vector4 _edgeColorBad = new Vector4(1, 0, 0, 0.1f);
        private static readonly Vector4 _nodeColor = new Vector4(0, 0, 1, 0.1f);
        private static readonly MyStringId _squareMaterial = MyStringId.GetOrCompute("Square");

        private const float _nodeMarkerSize = 1;
        private const float _edgeMarkerVertOffset = 0.325f;

        private BendyLayer Graph { get; set; }
        private string Layer => Definition.Layer;
        private new EdgePlacerBehaviorDefinition Definition { get; set; }
        private BendyComponentDefinition PlacedDefinition { get; set; }

        public override void Init(MyEntity holder, MyHandItem item, MyHandItemBehaviorDefinition definition)
        {
            base.Init(holder, item, definition);
            Definition = (EdgePlacerBehaviorDefinition)definition;
            PlacedDefinition = EdgePlacerSystem.DefinitionFor(Definition.Placed);
        }

        public override void Activate()
        {
            base.Activate();
            Graph = MySession.Static.Components.Get<BendyController>().GetOrCreateLayer(Layer);
            Graph.NodeCreated += NodesChanged;
            Graph.NodeMoved += NodesChanged;
            _vertices.Clear();
            if (IsLocallyControlled)
                MySession.Static.Components.Get<MyUpdateComponent>().AddFixedUpdate(Render);
        }

        private void NodesChanged(Node obj)
        {
            EdgePlacerSystem.AnnotateNodes(Graph, _vertices);
        }

        public override void Deactivate()
        {
            base.Deactivate();
            _vertices.Clear();
            Graph.NodeCreated -= NodesChanged;
            Graph.NodeMoved -= NodesChanged;
            Graph = null;
            MySession.Static.Components.Get<MyUpdateComponent>().RemoveFixedUpdate(Render);
            _hintInfo?.Hide();
            _hintInfo = null;
        }


        #region Rendering

        private MatrixD ComputeVertexMatrix(EdgePlacerSystem.AnnotatedNode vert, int index)
        {
            if (vert.Existing != null)
                return vert.Existing.Matrix;

            var prevPos = (index - 1) >= 0 ? (EdgePlacerSystem.AnnotatedNode?)_vertices[index - 1] : null;
            var nextPos = (index + 1) < _vertices.Count ? (EdgePlacerSystem.AnnotatedNode?)_vertices[index + 1] : null;

            var tan = Vector3D.Zero;
            if (prevPos.HasValue)
            {
                var t = (vert.Position - prevPos.Value.Position).SafeNormalized();
                tan += tan.Dot(t) < 0 ? -t : t;
            }

            if (nextPos.HasValue)
            {
                var t2 = (vert.Position - nextPos.Value.Position).SafeNormalized();
                tan += tan.Dot(t2) < 0 ? -t2 : t2;
            }

            if (prevPos.HasValue != nextPos.HasValue)
            {
                // try Quadratic bez with control point equidistance from both nodes.
                if (prevPos?.Existing != null)
                {
                    var pp = prevPos.Value.Existing;
                    tan = CurveExtensions.ExpandToCubic(pp.Position, pp.Position + pp.Tangent, vert.Position,
                        RailConstants.LongBezControlLimit) - vert.Position;
                }
                else if (nextPos?.Existing != null)
                {
                    var pp = nextPos.Value.Existing;
                    tan = CurveExtensions.ExpandToCubic(pp.Position, pp.Position + pp.Tangent, vert.Position,
                        RailConstants.LongBezControlLimit) - vert.Position;
                }
            }

            if (!tan.IsValid() || tan.LengthSquared() < 1e-3f)
                tan = Vector3D.Cross(vert.Up, vert.Up.Shifted());
            tan.SafeNormalized();
            return MatrixD.CreateWorld(vert.Position, tan, vert.Up);
        }

        private void Render()
        {
            // Fix because OnTargetEntityChanged is only called when the actual entity changed.
            SetTarget();

            var cam = MyCameraComponent.ActiveCamera;
            if (Graph == null || cam == null) return;

            {
                // draw node markers
                var proj = cam.GetProjectionSetup();
                proj.FarPlane = 100;
                var frust = new BoundingFrustumD(cam.GetViewMatrix() * proj.ProjectionMatrix);
                Graph.Nodes.OverlapAllFrustum(ref frust, (Node node, bool intersects) =>
                {
                    var color = _nodeColor;
                    var p1 = node.Position;
                    var p2 = node.Position + _nodeMarkerSize * node.Up;
                    MySimpleObjectDraw.DrawLine(p1, p2, _squareMaterial, ref color, _nodeWidth);
                });
            }

            {
                foreach (var k in _vertices)
                    if (Vector3D.DistanceSquared(cam.GetPosition(), k.Position) < 100 * 100)
                    {
                        var color = _nodeColor;
                        var p1 = k.Position;
                        var p2 = k.Position + _nodeMarkerSize * k.Up;
                        MySimpleObjectDraw.DrawLine(p1, p2, _squareMaterial, ref color, _nodeWidth);
                    }
            }

            foreach (var remove in _remove)
                if (Scene.TryGetEntity(remove, out var removeEnt) && removeEnt.Components.TryGet(out BendyComponent bendy))
                    foreach (var edge in bendy.Edges)
                        edge.Draw(0, 1, Color.Red, 1);

            if (Modified
                && _remove.Count > 0
                && TryGetBendyTarget(out _, out var removeTarget)
                && Scene.TryGetEntity(_lastRemove, out var lastEntity)
                && lastEntity.Components.TryGet(out BendyComponent lastBendy)
                && lastBendy.Graph == Graph)
            {
                using (PoolManager.Get(out List<Edge> edges))
                using (PoolManager.Get(out HashSet<BendyComponent> entities))
                {
                    if (Graph.TryFindPath(
                            lastBendy.Nodes, removeTarget.Nodes,
                            null, edges,
                            nodeLimit: RailConstants.MaxNodesPlaced))
                    {
                        if (_remove.Count + entities.Count < RailConstants.MaxNodesPlaced)
                            entities.Add(removeTarget);
                        foreach (var edge in edges)
                        {
                            var ent = edge.Owner?.Entity;
                            if (ent != null && ent.Components.TryGet(out BendyComponent bc) && _remove.Count + entities.Count < RailConstants.MaxNodesPlaced)
                                entities.Add(bc);
                        }

                        foreach (var ent in entities)
                        {
                            if (!ValidateDeconstruct(ent.Entity, out _, true)) continue;
                            foreach (var edge in ent.Edges)
                                edge.Draw(0, 1, Color.Red, 1);
                        }
                    }
                }
            }

            if (Holder == null) return;

            {
                using (new TemporaryVertex(this))
                {
                    for (var i = 1; i < _vertices.Count; i++)
                    {
                        var nextVert = _vertices[i];
                        var currentVert = _vertices[i - 1];

                        var nextMatrix = ComputeVertexMatrix(nextVert, i);
                        var currentMatrix = ComputeVertexMatrix(currentVert, i - 1);

                        var color = PlacedDefinition == null || EdgePlacerSystem.VerifyEdge(PlacedDefinition, currentVert, nextVert, null)
                            ? _edgeColor
                            : _edgeColorBad;
                        const float vertShift = .1f;
                        if (Vector3D.DistanceSquared(currentMatrix.Translation, nextMatrix.Translation) > 30 * 30)
                        {
                            var curve = PrepareSphericalBez(currentMatrix, nextMatrix);
                            curve.Draw(color, upZero: currentVert.Up * vertShift, upOne: nextVert.Up * vertShift);
                        }
                        else
                        {
                            if (Math.Min(Vector3D.DistanceSquared(cam.GetPosition(), nextVert.Position),
                                    Vector3D.DistanceSquared(cam.GetPosition(), currentVert.Position)) > 100 * 100)
                                continue;
                            var curve = PrepareNormalBez(currentMatrix, nextMatrix);
                            curve.Draw(color, upZero: currentVert.Up * vertShift, upOne: nextVert.Up * vertShift);
                        }
                    }
                }
            }
        }

        private static CubicSphericalCurve PrepareSphericalBez(MatrixD m1, MatrixD m2)
        {
            CurveExtensions.AlignFwd(ref m1, ref m2);
            return new CubicSphericalCurve(
                MyGamePruningStructureSandbox.GetClosestPlanet(m1.Translation)?.PositionComp.WorldVolume.Center ??
                Vector3D.Zero, m1, m2);
        }

        private static CubicCurve PrepareNormalBez(MatrixD m1, MatrixD m2)
        {
            CurveExtensions.AlignFwd(ref m1, ref m2);
            return new CubicCurve(m1, m2);
        }

        #endregion

        private readonly List<string> _tmpMessages = new List<string>();

        private const float ClickDistSq = 25 * 25;

        private struct TemporaryVertex : IDisposable
        {
            private readonly EdgePlacerBehavior _behavior;

            public TemporaryVertex(EdgePlacerBehavior be)
            {
                _behavior = be;
                var target = _behavior.Target;
                var myPosition = _behavior.Holder.GetPosition();
                _behavior._vertices.Add(_behavior.CreateVertex(target.Entity != null && Vector3D.DistanceSquared(myPosition, target.Position) < ClickDistSq
                    ? target.Position
                    : myPosition));
                EdgePlacerSystem.AnnotateNodes(_behavior.Graph, _behavior._vertices);
            }

            public void Dispose()
            {
                _behavior._vertices.RemoveAt(_behavior._vertices.Count - 1);
            }
        }

        protected override bool Start(MyHandItemActionEnum action)
        {
            if (!IsLocallyControlled)
                return false;
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
                return false;
            if (Target.Entity == null || Vector3D.DistanceSquared(Target.Position, Holder.GetPosition()) > ClickDistSq)
            {
                player.ShowNotification($"Too far away from the chosen point.  Click closer to yourself.", 2000, null, new Vector4(1, 0, 0, 1));
                return false;
            }

            switch (action)
            {
                case MyHandItemActionEnum.Tertiary:
                    return _vertices.Count > 0;
                case MyHandItemActionEnum.Secondary:
                    if (ValidateDeconstruct(Target.Entity, out var err, true))
                        return true;
                    if (!string.IsNullOrEmpty(err))
                        player.ShowNotification(err, 2000, null,
                            new Vector4(1, 0, 0, 1));
                    if (_vertices.Count > 0 && Vector3D.DistanceSquared(_vertices[_vertices.Count - 1].Position, Target.Position) < 1)
                        _vertices.RemoveAt(_vertices.Count - 1);
                    return false;
                case MyHandItemActionEnum.Primary:
                    if (_vertices.Count == 1 && MyAPIGateway.Input.IsKeyDown(MyKeys.Shift))
                    {
                        var originalStart = _vertices[0];
                        var lastVertex = CreateVertex(Target.Position);
                        _vertices.Add(lastVertex);
                        EdgePlacerSystem.AnnotateNodes(Graph, _vertices);
                        var curveData = new LongPlanData(this, _vertices[0], _vertices[1]);
                        var jointData = EdgePlacerSystem.ComputeEdgeParameters(_vertices[0], _vertices[_vertices.Count - 1]);
                        {
                            _tmpMessages.Clear();
                            if (jointData.BendRadians.HasValue)
                            {
                                var angle = jointData.BendRadians.Value / curveData.Count;
                                if (angle > RailConstants.LongToleranceFactor * PlacedDefinition.MaxAngleRadians)
                                    _tmpMessages.Add(
                                        $"Too curvy {angle * 180 / Math.PI:F0}º >= {RailConstants.LongToleranceFactor * PlacedDefinition.MaxAngleDegrees:F0}º");
                            }

                            // ReSharper disable once InvertIf
                            if (jointData.Grade.HasValue)
                            {
                                var grade = jointData.Grade.Value;
                                // ReSharper disable once InvertIf
                                if (Math.Abs(grade) > RailConstants.LongToleranceFactor * PlacedDefinition.MaxGradeRatio)
                                {
                                    _tmpMessages.Add(
                                        $"Too steep {grade * 100:F0}% {(grade < 0 ? "<= -" : ">= ")}{RailConstants.LongToleranceFactor * PlacedDefinition.MaxGradeRatio * 100:F0}%");
                                }
                            }

                            if (_tmpMessages.Count > 0)
                            {
                                player.ShowNotification(string.Join("\n", _tmpMessages), 2000, null,
                                    new Vector4(1, 0, 0, 1));
                                _tmpMessages.Clear();
                                _vertices.Clear();
                                _vertices.Add(originalStart);
                                return false;
                            }
                        }
                        ComputeLong(_vertices[0], _vertices[1], curveData);
                        _vertices.RemoveAt(_vertices.Count - 1);
                        _tmpMessages.Clear();
                        player.ShowNotification($"Dividing into {curveData.Count} segments");
                        if (!ValidatePlace(_tmpMessages, true))
                        {
                            player.ShowNotification(string.Join("\n", _tmpMessages), 2000, null,
                                new Vector4(1, 0, 0, 1));
                            _vertices.Clear();
                            _vertices.Add(originalStart);
                            return false;
                        }

                        return false;
                    }

                    _tmpMessages.Clear();
                    if (ValidatePlace(_tmpMessages, true))
                        return true;
                    if (_tmpMessages.Count > 0)
                        player.ShowNotification(string.Join("\n", _tmpMessages), 2000, null,
                            new Vector4(1, 0, 0, 1));
                    _tmpMessages.Clear();
                    return false;
                case MyHandItemActionEnum.None:
                default:
                    return false;
            }
        }

        private bool TargetIsStatic
        {
            get
            {
                var e = Target.Entity;
                while (e != null)
                {
                    if (e.Physics != null)
                        return e.Physics.IsStatic;
                    e = e.Parent;
                }

                return true;
            }
        }

        private bool IsLocallyControlled => MySession.Static.PlayerEntity == Holder;

        private IMyHudNotification _hintInfo;

        private void Cleanup()
        {
            // nasty hack here
            while (_vertices.Count > 0)
                if (_vertices[0].Position.Equals(Vector3D.Zero, 1e-6f))
                    _vertices.RemoveAt(0);
                else
                    break;
        }

        protected override void Hit()
        {
            if (!IsLocallyControlled)
                return;
            try
            {
                Cleanup();
                switch (ActiveAction)
                {
                    case MyHandItemActionEnum.Tertiary:
                        HandleInspect();
                        return;
                    case MyHandItemActionEnum.Secondary:
                        HandleRemove();
                        return;
                    case MyHandItemActionEnum.Primary:
                    {
                        _remove.Clear();
                        _vertices.Add(CreateVertex(Target.Position));
                        if (_vertices.Count < 2) return;
                        var pts = _vertices.Select(x => x.Position).ToArray();
                        _vertices.RemoveRange(0, _vertices.Count - 1);
                        EdgePlacerSystem.RaisePlaceEdge(new EdgePlacerSystem.EdgePlacerConfig()
                            {
                                EntityPlacing = Holder.EntityId,
                                Placed = Definition.Placed
                            }, pts
                        );
                        return;
                    }
                    case MyHandItemActionEnum.None:
                    default:
                        return;
                }
            }
            finally
            {
                Cleanup();
            }
        }

        private void HandleInspect()
        {
            _remove.Clear();
            if (_vertices.Count == 0)
                return;
            if (_hintInfo == null)
                _hintInfo = MyAPIGateway.Utilities.CreateNotification("");
            var sb = new StringBuilder();

            if (!TargetIsStatic)
                sb.AppendLine("Can't place node on dynamic physics");
            else
            {
                using (new TemporaryVertex(this))
                    if (_vertices.Count >= 2)
                    {
                        var jointData = EdgePlacerSystem.ComputeEdgeParameters(_vertices[_vertices.Count - 2], _vertices[_vertices.Count - 1]);
                        sb.Append($"Length {jointData.Length:F1} m");
                        if (PlacedDefinition != null && jointData.Length > PlacedDefinition.Distance.Max)
                            sb.AppendLine($" >= {PlacedDefinition.Distance.Max:F1} m");
                        else if (PlacedDefinition != null && jointData.Length < PlacedDefinition.Distance.Min)
                            sb.AppendLine($" <= {PlacedDefinition.Distance.Min:F1} m");
                        else
                            sb.AppendLine();
                        if (jointData.BendRadians.HasValue)
                        {
                            var b = jointData.BendRadians.Value;
                            sb.Append($"Curve {b * 180 / Math.PI:F0}º");
                            if (PlacedDefinition != null && b > PlacedDefinition.MaxAngleRadians)
                                sb.AppendLine($" >= {PlacedDefinition.MaxAngleDegrees}º");
                            else
                                sb.AppendLine();
                        }

                        if (jointData.Grade.HasValue)
                        {
                            var g = jointData.Grade.Value;
                            sb.Append($"Grade {g * 100:F0}%");
                            if (PlacedDefinition != null && Math.Abs(g) > PlacedDefinition.MaxGradeRatio)
                                sb.AppendLine($" {(g < 0 ? "<= -" : ">= ")}{PlacedDefinition.MaxGradeRatio * 100:F0}%");
                            else
                                sb.AppendLine();
                        }
                    }
            }

            _hintInfo.Text = sb.ToString();
            _hintInfo.Show(); // resets alive time + adds to queue if it's not in it
        }

        private bool TryGetBendyTarget(out MyEntity entity, out BendyComponent target)
        {
            entity = Target.Entity?.Components.Get<BendyShapeProxy>()?.Owner ?? Target.Entity;
            target = null;
            return entity != null && entity.Components.TryGet(out target) && target.Graph == Graph;
        }

        private void HandleRemove()
        {
            _vertices.Clear();
            if (!TryGetBendyTarget(out var entity, out var dynCon))
                return;
            if (!Modified)
            {
                EdgePlacerSystem.RaiseRemoveEdges(Holder.EntityId, _remove.ToArray());
                _remove.Clear();
                return;
            }

            if (_remove.Remove(entity.EntityId))
                return;
            var overflowed = false;
            if (_remove.Count > 0
                && Scene.TryGetEntity(_lastRemove, out var lastEntity)
                && lastEntity.Components.TryGet(out BendyComponent lastBendy)
                && lastBendy.Graph == Graph)
            {
                using (PoolManager.Get(out List<Edge> edges))
                {
                    if (Graph.TryFindPath(
                            lastBendy.Nodes, dynCon.Nodes,
                            null, edges,
                            nodeLimit: RailConstants.MaxNodesPlaced))
                    {
                        foreach (var edge in edges)
                        {
                            var ent = edge.Owner?.Entity;
                            if (ent == null || !ValidateDeconstruct(ent, out _, true)) continue;
                            if (_remove.Count >= RailConstants.MaxNodesPlaced)
                            {
                                overflowed = true;
                                break;
                            }

                            _remove.Add(ent.EntityId);
                        }
                    }
                }
            }

            if (_remove.Count >= RailConstants.MaxNodesPlaced)
                overflowed = true;
            else
                _remove.Add(entity.EntityId);
            _lastRemove = entity.EntityId;
            if (!overflowed)
                return;
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            player?.ShowNotification($"Only {RailConstants.MaxNodesPlaced} segments can be removed at once", 2000, null, new Vector4(1, 0, 0, 1));
        }

        #region Long-Place

        private struct LongPlanData
        {
            public readonly MatrixD M1, M2;
            public readonly CubicSphericalCurve Curve;
            public readonly int Count;
            public readonly double Length;

            public LongPlanData(EdgePlacerBehavior epa, EdgePlacerSystem.AnnotatedNode first, EdgePlacerSystem.AnnotatedNode last)
            {
                epa._vertices.Clear();
                epa._vertices.Add(first);
                epa._vertices.Add(last);
                M1 = epa.ComputeVertexMatrix(first, 0);
                M2 = epa.ComputeVertexMatrix(last, 1);
                Curve = PrepareSphericalBez(M1, M2);
                var length = Curve.LengthAuto(epa.PlacedDefinition.Distance.Min / 8);
                Length = length;

                var minCount = (int)Math.Ceiling(length / epa.PlacedDefinition.Distance.Max);
                var maxCount = (int)Math.Floor(length / epa.PlacedDefinition.Distance.Min);
                var idealCount = (int)Math.Round(length / epa.PlacedDefinition.PreferredDistance);
                Count = MathHelper.Clamp(idealCount, minCount, maxCount);
            }
        }

        private void ComputeLong(EdgePlacerSystem.AnnotatedNode first, EdgePlacerSystem.AnnotatedNode last, LongPlanData data)
        {
            var lenPerCount = data.Length / data.Count;
            _vertices.Clear();
            _vertices.Add(first);
            var time = 0f;
            var prev = data.Curve.Sample(0);
            var lengthElapsed = 0d;
            const float timeStep = .001f;
            for (var i = 1; i < data.Count; i++)
            {
                while (lengthElapsed < lenPerCount * i)
                {
                    time += timeStep;
                    var curr = data.Curve.Sample(time);
                    lengthElapsed += Vector3D.Distance(prev, curr);
                    prev = curr;
                }

                _vertices.Add(CreateVertex(prev));
            }

            _vertices.Add(last);
        }

        #endregion

        protected override void OnTargetEntityChanged(MyDetectedEntityProperties myEntityProps)
        {
            base.OnTargetEntityChanged(myEntityProps);
            SetTarget();
        }

        #region Validation

        private bool ValidatePlace(IList<string> errors, bool testPermissions)
        {
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
                return false;

            if (!TargetIsStatic || PlacedDefinition == null)
            {
                errors?.Add("Can't place node on dynamic physics");
                return false;
            }

            var vert = CreateVertex(Target.Position);
            if (testPermissions)
            {
                if (!player.HasPermission(vert.Position, MyPermissionsConstants.Build))
                {
                    errors?.Add("You cannot build here");
                    return false;
                }

                foreach (var t in _vertices)
                    if (!player.HasPermission(t.Position, MyPermissionsConstants.Build))
                    {
                        errors?.Add("You cannot build here");
                        return false;
                    }
            }

            if (_vertices.Count == 0)
                return true;
            using (new TemporaryVertex(this))
            {
                return EdgePlacerSystem.ValidatePath(PlacedDefinition, Graph, _vertices, errors);
            }
        }

        private bool ValidateDeconstruct(MyEntity targetEntity, out string err, bool testPermission)
        {
            err = null;
            var entity = targetEntity?.Components.Get<BendyShapeProxy>()?.Owner ?? targetEntity;
            if (entity == null || entity.Closed)
                return false;
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
                return false;
            if (!testPermission ||
                player.HasPermission(entity.GetPosition(), MyPermissionsConstants.QuickDeconstruct))
                return EdgePlacerSystem.ValidateQuickRemove(player, entity, out err);
            err = "You cannot quick deconstruct here";
            return false;
        }

        #endregion

        #region Hints

        public override IEnumerable<string> GetHintTexts()
        {
            if (ValidatePlace(null, true))
                yield return "Press LMB to place";
            string tmp;
            if (ValidateDeconstruct(Target.Entity, out tmp, true))
                yield return "Press RMB to remove";
        }

        public override IEnumerable<MyCrosshairIconInfo> GetIconsStates()
        {
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (Definition.CrosshairPrefix == null || player == null)
                yield break;

            if (ValidatePlace(null, false))
            {
                var hasPerms = true;
                var vert = CreateVertex(Target.Position);
                if (!player.HasPermission(vert.Position, MyPermissionsConstants.Build))
                    hasPerms = false;

                foreach (var t in _vertices)
                    if (!player.HasPermission(t.Position, MyPermissionsConstants.Build))
                    {
                        hasPerms = false;
                        break;
                    }

                if (hasPerms)
                {
                    if (Definition.CrosshairPlace.HasValue)
                        yield return Definition.CrosshairPlace.Value;
                }
                else if (Definition.CrosshairPlaceNoPermission.HasValue)
                    yield return Definition.CrosshairPlaceNoPermission.Value;
            }

            if (_vertices.Count > 0 && Definition.CrosshairQuestion.HasValue)
                yield return Definition.CrosshairQuestion.Value;
            // ReSharper disable once InvertIf
            if (ValidateDeconstruct(Target.Entity, out _, true))
            {
                var entity = Target.Entity?.Components.Get<BendyShapeProxy>()?.Owner ?? Target.Entity;
                if (entity != null &&
                    player.HasPermission(entity.GetPosition(), MyPermissionsConstants.QuickDeconstruct))
                {
                    if (Definition.CrosshairRemove.HasValue)
                        yield return Definition.CrosshairRemove.Value;
                }
                else if (Definition.CrosshairRemoveNoPermission.HasValue)
                    yield return Definition.CrosshairRemoveNoPermission.Value;
            }
        }

        #endregion
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EdgePlacerBehaviorDefinition))]
    public class EdgePlacerBehaviorDefinition : MyToolBehaviorDefinition
    {
        public string Layer { get; private set; }
        public MyDefinitionId Placed { get; private set; }
        public string CrosshairPrefix { get; private set; }

        public MyCrosshairIconInfo? CrosshairPlace { get; private set; }
        public MyCrosshairIconInfo? CrosshairPlaceNoPermission { get; private set; }
        public MyCrosshairIconInfo? CrosshairQuestion { get; private set; }
        public MyCrosshairIconInfo? CrosshairRemove { get; private set; }
        public MyCrosshairIconInfo? CrosshairRemoveNoPermission { get; private set; }

        private MyCrosshairIconInfo? Create(string name, MyCrosshairIconInfo.IconPosition pos)
        {
            if (string.IsNullOrEmpty(CrosshairPrefix))
                return null;
            return new MyCrosshairIconInfo(MyStringHash.GetOrCompute(CrosshairPrefix + name), pos);
        }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EdgePlacerBehaviorDefinition)builder;
            Layer = ob.Layer;
            if (string.IsNullOrWhiteSpace(Layer))
                MyDefinitionErrors.Add(builder.Package, $"{nameof(EdgePlacerBehaviorDefinition)} {builder.GetId()} has {nameof(Layer)} that is null",
                    LogSeverity.Error);
            Placed = ob.Placed;
            if (Placed.TypeId.IsNull)
                MyDefinitionErrors.Add(builder.Package, $"{nameof(EdgePlacerBehaviorDefinition)} {builder.GetId()} has {nameof(Placed)} that is null",
                    LogSeverity.Error);
            CrosshairPrefix = ob.CrosshairPrefix;

            CrosshairPlace = Create("Place", MyCrosshairIconInfo.IconPosition.TopLeftCorner);
            CrosshairPlaceNoPermission = Create("PlaceNoPerm", MyCrosshairIconInfo.IconPosition.TopLeftCorner);
            CrosshairQuestion = Create("Question", MyCrosshairIconInfo.IconPosition.Center);
            CrosshairRemove = Create("Remove", MyCrosshairIconInfo.IconPosition.TopRightCorner);
            CrosshairRemoveNoPermission = Create("RemoveNoPerm", MyCrosshairIconInfo.IconPosition.TopRightCorner);
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EdgePlacerBehaviorDefinition : MyObjectBuilder_ToolBehaviorDefinition
    {
        [XmlElement]
        public string Layer;

        [XmlElement]
        public string CrosshairPrefix;

        [XmlElement]
        public SerializableDefinitionId Placed;
    }
}