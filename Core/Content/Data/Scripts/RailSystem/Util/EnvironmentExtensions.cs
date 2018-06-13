using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.WorldEnvironment;
using VRage.Game.Entity;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util
{
    public static class FarmingExtensions
    {
        private static readonly List<MyEntity> _working = new List<MyEntity>();

        public static void DisableFarmingItemsIn(this MyVoxelBase voxel, OrientedBoundingBoxD box)
        {
            if (voxel.Hierarchy == null) return;
            var worldBox = box.GetAABB();
            _working.Clear();
            voxel.Hierarchy.QueryAABB(ref worldBox, _working);
            foreach (var entity in _working)
            {
                var sector = entity as MyEnvironmentSector;
                if (sector == null) continue;
                var tmp = box;
                sector.DisableItemsInObb(ref tmp);
            }

            _working.Clear();
        }
        
        public static void DisableFarmingItemsIn(this MyVoxelBase voxel, BoundingBoxD box)
        {
            if (voxel.Hierarchy == null) return;
            _working.Clear();
            voxel.Hierarchy.QueryAABB(ref box, _working);
            foreach (var entity in _working)
            {
                var sector = entity as MyEnvironmentSector;
                if (sector == null) continue;
                var tmp = box;
                sector.DisableItemsInAabb(ref tmp);
            }
            _working.Clear();
        }
        
        public static void DisableItemsIn(OrientedBoundingBoxD box)
        {
            var worldBox = box.GetAABB();

            foreach (var entity in MyEntities.GetEntitiesInAABB(ref worldBox))
                (entity as MyVoxelBase)?.DisableFarmingItemsIn(box);
        }
        
        public static void DisableItemsIn(BoundingBoxD box)
        {
            foreach (var entity in MyEntities.GetEntitiesInAABB(ref box))
                (entity as MyVoxelBase)?.DisableFarmingItemsIn(box);
        }
    }
}