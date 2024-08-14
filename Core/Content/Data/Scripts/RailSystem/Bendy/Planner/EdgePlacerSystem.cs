using System;
using System.Collections.Generic;
using Equinox76561198048419394.RailSystem.Bendy.Shape;
using Equinox76561198048419394.RailSystem.Construction;
using Equinox76561198048419394.RailSystem.Util;
using Medieval.Constants;
using Medieval.Entities.Components.Grid;
using Medieval.GameSystems;
using Sandbox.Definitions.Inventory;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Inventory;
using Sandbox.ModAPI;
using VRage;
using VRage.Components.Block;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Import;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Components;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy.Planner
{
    [StaticEventOwner]
    public static class EdgePlacerSystem
    {
        public delegate void DelEdgeModified(MyEntity holderEntity, IMyPlayer holderPlayer, MyEntity modifiedEntity);

        /// <summary>
        /// These events are not raised on clients!
        /// </summary>
        public static event DelEdgeModified EntityAdded, EntityRemoved;

        public struct EdgePlacerConfig
        {
            public long EntityPlacing;
            public SerializableDefinitionId Placed;
        }

        /// <summary>
        /// Bendy component definition for the given entity container ID
        /// </summary>
        /// <param name="entityDef">container ID</param>
        /// <returns>Definition, or null if none exists</returns>
        public static BendyComponentDefinition DefinitionFor(MyDefinitionId entityDef)
        {
            var containerDef = MyDefinitionManager.Get<MyContainerDefinition>(entityDef);
            if (containerDef?.Components == null)
                return null;

            foreach (var k in containerDef.Components)
            {
                var def = k.Definition as BendyComponentDefinition;
                if (def != null)
                    return def;
            }

            return null;
        }

        public struct JointParameters
        {
            public double Length;
            public double? BendRadians;
            public double? Grade;
        }

        public static JointParameters ComputeEdgeParameters(AnnotatedNode from, AnnotatedNode to)
        {
            var result = new JointParameters();

            var dirNext = to.Position - from.Position;
            result.Length = dirNext.Normalize();

            var dotFrom = Math.Abs(Vector3.Normalize(from.Tangent).Dot((Vector3)dirNext));
            var dotTo = Math.Abs(Vector3.Normalize(to.Tangent).Dot((Vector3)dirNext));

            result.BendRadians = Math.Acos(Math.Min(dotFrom, dotTo));

            var planet = MyGamePruningStructureSandbox.GetClosestPlanet(@from.Position);
            // ReSharper disable once InvertIf
            if (planet?.PositionComp != null)
            {
                var center = planet.PositionComp.WorldVolume.Center;
                var elevationA = Vector3D.Distance(from.Position, center);
                var elevationB = Vector3D.Distance(to.Position, center);
                var deltaElevation = elevationB - elevationA;
                var grade = deltaElevation / result.Length;
                result.Grade = grade;
            }

            return result;
        }

        /// <summary>
        /// Verifies the given joint against the given definition's constraints.
        /// </summary>
        /// <param name="def">Constraint source</param>
        /// <param name="from">Point one of the edge</param>
        /// <param name="to">Point two of the edge</param>
        /// <param name="errors">destination for all errors that occurred, or null</param>
        /// <returns>true if the joint is valid</returns>
        public static bool VerifyEdge(BendyComponentDefinition def, AnnotatedNode from, AnnotatedNode to, IList<string> errors = null)
        {
            var jointData = ComputeEdgeParameters(from, to);
            return VerifyEdge(def, jointData, errors);
        }

        public static bool VerifyEdge(BendyComponentDefinition def, JointParameters jointData, IList<string> errors = null)
        {
            if (jointData.Length > def.Distance.Max)
            {
                if (errors == null)
                    return false;
                errors.Add($"Too long {jointData.Length:F1} m >= {def.Distance.Max:F1} m");
            }
            else if (jointData.Length < def.Distance.Min)
            {
                if (errors == null)
                    return false;
                errors.Add($"Too short {jointData.Length:F1} m <= {def.Distance.Min:F1} m");
            }

            if (jointData.BendRadians.HasValue)
            {
                var angle = jointData.BendRadians.Value;
                if (angle > def.MaxAngleRadians)
                {
                    if (errors == null)
                        return false;
                    errors.Add($"Too curvy {angle * 180 / Math.PI:F0}º >= {def.MaxAngleDegrees:F0}º");
                }
            }

            // ReSharper disable once InvertIf
            if (jointData.Grade.HasValue)
            {
                var grade = jointData.Grade.Value;
                // ReSharper disable once InvertIf
                if (Math.Abs(grade) > def.MaxGradeRatio)
                {
                    if (errors == null)
                        return false;
                    errors.Add($"Too steep {grade * 100:F0}% {(grade < 0 ? "<= -" : ">= ")}{def.MaxGradeRatio * 100:F0}%");
                }
            }

            return errors == null || errors.Count == 0;
        }

        /// <summary>
        /// Verifies the given path against the given definition's constraints
        /// </summary>
        /// <param name="def">Constraint source</param>
        /// <param name="layer">Layer to use for node snapping, or null</param>
        /// <param name="nodes">Nodes in path</param>
        /// <param name="errors">Destination for all errors that occurred, or null</param>
        /// <returns>true if the path is valid</returns>
        public static bool ValidatePath(BendyComponentDefinition def, BendyLayer layer, IList<AnnotatedNode> nodes,
            IList<string> errors)
        {
            if (nodes.Count <= 1)
            {
                errors?.Add("Not enough points to form a path");
                return false;
            }

            if (nodes.Count >= RailConstants.MaxNodesPlaced)
            {
                errors?.Add($"Can't place more than {RailConstants.MaxNodesPlaced} nodes at once");
                return false;
            }

            for (var i = 1; i < nodes.Count; i++)
            {
                if (!VerifyEdge(def, nodes[i - 1], nodes[i], errors) && errors == null)
                    return false;
            }

            return errors == null || errors.Count == 0;
        }

        public struct AnnotatedNode
        {
            public Vector3D Position;
            public Vector3 Up;
            public Vector3 Tangent;
            public Node Existing;

            public bool Pinned => TangentPin.HasValue || Existing?.TangentPins > 0;
            public MatrixD Matrix => Existing != null && Existing.TangentPins != 0 ? Existing.Matrix : MatrixD.CreateWorld(Position, Tangent, Up);

            public Vector3? TangentPin;
        }

        public static AnnotatedNode[] AnnotateNodes(BendyLayer layer, Vector3D[] nodes, Vector3?[] tangents = null)
        {
            var res = new AnnotatedNode[nodes.Length];
            for (var i = 0; i < nodes.Length; i++)
                res[i] = new AnnotatedNode
                {
                    Position = nodes[i],
                    TangentPin = tangents?[i]
                };
            AnnotateNodes(layer, res);
            return res;
        }

        public static void AnnotateNodes(BendyLayer layer, IList<AnnotatedNode> nodes, bool create = false)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                var tmp = nodes[i];
                var here = create ? layer.GetOrCreateNode(tmp.Position) : layer?.GetNode(tmp.Position);
                tmp.Position = here?.Position ?? nodes[i].Position;
                tmp.Existing = here;
                tmp.Up = here?.Up ?? nodes[i].Up;
                nodes[i] = tmp;
            }

            // First pass: compute the tangents using neighbors.
            for (var i = 0; i < nodes.Count; i++)
            {
                var a = nodes[i];
                if (a.Existing?.TangentPins > 0)
                {
                    a.TangentPin = null;
                    a.Tangent = a.Existing.Tangent;
                    nodes[i] = a;
                    continue;
                }

                if (a.TangentPin != null)
                {
                    a.Tangent = a.TangentPin.Value;
                    nodes[i] = a;
                    continue;
                }

                var tanHere = Vector3.Zero;
                if (a.Existing != null)
                    tanHere = a.Existing.Tangent * a.Existing.Neighbors.Count();

                if (i > 0 && (nodes[i - 1].Existing == null || a.Existing?.ConnectionTo(nodes[i - 1].Existing) == null))
                {
                    var tP = Vector3.Normalize(nodes[i - 1].Position - a.Position);
                    if (tP.Dot(tanHere) < 0)
                        tP = -tP;
                    tanHere += tP;
                }

                // ReSharper disable once InvertIf
                if (i + 1 < nodes.Count && (nodes[i + 1].Existing == null || a.Existing?.ConnectionTo(nodes[i + 1].Existing) == null))
                {
                    var tP = Vector3.Normalize(nodes[i + 1].Position - a.Position);
                    if (tP.Dot(tanHere) < 0)
                        tP = -tP;
                    tanHere += tP;
                }

                a.Tangent = Vector3.Normalize(tanHere.LengthSquared() > 0 ? tanHere : Vector3.CalculatePerpendicularVector(nodes[i].Up));
                nodes[i] = a;
            }

            // Second pass: compute tangents using circular arcs.
            if (nodes.Count >= 2)
            {
                TangentFromArc(0, 1);
                TangentFromArc(nodes.Count - 1, nodes.Count - 2);
            }

            return;

            void TangentFromArc(int atI, int otherI)
            {
                var at = nodes[atI];
                var other = nodes[otherI];
                if (at.Pinned || !other.Pinned || at.Existing?.Connections.Count > 0)
                    return;
                var normal = Vector3.Cross((Vector3) (other.Position - at.Position), at.Up);
                normal.Normalize();

                Vector3.Reflect(ref other.Tangent, ref normal, out var tangent);
                if (tangent.Normalize() >= 1e-6f)
                    at.Tangent = tangent;
                nodes[atI] = at;
            }
        }

        public static void RaisePlaceEdge(EdgePlacerConfig cfg, Vector3D[] segments, uint[] tangents = null)
        {
            MyMultiplayerModApi.Static.RaiseStaticEvent(x => PlaceEdge, cfg, segments, tangents ?? Array.Empty<uint>());
        }

        [Event]
        [Server]
        private static void PlaceEdge(EdgePlacerConfig cfg, Vector3D[] segments, uint[] tangents)
        {
            MyEntities.TryGetEntityById(cfg.EntityPlacing, out var holderEntity);
            var holderPlayer = holderEntity != null ? MyAPIGateway.Players.GetPlayerControllingEntity(holderEntity) : null;

            var def = DefinitionFor(cfg.Placed);
            if (def == null)
            {
                MyEventContext.ValidationFailed();
                return;
            }

            #region Validation

            var unpackedTangents = new Vector3?[segments.Length];
            for (var i = 0; i < Math.Min(segments.Length, tangents.Length); i++)
                unpackedTangents[i] = tangents[i] == 0 ? default(Vector3?) : VF_Packer.UnpackNormal(tangents[i]);

            var layer = MySession.Static.Components.Get<BendyController>().GetOrCreateLayer(def.Layer);
            var annotated = AnnotateNodes(layer, segments, unpackedTangents);

            if (!MyEventContext.Current.IsLocallyInvoked)
            {
                if (holderEntity == null || holderPlayer == null ||
                    MyEventContext.Current.Sender.Value != holderPlayer.SteamUserId)
                {
                    MyEventContext.ValidationFailed();
                    return;
                }

                if (MyAreaPermissionSystem.Static != null)
                    foreach (var pos in segments)
                    {
                        if (MyAreaPermissionSystem.Static.HasPermission(holderPlayer.IdentityId, pos,
                                MyPermissionsConstants.Build)) continue;
                        holderPlayer.ShowNotification("You cannot build here", 2000, null, new Vector4(1, 0, 0, 1));
                        MyEventContext.ValidationFailed();
                        return;
                    }

                var validPlacedType = false;
                foreach (var item in holderEntity.GetInventory(MyCharacterConstants.MainInventory).Items)
                {
                    if (item == null)
                        continue;
                    var itemDef =
                        MyDefinitionManager.Get<MyInventoryItemDefinition>(item.DefinitionId) as MyHandItemDefinition;
                    if (itemDef == null)
                        continue;
                    foreach (var behaviorDef in itemDef.Behaviors)
                    {
                        var placeDef = behaviorDef as EdgePlacerBehaviorDefinition;
                        if (placeDef == null || placeDef.Placed != cfg.Placed) continue;
                        validPlacedType = true;
                        break;
                    }

                    if (validPlacedType)
                        break;
                }

                if (!validPlacedType)
                {
                    MyEventContext.ValidationFailed();
                    MySession.Static.Log.Warning(
                        $"{holderPlayer} tried to place {cfg.Placed}, but has no item that can place it");
                    return;
                }

                var tmp = new List<string>();
                if (!ValidatePath(def, layer, annotated, tmp))
                {
                    holderPlayer.ShowNotification(string.Join("\n", tmp));
                    MyEventContext.ValidationFailed();
                    return;
                }
            }

            #endregion

            var graph = MySession.Static.Components.Get<BendyController>().GetOrCreateLayer(def.Layer);
            // Re-annotate nodes, this time creating the backing nodes.
            AnnotateNodes(graph, annotated, true);
            for (var i = 1; i < annotated.Length; i++)
            {
                var nextAnn = annotated[i - 1];
                var prevAnn = annotated[i];

                if (graph.GetEdge(prevAnn.Existing, nextAnn.Existing) != null)
                    continue;

                var obContainer = new MyObjectBuilder_ComponentContainer();
                var worldMatrix = MatrixD.CreateWorld((prevAnn.Position + nextAnn.Position) / 2,
                    Vector3D.Normalize(nextAnn.Position - prevAnn.Position),
                    Vector3D.Normalize(nextAnn.Up + prevAnn.Up));
                var worldMatrixInv = MatrixD.Invert(worldMatrix);
                ((ICollection<MyObjectBuilder_EntityComponent>)obContainer.Components).Add(
                    new MyObjectBuilder_BendyComponent()
                    {
                        Overrides = new[]
                        {
                            CreatePose(0, in prevAnn, ref worldMatrixInv),
                            CreatePose(1, in nextAnn, ref worldMatrixInv)
                        }
                    });
                var entOb = new MyObjectBuilder_EntityBase()
                {
                    EntityDefinitionId = (MyDefinitionId)cfg.Placed,
                    PersistentFlags = MyPersistentEntityFlags2.InScene,
                    PositionAndOrientation = new MyPositionAndOrientation(worldMatrix),
                    SubtypeName = cfg.Placed.SubtypeId,
                    ComponentContainer = obContainer
                };
                var entity = MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(entOb);
                if (holderPlayer != null && holderPlayer.IsCreative())
                {
                    entity.Components.Get<ConstructableComponent>()?.InstallFromCreative();
                    entity.Components.Get<ConstructableComponent>()?.IncreaseIntegrity(1e9f, out _, out _);
                }

                entity.Components.Get<BendyPhysicsComponent>()?.DestroyEnvItems();

                EntityAdded?.Invoke(holderEntity, holderPlayer, entity);
                continue;

                MyObjectBuilder_BendyComponent.NodePose CreatePose(uint index, in AnnotatedNode node, ref MatrixD worldMatrixInvCaptured) =>
                    new MyObjectBuilder_BendyComponent.NodePose
                    {
                        Index = index,
                        Position = (Vector3)Vector3D.Transform(node.Position, ref worldMatrixInvCaptured),
                        Up = (Vector3)Vector3D.TransformNormal(node.Up, ref worldMatrixInvCaptured),
                        Tangent = node.TangentPin.HasValue ? (Vector3?) (Vector3) Vector3D.TransformNormal(node.TangentPin.Value, ref worldMatrixInvCaptured) : null,
                    };
            }
        }

        public static void RaiseRemoveEdges(long removerEntityId, params long[] entityIdsToRemove)
        {
            MyMultiplayerModApi.Static.RaiseStaticEvent(x => RemoveEdge, removerEntityId, entityIdsToRemove);
        }

        [Event]
        [Server]
        private static void RemoveEdge(long removerEntityId, long[] entityIdToRemove)
        {
            MyEntity holderEntity;
            MyEntities.TryGetEntityById(removerEntityId, out holderEntity);
            var holderPlayer = holderEntity != null
                ? MyAPIGateway.Players.GetPlayerControllingEntity(holderEntity)
                : null;

            var removeEntities = new List<MyEntity>(entityIdToRemove.Length);
            foreach (var id in entityIdToRemove)
                if (MyEntities.TryGetEntityById(id, out var ent))
                    removeEntities.Add(ent);
            if (removeEntities.Count == 0)
            {
                MyEventContext.ValidationFailed();
                return;
            }

            #region Validation

            if (!MyEventContext.Current.IsLocallyInvoked)
            {
                if (holderEntity == null || holderPlayer == null ||
                    MyEventContext.Current.Sender.Value != holderPlayer.SteamUserId)
                {
                    MyEventContext.ValidationFailed();
                    return;
                }

                if (removeEntities.Count >= RailConstants.MaxNodesPlaced)
                {
                    holderPlayer.ShowNotification($"Can't remove more than {RailConstants.MaxNodesPlaced} entities at once", 2000, null,
                        new Vector4(1, 0, 0, 1));
                    MyEventContext.ValidationFailed();
                    return;
                }

                foreach (var removeEntity in removeEntities)
                {
                    if (MyAreaPermissionSystem.Static != null
                        && !MyAPIGateway.Session.IsAdminModeEnabled(holderPlayer.SteamUserId)
                        && !MyAreaPermissionSystem.Static.HasPermission(
                            holderPlayer.IdentityId, removeEntity.GetPosition(), MyPermissionsConstants.QuickDeconstruct))
                    {
                        holderPlayer.ShowNotification("You cannot quick deconstruct here", 2000, null,
                            new Vector4(1, 0, 0, 1));
                        return;
                    }

                    if (!ValidateQuickRemove(holderPlayer, removeEntity, out var err))
                    {
                        MyEventContext.ValidationFailed();
                        if (!string.IsNullOrEmpty(err))
                            holderPlayer.ShowNotification(err, 2000, null, new Vector4(1, 0, 0, 1));
                        return;
                    }
                }
            }

            #endregion

            foreach (var removeEntity in removeEntities)
            {
                var block = removeEntity.Get<MyBlockComponent>();
                if (block != null)
                    block.GridData.RemoveBlock(block.Block);
                else
                    removeEntity.Close();

                EntityRemoved?.Invoke(holderEntity, holderPlayer, removeEntity);
            }
        }

        public static bool ValidateQuickRemove(IMyPlayer holderPlayer, MyEntity removeEntity, out string errMessage)
        {
            var dynCon = removeEntity.Components.Get<BendyComponent>();
            if (dynCon == null)
            {
                errMessage = null;
                return false;
            }

            if (!holderPlayer.IsCreative())
            {
                var constructionCon = removeEntity.Components.Get<ConstructableComponent>();
                // ReSharper disable once InvertIf
                if (constructionCon != null && (constructionCon.BuildIntegrity > 0 || !constructionCon.StockpileEmpty))
                {
                    errMessage = "You cannot quick deconstruct built segments";
                    return false;
                }

                var block = removeEntity?.Get<MyBlockComponent>();
                if (block != null)
                {
                    var gbc = block.GridData.Container.Get<MyGridBuildingComponent>();
                    if (gbc == null)
                    {
                        errMessage = "You cannot quick deconstruct built segments";
                        return false;
                    }

                    var state = gbc.GetBlockState(block.BlockId);
                    if (state == null || state.BuildIntegrity > 0)
                    {
                        errMessage = "You cannot quick deconstruct built segments";
                        return false;
                    }
                }
            }

            errMessage = null;
            return true;
        }
    }
}