using System;
using System.Collections.Generic;
using Equinox76561198048419394.RailSystem.Bendy.Shape;
using Equinox76561198048419394.RailSystem.Construction;
using Equinox76561198048419394.RailSystem.Util;
using Medieval.Constants;
using Medieval.GameSystems;
using Sandbox.Definitions.Inventory;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Inventory;
using Sandbox.ModAPI;
using VRage;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Logging;
using VRage.Network;
using VRage.ObjectBuilders;
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
            public DefinitionIdBlit Placed;
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

        public static JointParameters ComputeJointParameters(Vector3D? prev, Vector3D here, Vector3D next)
        {
            var result = new JointParameters();

            var dirNext = next - here;
            result.Length = dirNext.Normalize();

            if (prev.HasValue)
            {
                var dirPrev = here - prev.Value;
                dirPrev.Normalize();
                result.BendRadians = Math.Acos(Math.Abs(dirPrev.Dot(dirNext)));
            }

            var planet = MyGamePruningStructure.GetClosestPlanet(here);
            // ReSharper disable once InvertIf
            if (planet?.PositionComp != null)
            {
                var center = planet.PositionComp.WorldVolume.Center;
                var elevationHere = Vector3D.Distance(here, center);
                var elevationNext = Vector3D.Distance(next, center);
                var deltaElevation = elevationNext - elevationHere;
                var grade = deltaElevation / result.Length;
                result.Grade = grade;
            }

            return result;
        }

        /// <summary>
        /// Verifies the given joint against the given definition's constraints.
        /// </summary>
        /// <param name="def">Constraint source</param>
        /// <param name="prev">Previous position, or null to skip angle check</param>
        /// <param name="here">Position of joint</param>
        /// <param name="next">Next position</param>
        /// <param name="errors">destination for all errors that occurred, or null</param>
        /// <returns>true if the joint is valid</returns>
        public static bool VerifyJoint(BendyComponentDefinition def, Vector3D? prev, Vector3D here, Vector3D next,
            IList<string> errors)
        {
            var jointData = ComputeJointParameters(prev, here, next);

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
        public static bool ValidatePath(BendyComponentDefinition def, BendyLayer layer, Vector3D[] nodes,
            IList<string> errors)
        {
            if (nodes.Length <= 1)
            {
                errors?.Add("Not enough points to form a path");
                return false;
            }

            if (nodes.Length >= RailConstants.MaxNodesPlaced)
            {
                errors?.Add($"Can't place more than {RailConstants.MaxNodesPlaced} nodes at once");
                return false;
            }

            Vector3D? prevPos = null;
            for (var i = 0; i < nodes.Length; i++)
            {
                var here = layer?.GetNode(nodes[i]);
                var herePos = here?.Position ?? nodes[i];
                if (i > 0)
                {
                    var nextPos = i + 1 < nodes.Length ? nodes[i + 1] : here?.Opposition(nodes[i - 1])?.Position;
                    if (nextPos.HasValue && !VerifyJoint(def, prevPos, herePos, nextPos.Value, errors) && errors == null)
                        return false;
                }

                prevPos = herePos;
            }

            return errors == null || errors.Count == 0;
        }

        public static void RaisePlaceEdge(EdgePlacerConfig cfg, Vector3D[] segments)
        {
            MyMultiplayerModApi.Static.RaiseStaticEvent(x => PlaceEdge, cfg, segments);
        }

        [Event]
        [Server]
        private static void PlaceEdge(EdgePlacerConfig cfg, Vector3D[] segments)
        {
            MyEntity holderEntity;
            MyEntities.TryGetEntityById(cfg.EntityPlacing, out holderEntity);
            var holderPlayer = holderEntity != null ? MyAPIGateway.Players.GetPlayerControllingEntity(holderEntity) : null;

            var def = DefinitionFor(cfg.Placed);
            if (def == null)
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
                    MyLog.Default.Warning(
                        $"{holderPlayer} tried to place {cfg.Placed}, but has no item that can place it");
                    return;
                }

                var layer = MySession.Static.Components.Get<BendyController>().GetOrCreateLayer(def.Layer);
                var tmp = new List<string>();
                if (!ValidatePath(def, layer, segments, tmp))
                {
                    holderPlayer.ShowNotification(string.Join("\n", tmp));
                    MyEventContext.ValidationFailed();
                    return;
                }
            }

            #endregion


            var graph = MySession.Static.Components.Get<BendyController>().GetOrCreateLayer(def.Layer);

            for (var i = 1; i < segments.Length; i++)
            {
                var nextNode = graph.GetOrCreateNode(segments[i - 1]);
                var prevNode = graph.GetOrCreateNode(segments[i]);

                if (graph.GetEdge(prevNode, nextNode) != null)
                    continue;

                var obContainer = new MyObjectBuilder_ComponentContainer();
                var worldMatrix = MatrixD.CreateWorld((prevNode.Position + nextNode.Position) / 2,
                    Vector3D.Normalize(nextNode.Position - prevNode.Position),
                    Vector3D.Normalize(nextNode.Up + prevNode.Up));
                var worldMatrixInv = MatrixD.Invert(worldMatrix);
                ((ICollection<MyObjectBuilder_EntityComponent>) obContainer.Components).Add(
                    new MyObjectBuilder_BendyComponent()
                    {
                        Overrides = new[]
                        {
                            new MyObjectBuilder_BendyComponent.NodePose
                            {
                                Index = 0,
                                Position = (Vector3) Vector3D.Transform(prevNode.Position, worldMatrixInv),
                                Up = (Vector3) Vector3D.Transform(prevNode.Up, worldMatrixInv)
                            },
                            new MyObjectBuilder_BendyComponent.NodePose
                            {
                                Index = 1,
                                Position = (Vector3) Vector3D.Transform(nextNode.Position, worldMatrixInv),
                                Up = (Vector3) Vector3D.Transform(nextNode.Up, worldMatrixInv)
                            }
                        }
                    });
                var entOb = new MyObjectBuilder_EntityBase()
                {
                    EntityDefinitionId = (MyDefinitionId) cfg.Placed,
                    PersistentFlags = MyPersistentEntityFlags2.InScene,
                    PositionAndOrientation = new MyPositionAndOrientation(worldMatrix),
                    SubtypeName = cfg.Placed.SubtypeId.String,
                    ComponentContainer = obContainer
                };
                var entity = MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(entOb);
                if (holderPlayer != null && holderPlayer.IsCreative())
                {
                    entity.Components.Get<ConstructableComponent>()?.InstallFromCreative();
                    ConstructableComponentDefinition.CcComponent test;
                    int test2;
                    entity.Components.Get<ConstructableComponent>()
                        ?.IncreaseIntegrity(1e9f, out test, out test2);
                }

                entity.Components.Get<BendyPhysicsComponent>()?.DestroyEnvItems();

                EntityAdded?.Invoke(holderEntity, holderPlayer, entity);
            }
        }

        public static void RaiseRemoveEdge(long removerEntityId, long entityIdToRemove)
        {
            MyMultiplayerModApi.Static.RaiseStaticEvent(x => RemoveEdge, removerEntityId, entityIdToRemove);
        }

        [Event]
        [Server]
        private static void RemoveEdge(long removerEntityId, long entityIdToRemove)
        {
            MyEntity holderEntity;
            MyEntities.TryGetEntityById(removerEntityId, out holderEntity);
            var holderPlayer = holderEntity != null
                ? MyAPIGateway.Players.GetPlayerControllingEntity(holderEntity)
                : null;

            MyEntity removeEntity = null;
            MyEntities.TryGetEntityById(entityIdToRemove, out removeEntity);

            if (removeEntity == null)
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

                if (MyAreaPermissionSystem.Static != null && !MyAreaPermissionSystem.Static.HasPermission(
                        holderPlayer.IdentityId, removeEntity.GetPosition(), MyPermissionsConstants.QuickDeconstruct))
                {
                    holderPlayer.ShowNotification("You cannot quick deconstruct here", 2000, null,
                        new Vector4(1, 0, 0, 1));
                    return;
                }

                string err;
                if (!ValidateQuickRemove(holderPlayer, removeEntity, out err))
                {
                    MyEventContext.ValidationFailed();
                    if (!string.IsNullOrEmpty(err))
                        holderPlayer.ShowNotification(err, 2000, null, new Vector4(1, 0, 0, 1));
                    return;
                }
            }

            #endregion

            var block = removeEntity as MyCubeBlock;
            if (block != null)
            {
                var compound =
                    ((IMySlimBlock) block.CubeGrid.GetCubeBlock(block.Position))?.FatBlock as MyCompoundCubeBlock;
                if (compound != null && compound != block)
                {
                    var id = compound.GetBlockId(block.SlimBlock);
                    if (id.HasValue)
                        block.CubeGrid.RazeBlockInCompoundBlock(
                            new List<MyTuple<Vector3I, ushort>>
                            {
                                new MyTuple<Vector3I, ushort>(compound.Position, id.Value)
                            });
                }
                else
                    block.CubeGrid.RazeBlock(block.Position);
            }
            else
            {
                removeEntity.Close();
            }

            EntityRemoved?.Invoke(holderEntity, holderPlayer, removeEntity);
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

                var block = (IMySlimBlock) (removeEntity as MyCubeBlock)?.SlimBlock;
                if (block != null && (!block.StockpileEmpty || block.BuildIntegrity > 0))
                {
                    errMessage = "You cannot quick deconstruct built segments";
                    return false;
                }
            }

            errMessage = null;
            return true;
        }
    }
}