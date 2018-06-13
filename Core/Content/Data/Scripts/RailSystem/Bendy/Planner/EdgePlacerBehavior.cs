using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Bendy.Shape;
using Equinox76561198048419394.RailSystem.Construction;
using Equinox76561198048419394.RailSystem.Util;
using Equinox76561198048419394.RailSystem.Voxel;
using Medieval.Constants;
using Medieval.GameSystems;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using VRage;
using VRage.Components.Entity.Camera;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.GUI.Crosshair;
using VRage.Library.Collections;
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

        public MyEntity Owner => Holder;

        private struct VertexData
        {
            public readonly Vector3D Position;
            public readonly Vector3D Up;
            public readonly Node Node;

            public VertexData(Vector3D pos, Vector3D up, Node node)
            {
                Position = pos;
                Up = up;
                Node = node;
            }
        }

        private readonly List<VertexData> _vertices = new List<VertexData>();
        private bool _connectToPlayer = true;


        private VertexData CreateVertex(Vector3D worldPos)
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

            return new VertexData(worldPos, up, node);
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
            Definition = (EdgePlacerBehaviorDefinition) definition;
            PlacedDefinition = EdgePlacerSystem.DefinitionFor(Definition.Placed);
        }

        public override void Activate()
        {
            base.Activate();
            Graph = MySession.Static.Components.Get<BendyController>().GetOrCreateLayer(Layer);
            _vertices.Clear();
            if (IsLocallyControlled)
                MySession.Static.Components.Get<MyUpdateComponent>().AddFixedUpdate(Render);
        }

        public override void Deactivate()
        {
            base.Deactivate();
            _vertices.Clear();
            Graph = null;
            MySession.Static.Components.Get<MyUpdateComponent>().RemoveFixedUpdate(Render);
            _hintInfo?.Hide();
            _hintInfo = null;
        }

        #region Rendering

        private MatrixD ComputeVertexMatrix(VertexData vert, int index)
        {
            if (vert.Node != null)
                return vert.Node.Matrix;

            var prevPos = (index - 1) >= 0 ? (Vector3D?) _vertices[index - 1].Position : null;
            var nextPos = (index + 1) < _vertices.Count ? (Vector3D?) _vertices[index + 1].Position : null;

            var tan = Vector3D.Zero;
            if (prevPos.HasValue)
            {
                var t = (vert.Position - prevPos.Value).SafeNormalized();
                tan += tan.Dot(t) < 0 ? -t : t;
            }

            if (nextPos.HasValue)
            {
                var t = (vert.Position - nextPos.Value).SafeNormalized();
                tan += tan.Dot(t) < 0 ? -t : t;
            }

            if (!tan.IsValid() || tan.LengthSquared() < 1e-3f)
                tan = Vector3D.Cross(vert.Up, vert.Up.Shifted());
            tan.SafeNormalized();
            return MatrixD.CreateWorld(vert.Position, tan, vert.Up);
        }

        private void Render()
        {
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
                var nextNode = _connectToPlayer && Holder != null
                    ? (VertexData?) CreateVertex(Holder.GetPosition())
                    : null;
                if (nextNode.HasValue)
                    _vertices.Add(nextNode.Value);

                for (var i = 1; i < _vertices.Count; i++)
                {
                    var nextVert = _vertices[i];
                    var nextMatrix = ComputeVertexMatrix(nextVert, i);

                    var currentVert = _vertices[i - 1];
                    var prevPos = i >= 2
                        ? (Vector3D?) _vertices[i - 2].Position
                        : currentVert.Node?.Opposition(nextVert.Position)?.Position;
                    var currentMatrix = ComputeVertexMatrix(currentVert, i - 1);

                    DrawBez(currentMatrix, nextMatrix,
                        PlacedDefinition == null || EdgePlacerSystem.VerifyJoint(PlacedDefinition, prevPos,
                            currentVert.Position, nextVert.Position, null)
                            ? _edgeColor
                            : _edgeColorBad);
                }

                if (nextNode.HasValue)
                    _vertices.RemoveAt(_vertices.Count - 1);
            }
        }

        private static void DrawBez(MatrixD prev, MatrixD next, Vector4 color)
        {
            var cam = MyCameraComponent.ActiveCamera;
            if (cam == null)
                return;
            var center = (prev.Translation + next.Translation) / 2;
            var factor = Math.Sqrt(Vector3D.DistanceSquared(prev.Translation, next.Translation) /
                                   (1 + Vector3D.DistanceSquared(cam.GetPosition(), center)));
            var count = MathHelper.Clamp(factor * 100, 1, 10);
            var lastPos = default(Vector3D);
            for (var t = 0; t <= count; t++)
            {
                var pos = Bezier.BSpline(prev, next, t / 10f);
                var pact = pos.Translation + pos.Up * _edgeMarkerVertOffset;
                if (t > 0)
                    MySimpleObjectDraw.DrawLine(lastPos, pact, _squareMaterial, ref color, _edgeWidth);
                lastPos = pact;
            }
        }

        #endregion

        private readonly List<string> _tmpMessages = new List<string>();

        protected override bool Start(MyHandItemActionEnum action)
        {
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
                return false;

            switch (action)
            {
                case MyHandItemActionEnum.Tertiary:
                    return _vertices.Count > 0;
                case MyHandItemActionEnum.Secondary:
                    string err;
                    if (ValidateDeconstruct(out err, true))
                        return true;
                    if (!string.IsNullOrEmpty(err))
                        player.ShowNotification(err, 2000, null,
                            new Vector4(1, 0, 0, 1));
                    return false;
                case MyHandItemActionEnum.Primary:
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

        private bool TargetIsStatic => Target.Entity?.Physics?.IsStatic ?? true;

        private bool IsLocallyControlled => MySession.Static.PlayerEntity == Holder;

        private IMyHudNotification _hintInfo;

        protected override void Hit()
        {
            if (!IsLocallyControlled)
                return;

            switch (ActiveAction)
            {
                case MyHandItemActionEnum.Tertiary:
                {
                    if (_vertices.Count == 0)
                        return;
                    if (_hintInfo == null)
                        _hintInfo = MyAPIGateway.Utilities.CreateNotification("");
                    var sb = new StringBuilder();

                    if (!TargetIsStatic)
                        sb.AppendLine("Can't place node on dynamic physics");
                    else
                    {
                        var jointData = EdgePlacerSystem.ComputeJointParameters(
                            _vertices.Count > 1 ? (Vector3D?) _vertices[_vertices.Count - 2].Position : null,
                            _vertices[_vertices.Count - 1].Position, Target.Position);
                        sb.Append($"Length {jointData.Length:F1} m");
                        if (PlacedDefinition != null && jointData.Length > PlacedDefinition.Distance.Max)
                            sb.AppendLine($" > {PlacedDefinition.Distance.Max:F1} m");
                        else if (PlacedDefinition != null && jointData.Length < PlacedDefinition.Distance.Min)
                            sb.AppendLine($" < {PlacedDefinition.Distance.Min:F1} m");
                        else
                            sb.AppendLine();
                        if (jointData.BendRadians.HasValue)
                        {
                            var b = jointData.BendRadians.Value;
                            sb.Append($"Curve {b * 180 / Math.PI:F0}º");
                            if (PlacedDefinition != null && b > PlacedDefinition.MaxAngleRadians)
                                sb.AppendLine($" > {PlacedDefinition.MaxAngleDegrees}º");
                            else
                                sb.AppendLine();
                        }

                        if (jointData.Grade.HasValue)
                        {
                            var g = jointData.Grade.Value;
                            sb.Append($"Grade {g * 100:F0}%");
                            if (PlacedDefinition != null && g > PlacedDefinition.MaxGradeRatio)
                                sb.AppendLine($" > {PlacedDefinition.MaxGradeRatio * 100:F0}%");
                            else
                                sb.AppendLine();
                        }
                    }

                    _hintInfo.Text = sb.ToString();
                    _hintInfo.Show(); // resets alive time + adds to queue if it's not in it
                    return;
                }
                case MyHandItemActionEnum.Secondary:
                {
                    _vertices.Clear();
                    var entity = Target.Entity?.Components.Get<BendyShapeProxy>()?.Owner ?? Target.Entity;
                    var dynCon = entity?.Components.Get<BendyComponent>();
                    if (dynCon == null || dynCon.Graph != Graph)
                        return;
                    EdgePlacerSystem.RaiseRemoveEdge(Holder.EntityId, entity.EntityId);
                    return;
                }
                case MyHandItemActionEnum.Primary:
                {
                    _vertices.Add(CreateVertex(Target.Position));
                    if (_vertices.Count < 2) return;
                    EdgePlacerSystem.RaisePlaceEdge(new EdgePlacerSystem.EdgePlacerConfig()
                        {
                            EntityPlacing = Holder.EntityId,
                            Placed = Definition.Placed
                        },
                        _vertices.Select(x => x.Position).ToArray());
                    _vertices.RemoveRange(0, _vertices.Count - 1);
                    return;
                }
                case MyHandItemActionEnum.None:
                default:
                    return;
            }
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
            var prev = _vertices[_vertices.Count - 1];
            var prevPrevPos = _vertices.Count >= 2
                ? _vertices[_vertices.Count - 2].Position
                : prev.Node?.Opposition(vert.Position)?.Position;
            if (!EdgePlacerSystem.VerifyJoint(PlacedDefinition, prevPrevPos, prev.Position, vert.Position, errors))
                return false;
            Array.Resize(ref _tempPositions, _vertices.Count + 1);
            for (var i = 0; i < _vertices.Count; i++)
                _tempPositions[i] = _vertices[i].Position;
            _tempPositions[_tempPositions.Length - 1] = vert.Position;
            return EdgePlacerSystem.ValidatePath(PlacedDefinition, Graph, _tempPositions, errors);
        }

        private Vector3D[] _tempPositions;

        private bool ValidateDeconstruct(out string err, bool testPermission)
        {
            err = null;
            if (Target.Entity == null)
                return false;
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
                return false;
            if (!testPermission ||
                player.HasPermission(Target.Entity.GetPosition(), MyPermissionsConstants.QuickDeconstruct))
                return Target.Entity != null && EdgePlacerSystem.ValidateQuickRemove(player, Target.Entity, out err);
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
            if (ValidateDeconstruct(out tmp, true))
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
            string tmp;
            // ReSharper disable once InvertIf
            if (ValidateDeconstruct(out tmp, true))
            {
                if (player.HasPermission(Target.Entity.GetPosition(), MyPermissionsConstants.QuickDeconstruct))
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
            var ob = (MyObjectBuilder_EdgePlacerBehaviorDefinition) builder;
            Layer = ob.Layer;
            if (string.IsNullOrWhiteSpace(Layer))
                MyDefinitionErrors.Add(builder.ModContext,
                    $"{nameof(EdgePlacerBehaviorDefinition)} {builder.GetId()} has {nameof(Layer)} that is null or whitespace",
                    TErrorSeverity.Error);
            Placed = ob.Placed;
            if (Placed.TypeId.IsNull)
                MyDefinitionErrors.Add(builder.ModContext,
                    $"{nameof(EdgePlacerBehaviorDefinition)} {builder.GetId()} has {nameof(Placed)} that is null",
                    TErrorSeverity.Error);
            CrosshairPrefix = ob.CrosshairPrefix;

            CrosshairPlace = Create("_Place", MyCrosshairIconInfo.IconPosition.TopLeftCorner);
            CrosshairPlaceNoPermission = Create("_Place_NoPermission", MyCrosshairIconInfo.IconPosition.TopLeftCorner);
            CrosshairQuestion = Create("_Question", MyCrosshairIconInfo.IconPosition.Center);
            CrosshairRemove = Create("_Remove", MyCrosshairIconInfo.IconPosition.TopRightCorner);
            CrosshairRemoveNoPermission =
                Create("_Remove_NoPermission", MyCrosshairIconInfo.IconPosition.TopRightCorner);
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