using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.WorldEnvironment;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util
{
    public static class FarmingExtensions
    {
        private static readonly List<MyEntity> _working = new List<MyEntity>();

        public static void DisableItemsIn(OrientedBoundingBoxD box)
        {
            var worldBox = box.GetAABB();

            foreach (var vox in MyEntities.GetEntitiesInAABB(ref worldBox).OfType<MyVoxelBase>())
                if (vox.Hierarchy != null)
                {
                    _working.Clear();
                    vox.Hierarchy.QueryAABB(ref worldBox, _working);
                    foreach (var k in _working.OfType<MyEnvironmentSector>())
                    {
                        var tmp = box;
                        k.DisableItemsInObb(ref tmp);
                    }

                    _working.Clear();
                }
        }
        
        public static void DisableItemsIn(BoundingBoxD box)
        {
            foreach (var vox in MyEntities.GetEntitiesInAABB(ref box).OfType<MyVoxelBase>())
                if (vox.Hierarchy != null)
                {
                    _working.Clear();
                    vox.Hierarchy.QueryAABB(ref box, _working);
                    foreach (var k in _working.OfType<MyEnvironmentSector>())
                    {
                        var tmp = box;
                        k.DisableItemsInAabb(ref tmp);
                    }
                    _working.Clear();
                }
        }
    }
}