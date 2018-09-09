using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Bendy.Shape;
using Equinox76561198048419394.RailSystem.Util;
using Medieval.Constants;
using Medieval.GameSystems;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.Entities.Inventory;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.ModAPI;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Equipment;
using VRage.ObjectBuilders.Definitions.Inventory;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Construction
{
    // TODO MAKE IT WORK ON NORMAL BLOCKS
    [MyHandItemBehavior(typeof(MyObjectBuilder_ConstructorBehaviorDefinition))]
    public class ConstructorBehavior : MyToolBehaviorBase
    {
        public MyEntity Owner => Holder;
        
        public delegate void DelConstructed(ConstructorBehavior behavior, ConstructableComponent target, float before, float after);

        public static event DelConstructed OnConstructed;
        
        private ConstructableComponent GetConstructableComponent()
        {
            var test = Target.Entity;
            while (test != null)
            {
                var r = test.Components.Get<ConstructableComponent>();
                if (r != null)
                    return r;
                test = test.Components.Get<BendyShapeProxy>()?.Owner;
            }

            return null;
        }

        private IEnumerable<IConstructionPrereq> Prerequisites()
        {
            var test = Target.Entity;
            while (test != null)
            {
                foreach (var req in test.Components.OfType<IConstructionPrereq>())
                    yield return req;
                test = test.Components.Get<BendyShapeProxy>()?.Owner;
            }
        }

        protected override bool ValidateTarget()
        {
            return GetConstructableComponent() != null;
        }

        private MyInventoryBase _destinationInventory;
        private readonly List<MyInventoryBase> _sourceInventories = new List<MyInventoryBase>();

        protected override bool Start(MyHandItemActionEnum action)
        {
            _sourceInventories.Clear();
            _destinationInventory = null;

            var constructable = GetConstructableComponent();
            if (constructable == null)
                return false;

            if (ActiveAction == MyHandItemActionEnum.Primary)
                foreach (var prereq in Prerequisites())
                    if (!prereq.IsComplete)
                    {
                        MyAPIGateway.Utilities.ShowNotification(prereq.IncompleteMessage, 2000, null, new Vector4(1, 0, 0, 1));
                        return false;
                    }

            IEnumerable<MyInventoryBase> components = Holder.Components.GetComponents<MyInventoryBase>();
            if (components == null)
                return false;
            _sourceInventories.AddRange(components);
            _destinationInventory = Holder.GetInventory(MyCharacterConstants.MainInventory);
            if (MyAPIGateway.Session.CreativeMode)
                return true;
            switch (ActiveAction)
            {
                case MyHandItemActionEnum.Primary:
                    return _sourceInventories.Count > 0;
                case MyHandItemActionEnum.Secondary:
                    return _destinationInventory != null;
                default:
                    return false;
            }
        }

        protected override void Hit()
        {
            if (!MyAPIGateway.Session.IsServerDecider())
                return;

            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
                return;

            var constructable = GetConstructableComponent();
            if (constructable == null)
                return;

            int messageTime = 0;
            switch (ActiveAction)
            {
                case MyHandItemActionEnum.Primary:
                {
                    messageTime = (int) ((Definition.Effects[0]?.AnimationEndMilliseconds ?? 1000) * 1.5);

                    if (!HasPermission(MyPermissionsConstants.Repair))
                    {
                        player.ShowNotification($"You cannot repair here!", messageTime, null, new Vector4(1, 0, 0, 1));
                        return;
                    }

                    var before = constructable.BuildIntegrity;
                    var dt = Definition.Efficiency;// to match vanilla * (Definition.Effects[0]?.AnimationEndMilliseconds ?? 1000f) / 1000f;
                    if (player.IsCreative())
                        constructable.InstallFromCreative();
                    else
                        constructable.InstallFrom(_sourceInventories);
                    ConstructableComponentDefinition.CcComponent required;
                    int requiredCount;
                    UpdateDurability(-1);
                    constructable.IncreaseIntegrity(dt, out required, out requiredCount);
                    if (requiredCount > 0)
                    {
                        string name = null;
                        // ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
                        if (required.Required.Type == typeof(MyObjectBuilder_ItemTagDefinition).Name)
                            name = MyDefinitionManager.Get<MyItemTagDefinition>(required.Required)?.DisplayNameOf();
                        else
                            name = MyDefinitionManager.Get<MyInventoryItemDefinition>(required.Required)?.DisplayNameOf();
                        name = name ?? required.Required.Subtype;
                        player.ShowNotification($"Requires {requiredCount} {name}", 2000, null, new Vector4(1, 0, 0, 1));
                    }
                    OnConstructed?.Invoke(this, constructable, before, constructable.BuildIntegrity);
                    break;
                }
                case MyHandItemActionEnum.Secondary:
                {
                    messageTime = (int) ((Definition.Effects[1]?.AnimationEndMilliseconds ?? 1000) * 1.5);

                    if (!HasPermission(MyPermissionsConstants.Deconstruct))
                    {
                        player.ShowNotification($"You cannot deconstruct here!", messageTime, null, new Vector4(1, 0, 0, 1));
                        return;
                    }

                    var before = constructable.BuildIntegrity;
                    var dt = Definition.Efficiency;// to match vanilla * (Definition.Effects[1]?.AnimationEndMilliseconds ?? 1000f) / 1000f;
                    if (!HasPermission(MyPermissionsConstants.QuickDeconstruct))
                        dt *= 0.3333333f;
                    UpdateDurability(-1);
                    constructable.DecreaseIntegrity(dt);
                    constructable.UninstallTo(_destinationInventory);

                    if (constructable.BuildIntegrity <= 0)
                    {
                        constructable.UninstallAndDrop();
                        constructable.Entity.Close();
                    }
                    
                    OnConstructed?.Invoke(this, constructable, before, constructable.BuildIntegrity);
                    break;
                }
                case MyHandItemActionEnum.None:
                case MyHandItemActionEnum.Tertiary:
                default:
                    break;
            }

            player.ShowNotification($"{constructable.BuildPercent * 100:F0} %", messageTime);
        }

        public bool HasPermission(MyStringId id)
        {
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
                return false;
            return MyAreaPermissionSystem.Static == null || MyAreaPermissionSystem.Static.HasPermission(player.IdentityId, Target.Position, id);
        }
    }

    [MyDefinitionType(typeof(MyObjectBuilder_ConstructorBehaviorDefinition))]
    public class ConstructorBehaviorDefinition : MyToolBehaviorDefinition
    {
        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_ConstructorBehaviorDefinition) builder;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_ConstructorBehaviorDefinition : MyObjectBuilder_ToolBehaviorDefinition
    {
    }
}