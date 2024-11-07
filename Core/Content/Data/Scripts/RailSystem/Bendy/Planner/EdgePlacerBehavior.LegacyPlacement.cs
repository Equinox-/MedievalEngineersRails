using System;
using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.RailSystem.Util;
using Equinox76561198048419394.RailSystem.Util.Curve;
using Sandbox.ModAPI;
using VRage.Entities.Gravity;
using VRage.Game.ModAPI;
using VRage.Import;
using VRage.Input.Devices.Keyboard;
using VRage.Library.Collections;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.RailSystem.Bendy.Planner
{
    public partial class EdgePlacerBehavior
    {
        private readonly List<EdgePlacerSystem.AnnotatedNode> _vertices = new List<EdgePlacerSystem.AnnotatedNode>();

        private EdgePlacerSystem.AnnotatedNode CreateVertex(Vector3D worldPos, Vector3? pinTangent = null)
        {
            var node = Graph.GetNode(worldPos);
            Vector3D up;
            if (node != null)
            {
                up = node.Up;
                worldPos = node.Position;
                if (node.TangentPins > 0)
                    pinTangent = null;
            }
            else
            {
                up = Vector3D.Normalize(-MyGravityProviderSystem.CalculateNaturalGravityInPoint(worldPos));
                if (!up.IsValid() || up.LengthSquared() < 1e-3f)
                    up = Vector3D.Up;
            }

            if (pinTangent.HasValue)
                pinTangent = Vector3.Normalize(pinTangent.Value);

            return new EdgePlacerSystem.AnnotatedNode
            {
                Position = worldPos,
                Up = (Vector3)up,
                Existing = node,
                Tangent = pinTangent ?? node?.Tangent ?? Vector3.Zero,
                TangentPin = pinTangent
            };
        }

        private bool TryAddVertex()
        {
            var vertex = CreateVertex(LookingAtPosition);
            if (_vertices.Count > 0 && Vector3D.DistanceSquared(_vertices[_vertices.Count - 1].Position, vertex.Position) < RailConstants.NodeMergeDistanceSq)
                return false;
            _vertices.Add(vertex);
            EdgePlacerSystem.AnnotateNodes(Graph, _vertices);
            vertex = _vertices[_vertices.Count - 1];
            if ((vertex.Existing == null || vertex.Existing.TangentPins == 0) && (_gradeHint != null || _directionHint != null))
            {
                // Apply gradle and direction constraints.
                var surf = DirectionAndGrade.ComputeSurfaceMatrix(vertex.Position);
                DirectionAndGrade.DecomposeTangent(surf, vertex.Tangent, out var dir, out var grade);
                if (_directionHint != null)
                {
                    // If the direction is flipping then also flip the suggested grade.
                    dir += _directionHint.Value - MathHelper.PiOver2;
                }

                if (_gradeHint.HasValue)
                    grade = (grade < 0 ? -1 : 1) * _gradeHint.Value;

                vertex.TangentPin = DirectionAndGrade.ComposeTangent(in surf, dir, grade);
                _vertices[_vertices.Count - 1] = vertex;
                EdgePlacerSystem.AnnotateNodes(Graph, _vertices);
            }
            return true;
        }
        
        private bool StartPlacementLegacy(IMyPlayer player)
        {
            if (MyAPIGateway.Input.IsKeyDown(MyKeys.Shift))
            {
                var originalStart = _vertices[0];
                TryAddVertex();
                if (_vertices.Count != 2)
                    return false;
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
        }

        private void HandlePlaceLegacy()
        {
            _remove.Clear();
            TryAddVertex();
            if (_vertices.Count < 2) return;
            var pts = _vertices.Select(x => x.Position).ToArray();
            var tangents = _vertices.Select(x => x.TangentPin.HasValue ? VF_Packer.PackNormal(x.TangentPin.Value) : 0).ToArray();
            _vertices.RemoveRange(0, _vertices.Count - 1);
            EdgePlacerSystem.RaisePlaceEdge(
                new EdgePlacerSystem.EdgePlacerConfig
                {
                    EntityPlacing = Holder.EntityId,
                    Placed = Definition.Placed
                },
                pts,
                tangents
            );
        }

        private void DrawPlacementLegacy(ref Renderer renderer)
        {
            foreach (var k in _vertices)
                renderer.DrawNode(in k);

            using (new TemporaryVertex(this))
            using (PoolManager.Get(out List<IntermediateCurve> curves))
            {
                for (var i = 1; i < _vertices.Count; i++)
                {
                    var nextVert = _vertices[i];
                    var currentVert = _vertices[i - 1];

                    var nextMatrix = nextVert.Matrix;
                    var currentMatrix = currentVert.Matrix;

                    var color = PlacedDefinition == null || EdgePlacerSystem.VerifyEdge(PlacedDefinition, currentVert, nextVert, null)
                        ? EdgeColor
                        : EdgeColorBad;
                    ICurve curve;
                    if (Vector3D.DistanceSquared(currentMatrix.Translation, nextMatrix.Translation) > 30 * 30)
                    {
                        curve = PrepareSphericalBez(currentMatrix, nextMatrix);
                        renderer.DrawCurve(curve, color);
                    }
                    else
                    {
                        curve = PrepareNormalBez(currentMatrix, nextMatrix);
                        renderer.DrawCurve(curve, color);
                    }
                    if (_showMaxCurvature || _showMaxGrade)
                        curves.Add(new IntermediateCurve(curve));
                }

                for (var i = 0; i < curves.Count; i++)
                {
                    var intermediate = curves[i];
                    var prevCurve = i > 0 ? (IntermediateCurve?)curves[i - 1] : null;
                    var nextCurve = i + 1 < curves.Count ? (IntermediateCurve?)curves[i + 1] : null;
                    if (_showMaxCurvature)
                    {
                        // Determine if this curve has a local maximum in curvature.
                        var curvature = intermediate.Curvature.Max;
                        if (curvature > 1 / 1000f &&
                            (prevCurve?.Curvature.Max ?? float.NegativeInfinity) < curvature &&
                            (nextCurve?.Curvature.Max ?? float.NegativeInfinity) < curvature)
                        {
                            MyRenderProxy.DebugDrawText3D(
                                intermediate.Curve.Sample(intermediate.Curvature.MaxAtTime),
                                $" R_min={1 / curvature:F2}m",
                                Color.Purple, 0.7f, false,
                                align: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP);
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
                                intermediate.Curve.Sample(intermediate.Grade.MaxAtTime),
                                $"G_max={100 * intermediate.SignedGradeMax:F0}% ",
                                Color.Purple, 0.7f, false,
                                align: MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_TOP);
                        }
                    }
                }
            }
        }
    }
}