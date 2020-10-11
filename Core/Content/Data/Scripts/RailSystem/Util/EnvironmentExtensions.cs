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
            voxel.Hierarchy.QueryBounds(in worldBox, _working);
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
            voxel.Hierarchy.QueryBounds(in box, _working);
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


            using (PoolManager.Get(out List<MyEntity> entities))
            {
                MyEntities.GetTopMostEntitiesInBox(ref worldBox, entities);
                foreach (var entity in entities)
                    (entity as MyVoxelBase)?.DisableFarmingItemsIn(box);
            }
        }
    }
}