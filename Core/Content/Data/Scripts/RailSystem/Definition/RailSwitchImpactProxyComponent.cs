using System.Xml.Serialization;
using Sandbox.ModAPI;
using VRage.Components.Interfaces;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394.RailSystem.Definition
{
    [MyComponent(typeof(MyObjectBuilder_RailSwitchImpactProxyComponent))]
    public class RailSwitchImpactProxyComponent : MyEntityComponent, IMyDamageReceiver
    {
        public bool DoDamage(float damage, MyStringHash damageSource, bool sync, MyHitInfo? hitInfo = null, long attackerId = 0)
        {
            if (damageSource == MyDamageType.Bullet || damageSource == MyDamageType.Bolt)
            {
                var parentSwitcher = Entity.Hierarchy.Parent?.Container?.Get<IRailSwitch>();
                if (parentSwitcher != null)
                {
                    if (MyMultiplayerModApi.Static.IsServer)
                        parentSwitcher.Switch();
                    (parentSwitcher as RailSwitchExternalComponent)?.FlagAnimationWarp();
                    return true;
                }
            }

            if (!DamageParent(damage, damageSource, sync, hitInfo, attackerId))
                return false;
            DamageTaken?.Invoke(Entity, new MyDamageInformation(false, damage, damageSource, attackerId));
            return true;

        }

        private bool DamageParent(float damage, MyStringHash damageSource, bool sync, MyHitInfo? hitInfo, long attackerId)
        {
            var topMostParent = Entity.GetTopMostParent();
            if (topMostParent == null || topMostParent == Entity) return false;
            var parentReceiver = topMostParent.Get<IMyDamageReceiver>() ?? (topMostParent as IMyDamageReceiver);

            return parentReceiver != null && parentReceiver.DoDamage(damage, damageSource, sync, hitInfo, attackerId);
        }

        public void Kill(MyStringHash damageSource, bool sync, long attackerId = 0)
        {
        }

        public event DamageTakenDelegate DamageTaken;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailSwitchImpactProxyComponent : MyObjectBuilder_EntityComponent
    {
    }
}