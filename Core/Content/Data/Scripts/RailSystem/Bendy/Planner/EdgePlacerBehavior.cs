using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Bendy.Shape;
using Equinox76561198048419394.RailSystem.Construction;
using Equinox76561198048419394.RailSystem.Util;
using Equinox76561198048419394.RailSystem.Voxel;
using Medieval.Constants;
using Medieval.GameSystems;
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

        public static event Action<EdgePlacerBehavior, MyEntity> EntityAdded, EntityRemoved;

        private MatrixD? _prevNodeLocation;

        private MatrixD? PrevNodeLocation
        {
            get { return _prevNodeLocation; }
            set
            {
                var old = _prevNodeLocation;
                _prevNodeLocation = value;
                if (!IsLocallyControlled)
                    return;
                if (old.HasValue && !value.HasValue)
                    MySession.Static.Components.Get<MyUpdateComponent>().RemoveFixedUpdate(DrawConnection);
                else if (!old.HasValue && value.HasValue)
                    MySession.Static.Components.Get<MyUpdateComponent>().AddFixedUpdate(DrawConnection);
            }
        }

        private void DrawConnection()
        {
            if (PrevNodeLocation == null || Holder.PositionComp == null)
                return;
            var prevNodeValue = PrevNodeLocation.Value;
            var prevNodeActual = Graph.GetNode(prevNodeValue.Translation);
            if (prevNodeActual != null)
                prevNodeValue = MatrixD.CreateWorld(prevNodeActual.Position, prevNodeActual.Tangent, prevNodeActual.Up);

            var prevPos = prevNodeValue.Translation;
            var targetNode = Graph.GetNode(Holder.PositionComp.WorldMatrix.Translation);
            var nextPos = targetNode?.Position ?? Holder.PositionComp.WorldMatrix.Translation;

            var forward = nextPos - prevPos;

            MatrixD prev = MatrixD.CreateWorld(prevPos, prevNodeValue.Forward * Math.Sign(Vector3D.Dot(prevNodeValue.Forward, forward)), prevNodeValue.Up);
            MatrixD next;
            if (targetNode == null)
            {
                var npos = Holder.PositionComp.WorldMatrix.Translation;
                var up = Vector3D.Normalize(-MyGravityProviderSystem.CalculateNaturalGravityInPoint(Target.Position));
                if (!up.IsValid())
                    up = Vector3D.Up;
                var tan = Vector3D.Normalize(forward);
                var aux = Vector3D.Cross(tan, up);
                var upReal = Vector3D.Cross(aux, tan);
                upReal.Normalize();
                next = MatrixD.CreateWorld(npos, tan, upReal);
            }
            else
            {
                next = MatrixD.CreateWorld(targetNode.Position, targetNode.Tangent * Math.Sign(Vector3D.Dot(targetNode.Tangent, forward)), targetNode.Up);
            }


            var lastPos = default(Vector3D);
            var color = _edgeColor;
            for (var t = 0; t <= 10; t++)
            {
                var pos = Bezier.BSpline(prev, next, t / 10f);
                var pact = pos.Translation + pos.Up * _edgeMarkerVertOffset;
                if (t > 0)
                    MySimpleObjectDraw.DrawLine(lastPos, pact, _squareMaterial, ref color, _edgeWidth);
                lastPos = pact;
            }
        }

        private const float _edgeWidth = 0.05f;
        private const float _nodeWidth = 0.01f;
        private static readonly Vector4 _edgeColor = new Vector4(0, 0, 1, 0.1f);
        private static readonly Vector4 _nodeColor = new Vector4(1, 0, 0, 0.1f);
        private static readonly MyStringId _squareMaterial = MyStringId.GetOrCompute("Square");

        private const float _nodeMarkerSize = 1;
        private const float _edgeMarkerVertOffset = 0.325f;

        private BendyLayer Graph { get; set; }
        private string Layer => Definition.Layer;
        private new EdgePlacerBehaviorDefinition Definition { get; set; }

        public override void Init(MyEntity holder, MyHandItem item, MyHandItemBehaviorDefinition definition)
        {
            base.Init(holder, item, definition);
            Definition = (EdgePlacerBehaviorDefinition) definition;
        }

        public override void Activate()
        {
            base.Activate();
            Graph = MySession.Static.Components.Get<BendyController>().GetOrCreateLayer(Layer);
            PrevNodeLocation = null;
            if (IsLocallyControlled)
                MySession.Static.Components.Get<MyUpdateComponent>().AddFixedUpdate(DrawNodes);
        }

        public override void Deactivate()
        {
            base.Deactivate();
            PrevNodeLocation = null;
            Graph = null;
            MySession.Static.Components.Get<MyUpdateComponent>().RemoveFixedUpdate(DrawNodes);
            _hintInfo?.Hide();
            _hintInfo = null;
        }

        private void DrawNodes()
        {
            var cam = MyCameraComponent.ActiveCamera;
            if (Graph == null || cam == null) return;
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

        protected override bool Start(MyHandItemActionEnum action)
        {
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
                return false;

            switch (action)
            {
                case MyHandItemActionEnum.Tertiary:
                    return true;
                case MyHandItemActionEnum.Secondary:
                    string tmp;
                    if (ValidateQuickDeconstruct(out tmp)) return true;
                    if (MyAPIGateway.Session.IsServerDecider())
                        player.ShowNotification(tmp, 2000, null, new Vector4(1, 0, 0, 1));
                    return false;
                case MyHandItemActionEnum.Primary:
                    if (ValidateSpacing() && ValidateTargetNode()) return true;
                    if (MyAPIGateway.Session.IsServerDecider())
                        player.ShowNotification(string.Join("\n", GetHints(false)), 2000, null, new Vector4(1, 0, 0, 1));
                    return false;
                case MyHandItemActionEnum.None:
                default:
                    return false;
            }
        }

        private bool ValidateQuickDeconstruct(out string msg, bool ignorePermission = false)
        {
            msg = null;
            if (!ignorePermission && !HasPermission(MyPermissionsConstants.QuickDeconstruct))
            {
                msg = "You don't have permission to quick deconstruct here";
                return false;
            }

            var entity = Target.Entity?.Components.Get<BendyShapeProxy>()?.Owner ?? Target.Entity;
            var dynCon = entity?.Components.Get<BendyDynamicComponent>();
            if (dynCon == null || dynCon.Edge?.Graph != Graph)
                return false;
            var constructionCon = entity?.Components.Get<ConstructableComponent>();
            // ReSharper disable once InvertIf
            if (constructionCon != null && (constructionCon.BuildIntegrity > 0 || !constructionCon.StockpileEmpty) && !MyAPIGateway.Session.IsCreative())
            {
                msg = "Can't quick deconstruct the rail segment here";
                return false;
            }

            return true;
        }

        private bool IsLocallyControlled => MySession.Static.PlayerEntity == Holder;

        private bool HasPermission(MyStringId id)
        {
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
                return false;
            return MyAreaPermissionSystem.Static == null || MyAreaPermissionSystem.Static.HasPermission(player.IdentityId, Target.Position, id);
        }

        private IMyHudNotification _hintInfo;

        protected override void Hit()
        {
            switch (ActiveAction)
            {
                case MyHandItemActionEnum.Tertiary:
                {
                    if (!IsLocallyControlled) return;
                    if (_hintInfo == null)
                        _hintInfo = MyAPIGateway.Utilities.CreateNotification("");
                    var sb = new StringBuilder();
                    foreach (var msg in GetHints(true))
                        sb.AppendLine(msg);
                    _hintInfo.Text = sb.ToString();
                    _hintInfo.Show(); // resets alive time + adds to queue if it's no in it
                    return;
                }
                case MyHandItemActionEnum.Secondary:
                {
                    string msg;
                    if (!ValidateQuickDeconstruct(out msg))
                    {
                        if (IsLocallyControlled)
                            MyAPIGateway.Utilities.ShowNotification(msg, 2000, null, new Vector4(1, 0, 0, 1));
                        return;
                    }

                    PrevNodeLocation = null;
                    var entity = Target.Entity?.Components.Get<BendyShapeProxy>()?.Owner ?? Target.Entity;
                    var dynCon = entity?.Components.Get<BendyDynamicComponent>();
                    if (dynCon == null || dynCon.Edge?.Graph != Graph)
                        return;
                    if (!MyAPIGateway.Session.IsServerDecider()) return;
                    entity.Close();
                    EntityRemoved?.Invoke(this, entity);
                    return;
                }
                case MyHandItemActionEnum.Primary:
                {
                    Vector3D up = Vector3D.Normalize(-MyGravityProviderSystem.CalculateNaturalGravityInPoint(Target.Position));
                    if (!up.IsValid())
                        up = Vector3D.Up;

                    if (MyAPIGateway.Session.IsServerDecider())
                    {
                        if (!ValidateTargetNode())
                            return;

                        var nextNode = Graph.GetOrCreateNode(Target.Position, up);
                        var prevNode = PrevNodeLocation.HasValue ? Graph.GetOrCreateNode(PrevNodeLocation.Value.Translation, PrevNodeLocation.Value.Up) : null;

                        if (prevNode != null && nextNode != null && Graph.GetEdge(prevNode, nextNode) == null)
                        {
                            var obContainer = new MyObjectBuilder_ComponentContainer();
                            ((ICollection<MyObjectBuilder_EntityComponent>) obContainer.Components).Add(new MyObjectBuilder_BendyDynamicComponent()
                            {
                                From = prevNode.Position,
                                FromUp = (Vector3) prevNode.Up,
                                To = nextNode.Position,
                                ToUp = (Vector3) nextNode.Up
                            });
                            var entOb = new MyObjectBuilder_EntityBase()
                            {
                                EntityDefinitionId = Definition.Placed,
                                PersistentFlags = MyPersistentEntityFlags2.InScene,
                                PositionAndOrientation = new MyPositionAndOrientation(prevNode.Position, (Vector3) prevNode.Tangent, (Vector3) prevNode.Up),
                                ComponentContainer = obContainer
                            };
                            var entity = MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(entOb);
                            if (MyAPIGateway.Session.IsCreative())
                            {
                                entity.Components.Get<ConstructableComponent>()?.InstallFromCreative();
                                ConstructableComponentDefinition.CcComponent test;
                                int test2;
                                entity.Components.Get<ConstructableComponent>()?.IncreaseIntegrity(1e9f, out test, out test2);
                            }

                            EntityAdded?.Invoke(this, entity);
                        }

                        PrevNodeLocation = nextNode?.Matrix ?? MatrixD.CreateWorld(Target.Position, Vector3.Normalize(Vector3D.Cross(up, up.Shifted())), up);
                    }
                    else
                        PrevNodeLocation = MatrixD.CreateWorld(Target.Position, Vector3.Normalize(Vector3D.Cross(up, up.Shifted())), up);

                    return;
                }
                case MyHandItemActionEnum.None:
                default:
                    return;
            }
        }

        private bool ValidateSpacing()
        {
            var pnode = PrevNodeLocation.HasValue ? Graph.GetNode(PrevNodeLocation.Value.Translation) : null;
            if (pnode == null)
                return true;
            var d = Vector3D.DistanceSquared(pnode.Position, Target.Position);
            if (d < Definition.Distance.Min * Definition.Distance.Min || d > Definition.Distance.Max * Definition.Distance.Max)
                return false;

            var prevHeight = pnode.Position.GetElevation();
            var currHeight = Target.Position.GetElevation();
            var elevationChange = Math.Abs(prevHeight - currHeight);
            // ReSharper disable once ConvertIfStatementToReturnStatement
            if (elevationChange > Definition.MaxElevationChange)
                return false;


            // ReSharper disable once UseNullPropagation
            {
                var prevPrevNode = pnode.Opposition(Target.Position);
                if (prevPrevNode != null)
                {
                    var p1 = Vector3D.Normalize(pnode.Position - prevPrevNode.Position);
                    var p2 = Vector3D.Normalize(Target.Position - pnode.Position);
                    var angle = Math.Acos(MathHelper.Clamp(Vector3D.Dot(p1, p2), 0, 1));
                    if (angle > Definition.MaxAngleRadians)
                        return false;
                }
            }

            var targetNode = Graph.GetNode(Target.Position);
            // ReSharper disable once InvertIf
            if (targetNode != null)
            {
                var nextNode = targetNode.Opposition(pnode);
                var p1 = Vector3D.Normalize(nextNode.Position - targetNode.Position);
                var p2 = Vector3D.Normalize(targetNode.Position - pnode.Position);
                var angle = Math.Acos(MathHelper.Clamp(Vector3D.Dot(p1, p2), 0, 1));
                if (angle > Definition.MaxAngleRadians)
                {
                    return false;
                }
            }

            return true;
        }

        private bool ValidateTargetNode(bool ignorePermissions = false)
        {
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (!ignorePermissions && !HasPermission(MyPermissionsConstants.Build))
                return false;
            var targetNode = Graph.GetNode(Target.Position);
            if (targetNode != null)
                return true;
            MyEntity ent = Target.Entity;
            while (ent != null && ent.Physics == null)
                ent = ent.Parent;
            return ent?.Physics != null && ent.Physics.IsStatic;
        }

        private IEnumerable<string> GetHints(bool allHints)
        {
            if (!HasPermission(MyPermissionsConstants.Build))
            {
                yield return $"You don't have permission to build here";
                yield break;
            }

            if (Target.Entity == null)
                yield break;
            var targetNode = Graph.GetNode(Target.Position);
            if (targetNode == null)
            {
                MyEntity ent = Target.Entity;
                while (ent != null && ent.Physics == null)
                    ent = ent.Parent;
                if (ent?.Physics == null || !ent.Physics.IsStatic)
                {
                    yield return "Cannot place a node on non-static physics";
                    if (!allHints)
                        yield break;
                }
            }

            var prevNode = PrevNodeLocation.HasValue ? Graph.GetNode(PrevNodeLocation.Value.Translation) : null;
            if (prevNode != null)
            {
                var d = Vector3D.Distance(prevNode.Position, Target.Position);
                if (d < Definition.Distance.Min)
                {
                    yield return $"Too close to previous node {d:F1} m < {Definition.Distance.Min:F1} m";
                    if (!allHints)
                        yield break;
                }

                if (d > Definition.Distance.Max)
                {
                    yield return $"Too far from previous node {d:F1} m > {Definition.Distance.Max:F1} m";
                    if (!allHints)
                        yield break;
                }

                if (allHints && d >= Definition.Distance.Min && d <= Definition.Distance.Max)
                {
                    yield return $"Length {d:F1} m";
                }

                var prevHeight = prevNode.Position.GetElevation();
                var currHeight = Target.Position.GetElevation();
                var elevationChange = Math.Abs(prevHeight - currHeight);
// ReSharper disable once ConvertIfStatementToReturnStatement
                if (elevationChange > Definition.MaxElevationChange)
                {
                    yield return $"Too much elevation change {elevationChange:F1} m > {Definition.MaxElevationChange:F1} m";
                    if (!allHints)
                        yield break;
                }
                else if (allHints)
                {
                    yield return $"Elevation {elevationChange:F1} m";
                }

                var prevPrevNode = prevNode.Opposition(Target.Position);
                if (prevPrevNode != null)
                {
                    var p1 = Vector3D.Normalize(prevNode.Position - prevPrevNode.Position);
                    var p2 = Vector3D.Normalize(Target.Position - prevNode.Position);
                    var angle = Math.Acos(MathHelper.Clamp(Vector3D.Dot(p1, p2), 0, 1));
                    if (angle > Definition.MaxAngleRadians)
                    {
                        yield return $"Too curvy (prev), {angle * 180f / Math.PI:F1}º > {Definition.MaxAngleRadians * 180f / Math.PI:F1}º";
                        if (!allHints)
                            yield break;
                    }
                    else if (allHints)
                    {
                        yield return $"Curve (prev) {angle * 180f / Math.PI:F1}º";
                    }
                }
            }

            if (targetNode != null && prevNode != null)
            {
                var nextNode = targetNode.Opposition(prevNode);
                var p1 = Vector3D.Normalize(nextNode.Position - targetNode.Position);
                var p2 = Vector3D.Normalize(targetNode.Position - prevNode.Position);
                var angle = Math.Acos(MathHelper.Clamp(Vector3D.Dot(p1, p2), 0, 1));
                if (angle > Definition.MaxAngleRadians)
                {
                    yield return $"Too curvy (next), {angle * 180f / Math.PI:F1}º > {Definition.MaxAngleRadians * 180f / Math.PI:F1}º";
                    if (!allHints)
                        yield break;
                }
                else if (allHints)
                {
                    yield return $"Curve (next) {angle * 180f / Math.PI:F1}º";
                }
            }

            if (targetNode != null)
                yield return "Connect to existing node";
            else
                yield return "Connect to new node";
        }

        public override IEnumerable<string> GetHintTexts()
        {
            if (ValidateSpacing() && ValidateTargetNode())
                yield return "Press LMB to place";
            string tmp;
            if (ValidateQuickDeconstruct(out tmp))
                yield return "Press RMB to remove";
        }

        public override IEnumerable<MyCrosshairIconInfo> GetIconsStates()
        {
            if (Definition.CrosshairPrefix == null)
                yield break;

            if (ValidateSpacing() && ValidateTargetNode(true))
            {
                if (HasPermission(MyPermissionsConstants.Build))
                {
                    if (Definition.CrosshairPlace.HasValue)
                        yield return Definition.CrosshairPlace.Value;
                }
                else if (Definition.CrosshairPlaceNoPermission.HasValue)
                    yield return Definition.CrosshairPlaceNoPermission.Value;
            }

            if (_prevNodeLocation != null && Definition.CrosshairQuestion.HasValue)
                yield return Definition.CrosshairQuestion.Value;
            string tmp;
            // ReSharper disable once InvertIf
            if (ValidateQuickDeconstruct(out tmp, true))
            {
                if (HasPermission(MyPermissionsConstants.QuickDeconstruct))
                {
                    if (Definition.CrosshairRemove.HasValue)
                        yield return Definition.CrosshairRemove.Value;
                }
                else if (Definition.CrosshairRemoveNoPermission.HasValue)
                    yield return Definition.CrosshairRemoveNoPermission.Value;
            }
        }
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EdgePlacerBehaviorDefinition))]
    public class EdgePlacerBehaviorDefinition : MyToolBehaviorDefinition
    {
        public string Layer { get; private set; }
        public MyDefinitionId Placed { get; private set; }
        public ImmutableRange<float> Distance { get; private set; }
        public string CrosshairPrefix { get; private set; }
        public float MaxElevationChange { get; private set; }
        public float MaxAngleRadians { get; private set; }

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
            Placed = ob.Placed;
            CrosshairPrefix = ob.CrosshairPrefix;
            Distance = (ob.Distance ?? new MutableRange<float>(RailConstants.DefaultMinLength, RailConstants.DefaultMaxLength)).Immutable();
            MaxElevationChange = ob.MaxElevationChange ?? RailConstants.DefaultMaxElevationChange;
            MaxAngleRadians = (float) ((ob.MaxAngleDegrees ?? RailConstants.DefaultMaxAngleDegrees) * Math.PI / 180f);

            CrosshairPlace = Create("_Place", MyCrosshairIconInfo.IconPosition.TopLeftCorner);
            CrosshairPlaceNoPermission = Create("_Place_NoPermission", MyCrosshairIconInfo.IconPosition.TopLeftCorner);
            CrosshairQuestion = Create("_Question", MyCrosshairIconInfo.IconPosition.Center);
            CrosshairRemove = Create("_Remove", MyCrosshairIconInfo.IconPosition.TopRightCorner);
            CrosshairRemoveNoPermission = Create("_Remove_NoPermission", MyCrosshairIconInfo.IconPosition.TopRightCorner);
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

        [XmlElement]
        public MutableRange<float>? Distance;

        [XmlElement]
        public float? MaxElevationChange;

        [XmlElement]
        public float? MaxAngleDegrees;
    }
}