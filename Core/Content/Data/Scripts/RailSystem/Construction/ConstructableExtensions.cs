using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game.ModAPI;

namespace Equinox76561198048419394.RailSystem.Construction
{
    public static class ConstructableExtensions
    {
        public static float GetBuildRatio(this MyEntity e)
        {
            ConstructableComponent cc;
            if (e.Components.TryGet(out cc))
                return cc.BuildPercent;
            var block = e as MyCubeBlock;
            if (block != null)
                return ((IMySlimBlock) block.SlimBlock).BuildLevelRatio;
            return 1f;
        }
    }
}