using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
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

        public static bool IsCreative(this IMySession session)
        {
            return session.EnableCopyPaste;
        }

        public static bool IsServerDecider(this IMySession session)
        {
            return MyAPIGateway.Multiplayer?.IsServer ?? true;
        }

        public static bool HasPermission(this IMyPlayer player, Vector3D location, MyStringId id)
        {
            return MyAreaPermissionSystem.Static == null || MyAreaPermissionSystem.Static.HasPermission(player.IdentityId, location, id);
        }
    }

    // TODO VRage.ObjectBuilders.Definitions.* whitelist
    public struct MyObjectBuilder_VoxelMiningDefinition_MinedItem
    {
        public bool Equals(MyObjectBuilder_VoxelMiningDefinition_MinedItem other)
        {
            return string.Equals(Type, other.Type) && string.Equals(Subtype, other.Subtype);
        }

        public override bool Equals(object obj)
        {
            return obj is MyObjectBuilder_VoxelMiningDefinition_MinedItem && Equals((MyObjectBuilder_VoxelMiningDefinition_MinedItem) obj);
        }

        public override int GetHashCode()
        {
            // ReSharper disable NonReadonlyMemberInGetHashCode
            return ((Type != null) ? Type.GetHashCode() : 0) * 397 ^ ((Subtype != null) ? Subtype.GetHashCode() : 0);
            // ReSharper restore NonReadonlyMemberInGetHashCode
        }

        [XmlAttribute]
        public string Type;

        [XmlAttribute]
        public string Subtype;

        [XmlAttribute]
        public int Amount;
    }

    public class MyObjectBuilder_VoxelMiningDefinition_MiningDef
    {
        [XmlAttribute("Volume")]
        public int VolumeAttribute
        {
            get
            {
                int? volume = Volume;
                if (volume == null)
                {
                    return 64;
                }

                return volume.GetValueOrDefault();
            }
            set { Volume = new int?(value); }
        }

        protected bool Equals(MyObjectBuilder_VoxelMiningDefinition_MiningDef other)
        {
            return string.Equals(VoxelMaterial, other.VoxelMaterial);
        }

        public override bool Equals(object obj)
        {
            return !ReferenceEquals(null, obj) && (ReferenceEquals(this, obj) ||
                                                          (!(obj.GetType() != GetType()) && Equals((MyObjectBuilder_VoxelMiningDefinition_MiningDef) obj)));
        }

        public override int GetHashCode()
        {
            // ReSharper disable once NonReadonlyMemberInGetHashCode
            return VoxelMaterial?.GetHashCode() ?? 0;
        }

        [XmlAttribute]
        public string VoxelMaterial;

        [XmlIgnore]
        public int? Volume;

        [XmlElement("MinedItem")]
        [DefaultValue(null)]
//        public MyMergingList<MyObjectBuilder_VoxelMiningDefinition_MinedItem> MinedItems = new MyMergingList<MyObjectBuilder_VoxelMiningDefinition_MinedItem>();
        public List<MyObjectBuilder_VoxelMiningDefinition_MinedItem> MinedItems = new List<MyObjectBuilder_VoxelMiningDefinition_MinedItem>();
    }
}