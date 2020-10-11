using System.Xml.Serialization;
using Sandbox.ModAPI;
using VRage.Components.Interfaces;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.RailSystem.Definition
{
    [MyComponent(typeof(MyObjectBuilder_RailSwitchImpactProxyComponent))]
    public class RailSwitchImpactProxyComponent : MyEntityComponent, IMyDamageReceiver
    {
        public bool DoDamage(MyDamageInformation dmg)
        {
            if (dmg.Type == MyDamageType.Bullet || dmg.Type == MyDamageType.Bolt)
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

            if (!DamageParent(dmg))
                return false;
            DamageTaken?.Invoke( dmg);
            return true;

        }

        private bool DamageParent(MyDamageInformation dmg)
        {
            var topMostParent = Entity.GetTopMostParent();
            if (topMostParent == null || topMostParent == Entity) return false;
            var parentReceiver = topMostParent.Get<IMyDamageReceiver>() ?? (topMostParent as IMyDamageReceiver);

            return parentReceiver != null && parentReceiver.DoDamage(dmg);
        }

        public event DamageTakenDelegate DamageTaken;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailSwitchImpactProxyComponent : MyObjectBuilder_EntityComponent
    {
    }
}