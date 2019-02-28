using Medieval.Entities.Components.Grid;
using VRage.Components.Block;
using VRage.Game.Entity;

namespace Equinox76561198048419394.RailSystem.Construction
{
    public static class ConstructableExtensions
    {
        public static float GetBuildRatio(this MyEntity e)
        {
            ConstructableComponent cc;
            if (e.Components.TryGet(out cc))
                return cc.BuildPercent;
            var block = e.Components.Get<MyBlockComponent>();
            if (block != null)
            {
                return block.GridData.Container.Get<MyGridBuildingComponent>()?.GetBlockState(block.BlockId)?.BuildIntegrity ?? 1f;
            }

            return 1f;
        }
    }
}