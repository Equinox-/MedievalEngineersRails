using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Library.Collections;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy.Planner
{
    public partial class EdgePlacerBehavior
    {
        private readonly HashSet<long> _remove = new HashSet<long>();
        private long _lastRemove;

        private bool StartRemoval(IMyPlayer player)
        {
            if (ValidateDeconstruct(Target.Entity, out var err, true))
                return true;
            if (!string.IsNullOrEmpty(err))
                player.ShowNotification(err, 2000, null,
                    new Vector4(1, 0, 0, 1));
            if (_vertices.Count > 0 && Vector3D.DistanceSquared(_vertices[_vertices.Count - 1].Position, Target.Position) < 1)
                _vertices.RemoveAt(_vertices.Count - 1);
            return false;
        }

        private void HandleRemove()
        {
            _vertices.Clear();
            if (!TryGetBendyTarget(out var entity, out var dynCon))
                return;
            if (!Modified)
            {
                _remove.Add(entity.EntityId);
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

        private void DrawRemovals(ref Renderer renderer)
        {
            foreach (var remove in _remove)
                if (Scene.TryGetEntity(remove, out var removeEnt) && removeEnt.Components.TryGet(out BendyComponent bendy))
                    foreach (var edge in bendy.Edges)
                        renderer.DrawCurve(edge.Curve, Color.Red);

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
                                renderer.DrawCurve(edge.Curve, Color.Red);
                        }
                    }
                }
            }
        }
    }
}