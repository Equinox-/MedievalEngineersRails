using System;
using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.RailSystem.Util;
using Equinox76561198048419394.RailSystem.Util.Curve;
using Sandbox.ModAPI;
using VRage.Components.Entity.Camera;
using VRage.Entities.Gravity;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Input.Devices.Keyboard;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy.Planner
{
    public partial class EdgePlacerBehavior
    {
        private readonly List<EdgePlacerSystem.AnnotatedNode> _vertices = new List<EdgePlacerSystem.AnnotatedNode>();

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

        private bool StartPlacementLegacy(IMyPlayer player)
        {
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
        }

        private void HandlePlaceLegacy()
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

        private void DrawPlacementLegacy(ref Renderer renderer)
        {
            foreach (var k in _vertices)
                renderer.DrawNode(in k);

            using (new TemporaryVertex(this))
            {
                for (var i = 1; i < _vertices.Count; i++)
                {
                    var nextVert = _vertices[i];
                    var currentVert = _vertices[i - 1];

                    var nextMatrix = ComputeVertexMatrix(nextVert, i);
                    var currentMatrix = ComputeVertexMatrix(currentVert, i - 1);

                    var color = PlacedDefinition == null || EdgePlacerSystem.VerifyEdge(PlacedDefinition, currentVert, nextVert, null)
                        ? EdgeColor
                        : EdgeColorBad;
                    if (Vector3D.DistanceSquared(currentMatrix.Translation, nextMatrix.Translation) > 30 * 30)
                    {
                        var curve = PrepareSphericalBez(currentMatrix, nextMatrix);
                        renderer.DrawCurve(curve, color);
                    }
                    else
                    {
                        var curve = PrepareNormalBez(currentMatrix, nextMatrix);
                        renderer.DrawCurve(curve, color);
                    }
                }
            }
        }
    }
}