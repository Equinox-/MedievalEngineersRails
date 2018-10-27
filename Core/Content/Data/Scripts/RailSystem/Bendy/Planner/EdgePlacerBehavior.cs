using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Bendy.Shape;
using Equinox76561198048419394.RailSystem.Util;
using Equinox76561198048419394.RailSystem.Util.Curve;
using Medieval.Constants;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using VRage.Components.Entity.Camera;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.GUI.Crosshair;
using VRage.Input.Devices.Keyboard;
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

        private struct VertexData
        {
            public Vector3D Position { get; private set; }
            public Vector3D Up { get; private set; }
            public Node Node { get; private set; }

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
            Graph.NodeCreated += NodesChanged;
            Graph.NodeMoved += NodesChanged;
            _vertices.Clear();
            if (IsLocallyControlled)
                MySession.Static.Components.Get<MyUpdateComponent>().AddFixedUpdate(Render);
        }

        private void NodesChanged(Node obj)
        {
            for (var i = 0; i < _vertices.Count; i++)
            {
                var v = _vertices[i];
                var newNode = Graph.GetNode(v.Position);
                if (newNode == v.Node)
                    continue;
                if (newNode != null)
                    _vertices[i] = new VertexData(newNode.Position, newNode.Up, newNode);
                else
                    _vertices[i] = new VertexData(v.Position, v.Up, null);
            }
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

        private MatrixD ComputeVertexMatrix(VertexData vert, int index)
        {
            if (vert.Node != null)
                return vert.Node.Matrix;

            var prevPos = (index - 1) >= 0 ? (VertexData?) _vertices[index - 1] : null;
            var nextPos = (index + 1) < _vertices.Count ? (VertexData?) _vertices[index + 1] : null;

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
                if (prevPos?.Node != null)
                {
                    var pp = prevPos.Value.Node;
                    tan = CurveExtensions.ExpandToCubic(pp.Position, pp.Position + pp.Tangent, vert.Position,
                              RailConstants.LongBezControlLimit) - vert.Position;
                }
                else if (nextPos?.Node != null)
                {
                    var pp = nextPos.Value.Node;
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

            {
                var nextNode = _connectToPlayer && Holder != null
                    ? (VertexData?) CreateVertex(Holder.GetPosition())
                    : null;
                if (nextNode.HasValue)
                    _vertices.Add(nextNode.Value);

                for (var i = 1; i < _vertices.Count; i++)
                {
                    var nextVert = _vertices[i];
                    var currentVert = _vertices[i - 1];
                    var prevPos = i >= 2
                        ? _vertices[i - 2].Position
                        : currentVert.Node?.Opposition(nextVert.Position)?.Position;

                    var nextMatrix = ComputeVertexMatrix(nextVert, i);
                    var currentMatrix = ComputeVertexMatrix(currentVert, i - 1);

                    var color = PlacedDefinition == null || EdgePlacerSystem.VerifyJoint(PlacedDefinition, prevPos,
                                    currentVert.Position, nextVert.Position, null)
                        ? _edgeColor
                        : _edgeColorBad;
                    if (Vector3D.DistanceSquared(currentMatrix.Translation, nextMatrix.Translation) > 30 * 30)
                    {
                        var curve = PrepareSphericalBez(currentMatrix, nextMatrix);
                        DrawBez(currentMatrix.Up, nextMatrix.Up, curve, color, 100);
                    }
                    else
                    {
                        if (Math.Min(Vector3D.DistanceSquared(cam.GetPosition(), nextVert.Position),
                                Vector3D.DistanceSquared(cam.GetPosition(), currentVert.Position)) > 100 * 100)
                            continue;
                        var curve = PrepareNormalBez(currentMatrix, nextMatrix);
                        DrawBez(currentMatrix.Up, nextMatrix.Up, curve, color);
                    }
                }

                if (nextNode.HasValue)
                    _vertices.RemoveAt(_vertices.Count - 1);
            }
        }

        private static CubicSphericalCurve PrepareSphericalBez(MatrixD m1, MatrixD m2)
        {
            CurveExtensions.AlignFwd(ref m1, ref m2);
            return new CubicSphericalCurve(
                MyGamePruningStructure.GetClosestPlanet(m1.Translation)?.PositionComp.WorldVolume.Center ??
                Vector3D.Zero, m1, m2);
        }

        private static CubicCurve PrepareNormalBez(MatrixD m1, MatrixD m2)
        {
            CurveExtensions.AlignFwd(ref m1, ref m2);
            return new CubicCurve(m1, m2);
        }

        private static void DrawBez<T>(Vector3D up1, Vector3D up2, T bezCurve, Vector4 color, int? forcedLod = null)
            where T : ICurve
        {
            var cam = MyCameraComponent.ActiveCamera;
            if (cam == null)
                return;
            var first = bezCurve.Sample(0);
            var last = bezCurve.Sample(1);
            var center = (first + last) / 2;
            var factor = Math.Sqrt(Vector3D.DistanceSquared(first, last) /
                                   (1 + Vector3D.DistanceSquared(cam.GetPosition(), center)));
            var count = forcedLod ?? MathHelper.Clamp(factor * 100, 1, 25);
            var lastPos = default(Vector3D);
            for (var t = 0; t <= count; t++)
            {
                var time = t / (float) count;
                var pos = bezCurve.Sample(time);
                var pact = pos + Vector3D.Lerp(up1, up2, time) * _edgeMarkerVertOffset;
                if (t > 0)
                    MySimpleObjectDraw.DrawLine(lastPos, pact, _squareMaterial, ref color, _edgeWidth);
                lastPos = pact;
            }
        }

        #endregion

        private readonly List<string> _tmpMessages = new List<string>();

        protected override bool Start(MyHandItemActionEnum action)
        {
            if (!IsLocallyControlled)
                return false;
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
                return false;
            if (Target.Entity == null || Vector3D.DistanceSquared(Target.Position, Holder.GetPosition()) > 25 * 25)
            {
                player.ShowNotification($"Too far away from the chosen point.  Click closer to yourself.", 2000, null, new Vector4(1, 0, 0, 1));
                return false;
            }

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
                    if (_vertices.Count == 1 && MyAPIGateway.Input.IsKeyDown(MyKeys.Shift))
                    {
                        var lastVertex = CreateVertex(Target.Position);
                        _vertices.Add(lastVertex);
                        var jointData = EdgePlacerSystem.ComputeJointParameters(_vertices[0].Node?.Opposition(lastVertex.Position)?.Position,
                            _vertices[0].Position, lastVertex.Position);
                        {
                            _tmpMessages.Clear();
                            if (jointData.BendRadians.HasValue)
                            {
                                var angle = jointData.BendRadians.Value;
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
                                return false;
                            }
                        }
                        ComputeLong(_vertices[0], _vertices[1]);
                        player.ShowNotification($"Divided into {_vertices.Count - 1} segments");
                        if (_vertices.Count > 0)
                            _vertices.RemoveAt(_vertices.Count - 1);
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
                                _vertices.Count > 1
                                    ? _vertices[_vertices.Count - 2].Position
                                    : _vertices[0].Node?.Opposition(Target.Position)?.Position,
                                _vertices[_vertices.Count - 1].Position, Target.Position);
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

        #region Long-Place

        private void ComputeLong(VertexData first, VertexData last)
        {
            _vertices.Clear();
            _vertices.Add(first);
            _vertices.Add(last);
            var m1 = ComputeVertexMatrix(first, 0);
            var m2 = ComputeVertexMatrix(last, 1);
            var bez = PrepareSphericalBez(m1, m2);

            var length = 0d;
            var prev = default(Vector3D);
            for (var t = 0; t < 100; t++)
            {
                var curr = bez.Sample(t / 100f);
                if (t > 0)
                {
                    length += Vector3D.Distance(prev, curr);
                }

                prev = curr;
            }

            var minCount = (int) Math.Ceiling(length / PlacedDefinition.Distance.Min);
            var maxCount = (int) Math.Floor(length / PlacedDefinition.Distance.Max);
            var count = (minCount + maxCount) / 2;
            var lenPerCount = length / count;
            _vertices.Clear();
            _vertices.Add(first);
            var time = 0f;
            prev = bez.Sample(0);
            var lengthElapsed = 0d;
            const float timeStep = .001f;
            for (var i = 1; i < count; i++)
            {
                while (lengthElapsed < lenPerCount * i)
                {
                    time += timeStep;
                    var curr = bez.Sample(time);
                    lengthElapsed += Vector3D.Distance(prev, curr);
                    prev = curr;
                }

                _vertices.Add(CreateVertex(prev));
            }

            _vertices.Add(last);
        }

        #endregion

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
            var entity = Target.Entity?.Components.Get<BendyShapeProxy>()?.Owner ?? Target.Entity;
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
            var ob = (MyObjectBuilder_EdgePlacerBehaviorDefinition) builder;
            Layer = ob.Layer;
            if (string.IsNullOrWhiteSpace(Layer))
                MyDefinitionErrors.Add(builder.ModContext, $"{nameof(EdgePlacerBehaviorDefinition)} {builder.GetId()} has {nameof(Layer)} that is null",
                    TErrorSeverity.Error);
            Placed = ob.Placed;
            if (Placed.TypeId.IsNull)
                MyDefinitionErrors.Add(builder.ModContext, $"{nameof(EdgePlacerBehaviorDefinition)} {builder.GetId()} has {nameof(Placed)} that is null",
                    TErrorSeverity.Error);
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