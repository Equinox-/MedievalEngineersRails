using System;
using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.RailSystem.Util;
using Equinox76561198048419394.RailSystem.Util.Curve;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents.Character;
using VRage.Entities.Gravity;
using VRage.Game.ModAPI;
using VRage.Library.Collections;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.RailSystem.Bendy.Planner
{
    public partial class EdgePlacerBehavior
    {
        private bool StartV2(IMyPlayer player, MyHandItemActionEnum action)
        {
            return true;
        }

        public enum ModeV2
        {
            Legacy = 0,
            Place = 1,
            Move = 2,
            Split = 3,
        }

        private ModeV2 _mode = ModeV2.Legacy;

        private TempEdgePlan _tempPlan;
        private TempEdgePlan.NodeId? _prevNode = null;
        private TempEdgePlan.NodeId? _movingNode = null;

        private void SetMode(ModeV2 newMode)
        {
            if (newMode == _mode) return;
            _mode = newMode;
            _prevNode = null;
            _movingNode = null;
        }

        public bool ShouldUseV2(MyHandItemActionEnum action)
        {
            if (_mode == ModeV2.Legacy)
                return false;
            return action != MyHandItemActionEnum.Secondary || _tempPlan.Nodes.Count > 0;
        }

        private TempEdgePlan.NodeId GetOrCreateNode(Vector3D pos, out bool created)
        {
            var nodeId = _tempPlan.GetOrCreateNode(pos, out created);
            if (created)
            {
                ref var node = ref _tempPlan.GetNode(nodeId);
                ApplyV2Options(ref node);
            }

            return nodeId;
        }

        private void HandleV2()
        {
            switch (_mode)
            {
                case ModeV2.Place:
                    HandlePlaceMode();
                    break;
                case ModeV2.Move:
                    HandleMoveMode();
                    break;
                case ModeV2.Split:
                    HandleSplitMode();
                    break;
                case ModeV2.Legacy:
                default:
                    return;
            }
        }

        private IEnumerable<ToolHint> GetV2Hints()
        {
            switch (_mode)
            {
                case ModeV2.Place:
                    return GetPlaceModeHints();
                case ModeV2.Move:
                    return GetMoveModeHints();
                case ModeV2.Split:
                    return GetSplitModeHints();
                case ModeV2.Legacy:
                default:
                    break;
            }

            return Enumerable.Empty<ToolHint>();
        }

        #region Add Remove Handling

        private IEnumerable<ToolHint> GetPlaceModeHints()
        {
            var hasNode = _tempPlan.TryGetNearestNode(Target.Position, out var nearNode);
            if (_prevNode.HasValue && hasNode)
                yield return new ToolHint("Press [KEY:ToolPrimary] to create edge", Definition.CrosshairPlace);
            else if (_prevNode.HasValue)
                yield return new ToolHint("Press [KEY:ToolPrimary] to create edge and node", Definition.CrosshairPlace);
            else if (hasNode)
                yield return new ToolHint("Press [KEY:ToolPrimary] to start edge", Definition.CrosshairPlace);
            else
                yield return new ToolHint("Press [KEY:ToolPrimary] to create node", Definition.CrosshairPlace);

            if (_prevNode.HasValue && (!hasNode || nearNode != _prevNode.Value))
                yield return new ToolHint("Press [KEY:ToolTertiary] to inspect new edge", Definition.CrosshairQuestion);
            else if (!_prevNode.HasValue && _tempPlan.TryGetNearestEdge(Target.Position, out _, out _, out _))
                yield return new ToolHint("Press [KEY:ToolTertiary] to inspect existing edge", Definition.CrosshairQuestion);

            if (hasNode)
                yield return new ToolHint("Press [KEY:ToolSecondary] to remove node", Definition.CrosshairRemove);
            else if (_prevNode.HasValue)
                yield return new ToolHint("Press [KEY:ToolSecondary] to discard edge", Definition.CrosshairRemove);
        }

        private void HandlePlaceMode()
        {
            switch (ActiveAction)
            {
                case MyHandItemActionEnum.Primary:
                    HandlePlaceV2();
                    break;
                case MyHandItemActionEnum.Secondary:
                    HandleRemoveV2();
                    break;
                case MyHandItemActionEnum.Tertiary:
                    HandleInspectV2();
                    break;
                case MyHandItemActionEnum.None:
                default:
                    return;
            }
        }

        private void HandlePlaceV2()
        {
            var newNode = GetOrCreateNode(Target.Position, out _);
            if (_prevNode != null)
                _tempPlan.GetOrCreateEdge(_prevNode.Value, newNode, out _);
            _prevNode = newNode;
            _tempPlan.Update();
        }

        private void HandleRemoveV2()
        {
            if (_tempPlan.TryGetNearestNode(Target.Position, out var existingNode))
                _tempPlan.RemoveNode(existingNode);
            else
                _prevNode = null;
        }

        private void HandleInspectV2()
        {
            using (PoolManager.Get(out List<TempEdgePlan.EdgeId> edges))
            {
                if (!_prevNode.HasValue)
                {
                    InspectInstantV2();
                    return;
                }

                using (var tempNode = new TemporaryAddition(this, Target.Position, _prevNode.Value))
                {
                    if (!tempNode.TempEdge.HasValue)
                        return;
                    edges.Add(tempNode.TempEdge.Value);
                    InspectV2(edges);
                }
            }
        }

        private readonly struct TemporaryAddition : IDisposable
        {
            private readonly EdgePlacerBehavior _owner;
            private readonly bool _createdTempNode;
            private readonly bool _createdTempEdge;
            public readonly TempEdgePlan.NodeId TempNode;
            public readonly TempEdgePlan.EdgeId? TempEdge;

            public TemporaryAddition(EdgePlacerBehavior behavior, Vector3D pos, TempEdgePlan.NodeId? connectTo)
            {
                _owner = behavior;
                TempNode = _owner.GetOrCreateNode(pos, out _createdTempNode);
                var createdTempEdge = false;
                TempEdge = connectTo == null || TempNode.Equals(connectTo.Value)
                    ? default(TempEdgePlan.EdgeId?)
                    : _owner._tempPlan.GetOrCreateEdge(connectTo.Value, TempNode, out createdTempEdge);
                _createdTempEdge = createdTempEdge;
            }

            public void Dispose()
            {
                if (_createdTempEdge && TempEdge.HasValue)
                    _owner._tempPlan.RemoveEdge(TempEdge.Value);
                if (_createdTempNode)
                    _owner._tempPlan.RemoveNode(TempNode);
            }
        }

        #endregion

        #region Move Handling

        private IEnumerable<ToolHint> GetMoveModeHints()
        {
            if (_movingNode.HasValue)
            {
                yield return "Press [KEY:ToolPrimary] to drop";
                if (_tempPlan.GetEdges(_movingNode.Value).Count > 0)
                    yield return new ToolHint("Press [KEY:ToolTertiary] to inspect edge", Definition.CrosshairQuestion);
                yield return "Press [KEY:ToolSecondary] to return";
            }
            else if (_tempPlan.TryGetNearestNode(Target.Position, out _))
            {
                yield return "Press [KEY:ToolPrimary] to pick up";
            }
            if (!_movingNode.HasValue && _tempPlan.TryGetNearestEdge(Target.Position, out _, out _, out _))
                yield return new ToolHint("Press [KEY:ToolTertiary] to inspect existing edge", Definition.CrosshairQuestion);
        }

        private void HandleMoveMode()
        {
            switch (ActiveAction)
            {
                case MyHandItemActionEnum.Primary:
                    HandleMove();
                    break;
                case MyHandItemActionEnum.Secondary:
                    // Revert held node.
                    _movingNode = null;
                    break;
                case MyHandItemActionEnum.Tertiary:
                    if (!_movingNode.HasValue)
                    {
                        InspectInstantV2();
                        return;
                    }

                    using (PoolManager.Get(out List<TempEdgePlan.EdgeId> edges))
                    {
                        foreach (var neighbor in _tempPlan.GetEdges(_movingNode.Value))
                            edges.Add(neighbor.Value);
                        if (edges.Count > 0)
                            InspectV2(edges);
                    }

                    break;
                case MyHandItemActionEnum.None:
                default:
                    return;
            }
        }

        private void HandleMove()
        {
            if (!_movingNode.HasValue)
            {
                _movingNode = _tempPlan.TryGetNearestNode(Target.Position, out var nearby) ? (TempEdgePlan.NodeId?)nearby : null;
                return;
            }

            ref var node = ref _tempPlan.GetNode(_movingNode.Value);
            node.RawPosition = Target.Position;
            ApplyV2Options(ref node);
            _movingNode = null;
        }

        private readonly struct MovingNodeV2 : IDisposable
        {
            private readonly EdgePlacerBehavior _owner;
            private readonly TempEdgePlan.NodeId _moving;
            private readonly TempEdgePlan.NodeData _snapshot;

            public MovingNodeV2(EdgePlacerBehavior behavior, Vector3D pos, TempEdgePlan.NodeId moving)
            {
                _owner = behavior;
                _moving = moving;
                ref var node = ref _owner._tempPlan.GetNode(moving);
                _snapshot = node;
                node.RawPosition = pos;
                _owner.ApplyV2Options(ref node);
            }

            public void Dispose()
            {
                ref var node = ref _owner._tempPlan.GetNode(_moving);
                node.PinnedDirection = _snapshot.PinnedDirection;
                node.PinnedGrade = _snapshot.PinnedGrade;
                node.Smoothness = _snapshot.Smoothness;
                node.VerticalShift = _snapshot.VerticalShift;
                node.RawPosition = _snapshot.RawPosition;
                node.Dirty = true;
            }
        }

        #endregion

        #region Split Handling

        private IEnumerable<ToolHint> GetSplitModeHints()
        {
            if (_tempPlan.TryGetNearestEdge(Target.Position, out _, out _, out _,
                    minSeparation: PlacedDefinition.PreferredDistance))
                yield return "Press [KEY:ToolPrimary] to split";
            if (_tempPlan.TryGetNearestEdge(Target.Position, out _, out _, out _))
                yield return new ToolHint("Press [KEY:ToolTertiary] to inspect existing edge", Definition.CrosshairQuestion);
        }

        private void HandleSplitMode()
        {
            switch (ActiveAction)
            {
                case MyHandItemActionEnum.Primary:
                    if (!_tempPlan.TryGetNearestEdge(Target.Position, out var edgeId, out var curve, out var curveTime))
                        return;
                    ref var edge = ref _tempPlan.GetEdge(edgeId);
                    var left = edge.Left;
                    var right = edge.Right;
                    var splitPos = curve.Sample(curveTime);
                    var midPoint = GetOrCreateNode(splitPos, out _);
                    _tempPlan.RemoveEdge(edgeId);
                    _tempPlan.GetOrCreateEdge(left, midPoint, out _);
                    _tempPlan.GetOrCreateEdge(midPoint, right, out _);
                    break;
                case MyHandItemActionEnum.Secondary:
                case MyHandItemActionEnum.Tertiary:
                    InspectInstantV2();
                    break;
                case MyHandItemActionEnum.None:
                default:
                    return;
            }
        }

        #endregion

        private void InspectInstantV2()
        {
            if (!_tempPlan.TryGetNearestEdge(Target.Position, out _, out var curve, out var curveTime))
                return;
            var position = curve.Sample(curveTime);
            var firstDerivative = (Vector3) curve.SampleDerivative(curveTime);
            var secondDerivative = (Vector3) curve.SampleSecondDerivative(curveTime);
            var curvature = CurveExtensions.Curvature(firstDerivative, secondDerivative);
            var surface = DirectionAndGrade.ComputeSurfaceMatrix(position);
            var tangent = Vector3.Normalize(firstDerivative);
            DirectionAndGrade.DecomposeTangent(surface, tangent, out _, out var grade);
            using (var hint = new HintToken(this))
            {
                hint.Text.Append($"Radius {1 / curvature:F0}m");
                hint.Text.AppendLine();

                hint.Text.Append($"Grade {grade * 100:F0}%");
                if (PlacedDefinition != null && Math.Abs(grade) > PlacedDefinition.MaxGradeRatio)
                    hint.Text.AppendLine($" {(grade < 0 ? "<= -" : ">= ")}{PlacedDefinition.MaxGradeRatio * 100:F0}%");
                else
                    hint.Text.AppendLine();
            }
        }

        private void InspectV2(List<TempEdgePlan.EdgeId> edges)
        {
            using (var hint = new HintToken(this))
            {
                var maxCurvature = 0f;
                var maxSignedGrade = 0f;
                var length = 0f;
                var maxBend = default(double?);
                foreach (var edgeId in edges)
                {
                    ref var edge = ref _tempPlan.GetEdge(edgeId);
                    foreach (var curve in edge.IntermediateCurves)
                    {
                        var curvature = curve.Curvature.Max;
                        if (curvature > maxCurvature)
                            maxCurvature = curvature;
                        var signedGrade = curve.SignedGradeMax;
                        if (Math.Abs(signedGrade) > Math.Abs(maxSignedGrade))
                            maxSignedGrade = signedGrade;
                        length += curve.Length;
                        var bend = curve.JointParameters.BendRadians;
                        if (bend.HasValue && bend.Value > (maxBend ?? -1))
                            maxBend = bend;
                    }
                }

                hint.Text.Append($"Length {length:F1} m");
                if (PlacedDefinition != null && length < PlacedDefinition.Distance.Min)
                    hint.Text.AppendLine($" <= {PlacedDefinition.Distance.Min:F1} m");
                else
                    hint.Text.AppendLine();

                if (maxCurvature > 0)
                {
                    hint.Text.Append($"Min Radius {1 / maxCurvature:F0}m");
                    hint.Text.AppendLine();
                }

                hint.Text.Append($"Grade {maxSignedGrade * 100:F0}%");
                if (PlacedDefinition != null && Math.Abs(maxSignedGrade) > PlacedDefinition.MaxGradeRatio)
                    hint.Text.AppendLine($" {(maxSignedGrade < 0 ? "<= -" : ">= ")}{PlacedDefinition.MaxGradeRatio * 100:F0}%");
                else
                    hint.Text.AppendLine();

                if (maxBend.HasValue)
                {
                    var b = maxBend.Value;
                    hint.Text.Append($"Curve {b * 180 / Math.PI:F0}º");
                    if (PlacedDefinition != null && b > PlacedDefinition.MaxAngleRadians)
                        hint.Text.AppendLine($" >= {PlacedDefinition.MaxAngleDegrees}º");
                    else
                        hint.Text.AppendLine();
                }
            }
        }

        private void DrawV2(ref Renderer renderer)
        {
            switch (_mode)
            {
                case ModeV2.Place:
                    using (var temp = new TemporaryAddition(this, LookingAtPosition, _prevNode))
                    {
                        DrawV2Internal(ref renderer, temp.TempNode);
                    }

                    return;
                case ModeV2.Move when _movingNode.HasValue:
                    using (new MovingNodeV2(this, LookingAtPosition, _movingNode.Value))
                    {
                        DrawV2Internal(ref renderer, _movingNode);
                    }
                    return;
                case ModeV2.Split:
                {
                    TempEdgePlan.EdgeId? highlightEdge = null;
                    if (_tempPlan.TryGetNearestEdge(Target.Position, out var splitId, out var curve, out var curveTime))
                    {
                        highlightEdge = splitId;
                        var splitPos = curve.Sample(curveTime); 
                        var up = MyGravityProviderSystem.CalculateTotalGravityInPoint(splitPos);
                        up /= -up.Length();
                        renderer.DrawNode(splitPos, up, Color.Purple);
                    }
                    DrawV2Internal(ref renderer, highlightEdge: highlightEdge);
                    break;
                }
                case ModeV2.Legacy:
                default:
                    DrawV2Internal(ref renderer);
                    return;
            }
        }

        private void DrawV2Internal(ref Renderer renderer, TempEdgePlan.NodeId? highlightNode = null, TempEdgePlan.EdgeId? highlightEdge = null)
        {
            _tempPlan.Update();
            foreach (var rawNodeId in _tempPlan.Nodes.Ids)
            {
                var highlight = rawNodeId == highlightNode?.RawId;
                renderer.DrawNode(in _tempPlan.GetNode(rawNodeId), highlight ? (Vector4)Color.Purple : NodeColor);
            }

            foreach (var rawEdgeId in _tempPlan.Edges.Ids)
            {
                ref var edge = ref _tempPlan.GetEdge(rawEdgeId);
                var highlight = rawEdgeId == highlightEdge?.RawId;
                for (var i = 0; i < edge.IntermediateCurves.Count; i++)
                {
                    var intermediate = edge.IntermediateCurves[i];
                    var isOkay = EdgePlacerSystem.VerifyEdge(PlacedDefinition, intermediate.JointParameters);
                    var color = highlight ? Color.Purple : isOkay ? Color.Green : Color.Red;
                    renderer.DrawCurve(intermediate.Curve, color);
                    var prevCurve = i > 0 ? (IntermediateCurve?)edge.IntermediateCurves[i - 1] : null;
                    var nextCurve = i + 1 < edge.IntermediateCurves.Count ? (IntermediateCurve?)edge.IntermediateCurves[i + 1] : null;
                    if (_showMaxCurvature)
                    {
                        // Determine if this curve has a local maximum in curvature.
                        var curvature = intermediate.Curvature.Max;
                        if ((prevCurve?.Curvature.Max ?? float.NegativeInfinity) < curvature &&
                            (nextCurve?.Curvature.Max ?? float.NegativeInfinity) < curvature)
                        {
                            MyRenderProxy.DebugDrawText3D(
                                intermediate.Curve.Sample(intermediate.Curvature.MaxAtTime),
                                $"R_min={1 / curvature:F2}m",
                                Color.Red, 0.5f, false);
                        }
                    }

                    if (_showMaxGrade)
                    {
                        // Determine if this curve has a local maximum in grade
                        var grade = intermediate.UnsignedGradeMax;
                        if ((prevCurve?.UnsignedGradeMax ?? float.NegativeInfinity) < grade &&
                            (nextCurve?.UnsignedGradeMax ?? float.NegativeInfinity) < grade)
                        {
                            MyRenderProxy.DebugDrawText3D(
                                intermediate.Curve.Sample(intermediate.Curvature.MaxAtTime),
                                $"G_max={100 * intermediate.SignedGradeMax:F0}%",
                                Color.Red, 0.5f, false);
                        }
                    }
                }
            }
        }
    }
}