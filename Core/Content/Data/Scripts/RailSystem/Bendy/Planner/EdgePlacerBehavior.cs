using System;
using System.Collections.Generic;
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
using VRage.Components.Entity.Camera;
using VRage.Entities.Gravity;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.GUI.Crosshair;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Equipment;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy.Planner
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_EdgePlacerBehaviorDefinition))]
    public partial class EdgePlacerBehavior : MyToolBehaviorBase
    {
        protected override bool ValidateTarget()
        {
            return true;
        }

        private const float EdgeWidth = 0.05f;
        private const float NodeWidth = 0.01f;
        private static readonly Vector4 EdgeColor = new Vector4(0, 0, 1, 0.1f);
        private static readonly Vector4 EdgeColorBad = new Vector4(1, 0, 0, 0.1f);
        private static readonly Vector4 NodeColor = new Vector4(0, 0, 1, 0.1f);
        private static readonly MyStringId SquareMaterial = MyStringId.GetOrCompute("SquareIgnoreDepth");

        private const float NodeMarkerSize = 0.5f;
        private const float EdgeMarkerVertOffset = 0.325f;

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
            {
                Scene.Scheduler.AddFixedUpdate(Render);
                _edgePlacerControls.Push();
            }
        }

        private void NodesChanged(Node obj)
        {
            EdgePlacerSystem.AnnotateNodes(Graph, _vertices);
        }

        public override void Deactivate()
        {
            if (IsLocallyControlled)
                _edgePlacerControls.Pop();
            base.Deactivate();
            _vertices.Clear();
            Graph.NodeCreated -= NodesChanged;
            Graph.NodeMoved -= NodesChanged;
            Graph = null;
            Scene.Scheduler.RemoveFixedUpdate(Render);
            _hintInfo?.Hide();
            _hintInfo = null;

            _menu?.Close();
            _menu = null;
        }


        #region Rendering

        private void Render()
        {
            // Fix because OnTargetEntityChanged is only called when the actual entity changed.
            SetTarget();
            UpdateMenuContext();

            var cam = MyCameraComponent.ActiveCamera;
            if (Graph == null || cam == null) return;
            var renderer = new Renderer(cam);

            Graph.Nodes.OverlapAllFrustum(ref renderer.DetailFrustum, (Node node, bool intersects) => renderer.DrawNode(node));

            DrawRemovals(ref renderer);

            if (Holder == null) return;

            DrawPlacementLegacy(ref renderer);
        }

        public static CubicSphericalCurve PrepareSphericalBez(MatrixD m1, MatrixD m2,
            float smoothness1 = CubicCurve.DefaultSmoothness, float smoothness2 = CubicCurve.DefaultSmoothness)
        {
            CurveExtensions.AlignFwd(ref m1, ref m2);
            return new CubicSphericalCurve(
                MyGamePruningStructureSandbox.GetClosestPlanet(m1.Translation)?.PositionComp.WorldVolume.Center ??
                Vector3D.Zero, m1, m2, smoothness1, smoothness2);
        }

        private static CubicCurve PrepareNormalBez(MatrixD m1, MatrixD m2)
        {
            CurveExtensions.AlignFwd(ref m1, ref m2);
            return new CubicCurve(m1, m2);
        }

        #endregion

        private readonly List<string> _tmpMessages = new List<string>();

        private const float ClickDistSq = 25 * 25;

        private Vector3D LookingAtPosition
        {
            get
            {
                var target = Target;
                var myPosition = Holder.GetPosition();
                var caster = Holder.Get<MyCharacterDetectorComponent>();
                Vector3D pos;
                if (Target.Entity != null && Vector3D.DistanceSquared(myPosition, target.Position) < ClickDistSq)
                    pos = target.Position;
                else
                    pos = caster == null ? myPosition : caster.StartPosition + caster.Direction * 2;

                // Snap to targeted bendy edge.
                if (TryGetBendyTarget(out _, out var bendy) && bendy.Edges != null)
                {
                    var originalPos = pos;
                    double bestDistSq = RailConstants.NodeRoughDistanceSq;
                    foreach (var edge in bendy.Edges)
                        if (edge.Curve != null)
                        {
                            var curve = edge.Curve;
                            var localPos = Vector3D.Transform(in originalPos, edge.Transform);
                            float t0 = 0, t1 = 1;
                            CurveExtensions.NearestPoint(curve, localPos, 16, ref t0, ref t1);
                            var snapPos = curve.Sample((t0 + t1) / 2);
                            Vector3D.DistanceSquared(ref localPos, ref snapPos, out var distSq);
                            if (distSq >= bestDistSq) continue;
                            bestDistSq = distSq;
                            pos = snapPos;
                        }
                }

                // Apply vertical shift.
                pos -= _verticalShift * Vector3.Normalize(MyGravityProviderSystem.CalculateTotalGravityInPoint(pos));

                // Snap to nearest bendy node.
                var snap = Graph.GetNode(pos, roughMatch: true);
                if (snap != null)
                    pos = snap.Position;
                return pos;
            }
        }

        private readonly struct TemporaryVertex : IDisposable
        {
            private readonly EdgePlacerBehavior _behavior;
            private readonly bool _added;

            public TemporaryVertex(EdgePlacerBehavior be)
            {
                _behavior = be;
                _added = _behavior.TryAddVertex();
            }

            public void Dispose()
            {
                if (_added)
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
                    return StartRemoval(player);
                case MyHandItemActionEnum.Primary:
                    return StartPlacementLegacy(player);
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
                        HandlePlaceLegacy();
                        return;
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

            using (var hint = new HintToken(this))
            {
                if (!TargetIsStatic)
                {
                    hint.Text.AppendLine("Can't place node on dynamic physics");
                    return;
                }

                using (new TemporaryVertex(this))
                    if (_vertices.Count >= 2)
                    {
                        var jointData = EdgePlacerSystem.ComputeEdgeParameters(_vertices[_vertices.Count - 2], _vertices[_vertices.Count - 1]);
                        hint.Text.Append($"Length {jointData.Length:F1} m");
                        if (PlacedDefinition != null && jointData.Length > PlacedDefinition.Distance.Max)
                            hint.Text.AppendLine($" >= {PlacedDefinition.Distance.Max:F1} m");
                        else if (PlacedDefinition != null && jointData.Length < PlacedDefinition.Distance.Min)
                            hint.Text.AppendLine($" <= {PlacedDefinition.Distance.Min:F1} m");
                        else
                            hint.Text.AppendLine();
                        if (jointData.BendRadians.HasValue)
                        {
                            var b = jointData.BendRadians.Value;
                            hint.Text.Append($"Curve {b * 180 / Math.PI:F0}º");
                            if (PlacedDefinition != null && b > PlacedDefinition.MaxAngleRadians)
                                hint.Text.AppendLine($" >= {PlacedDefinition.MaxAngleDegrees}º");
                            else
                                hint.Text.AppendLine();
                        }

                        if (jointData.Grade.HasValue)
                        {
                            var g = jointData.Grade.Value;
                            hint.Text.Append($"Grade {g * 100:F0}%");
                            if (PlacedDefinition != null && Math.Abs(g) > PlacedDefinition.MaxGradeRatio)
                                hint.Text.AppendLine($" {(g < 0 ? "<= -" : ">= ")}{PlacedDefinition.MaxGradeRatio * 100:F0}%");
                            else
                                hint.Text.AppendLine();
                        }
                    }
            }
        }

        private readonly struct HintToken : IDisposable
        {
            private readonly IMyHudNotification _notification;
            public readonly StringBuilder Text;

            public HintToken(EdgePlacerBehavior owner)
            {
                if (owner._hintInfo == null)
                    owner._hintInfo = MyAPIGateway.Utilities.CreateNotification("");
                _notification = owner._hintInfo;
                Text = new StringBuilder();
            }

            public void Dispose()
            {
                _notification.Text = Text.ToString();
                if (Text.Length > 0)
                    _notification.Show(); // resets alive time + adds to queue if it's not in it
            }
        }

        private bool TryGetBendyTarget(out MyEntity entity, out BendyComponent target)
        {
            entity = Target.Entity?.Components.Get<BendyShapeProxy>()?.Owner ?? Target.Entity;
            target = null;
            return entity != null && entity.Components.TryGet(out target) && target.Graph == Graph;
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
                EdgePlacerSystem.AnnotateNodes(epa.Graph, epa._vertices);
                M1 = first.Matrix;
                M2 = last.Matrix;
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
            _vertices.Add(first.TangentPin.HasValue ? first : CreateVertex(first.Position, (Vector3)data.Curve.SampleDerivative(0)));

            var time = 0f;
            var prev = data.Curve.Sample(0);
            var prevTangent = data.Curve.SampleDerivative(0);
            var lengthElapsed = 0d;
            const float timeStep = .001f;
            for (var i = 1; i < data.Count; i++)
            {
                while (lengthElapsed < lenPerCount * i)
                {
                    time += timeStep;
                    var curr = data.Curve.Sample(time);
                    var currTangent = data.Curve.SampleDerivative(time);
                    lengthElapsed += Vector3D.Distance(prev, curr);
                    prev = curr;
                    prevTangent = currTangent;
                }

                _vertices.Add(CreateVertex(prev, (Vector3)prevTangent));
            }

            _vertices.Add(last.TangentPin.HasValue ? last : CreateVertex(last.Position, (Vector3)data.Curve.SampleDerivative(1)));
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
                MyAPIGateway.Session.IsAdminModeEnabled(player.SteamUserId) ||
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
                yield return "Press [KEY:ToolPrimary] to place";
            if (ValidateDeconstruct(Target.Entity, out _, true))
                yield return "Press [KEY:ToolSecondary] to remove";
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