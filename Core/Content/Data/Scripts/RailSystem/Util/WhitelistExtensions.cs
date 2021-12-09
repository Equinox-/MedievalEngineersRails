using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
using Medieval.Entities.Components.Quests.Conditions;
using Medieval.GameSystems;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util
{
    public static class WhitelistExtensions
    {
        public static string DisplayNameOf(this MyDefinitionBase def)
        {
            var vdef = def as MyVisualDefinitionBase;
            return vdef != null ? vdef.DisplayNameText : def.Id.SubtypeName;
        }

        public static bool IsCreative(this IMyPlayer player)
        {
            return MyAPIGateway.Session.CreativeMode ||  MyAPIGateway.Session.IsAdminModeEnabled(player.IdentityId);
        }

        public static IMyPlayer Player(this MyQuestConditionBase quest)
        {
            return MyAPIGateway.Players.GetPlayerControllingEntity(quest.Owner?.Entity);
        }

        public static bool IsServerDecider(this IMySession session)
        {
            return MyMultiplayerModApi.Static.IsServer;
        }

        public static bool HasPermission(this IMyPlayer player, Vector3D location, MyStringId id)
        {
            return MyAreaPermissionSystem.Static == null || MyAreaPermissionSystem.Static.HasPermission(player.IdentityId, location, id);
        }
    }
}