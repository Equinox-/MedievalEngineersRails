using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.RailSystem.Util;
using Medieval.Constants;
using Medieval.GameSystems;
using Sandbox.Definitions;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Inventory;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Definitions;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions;
using VRage.ObjectBuilders.Definitions.Equipment;
using VRage.Session;
using VRage.Systems;
using VRage.Utils;
using VRageMath;
using MessageUtil = Equinox76561198048419394.RailSystem.Util.MessageUtil;

namespace Equinox76561198048419394.RailSystem.Voxel
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_RailGraderBehaviorDefinition))]
    [StaticEventOwner]
    public class RailGraderBehavior : MyToolBehaviorBase
    {
        public MyEntity Owner => Holder;

        public delegate void DelGradeAction(RailGraderBehavior behavior, IReadOnlyCollection<RailGradeComponent> grades, uint added, uint removed);

        public static event DelGradeAction GraderUsed;

        public new RailGraderBehaviorDefinition Definition { get; private set; }

        public override void Init(MyEntity holder, MyHandItem handItem, MyHandItemBehaviorDefinition def)
        {
            base.Init(holder, handItem, def);
            Definition = (RailGraderBehaviorDefinition) def;
        }

        private Vector3D _cachedTargetDirection;

        protected override bool ValidateTarget()
        {
            if (Vector3D.DistanceSquared(_cachedTargetDirection, Target.Position) < 1)
                return _gradeComponents.Count > 0;

            GatherGradeComponents(Target.Position);

            _cachedTargetDirection = Target.Position;
            return _gradeComponents.Count > 0;
        }

        protected override bool Start(MyHandItemActionEnum action)
        {
            return _gradeComponents.Count != 0;
        }

        private readonly VoxelPlacementBuffer _placementBuffer = new VoxelPlacementBuffer();
        private readonly VoxelMiningBuffer _miningBuffer = new VoxelMiningBuffer();

        protected override void Hit()
        {
            if (!WhitelistExtensions.IsServerDecider(MyAPIGateway.Session))
                return;
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
                return;

            if (!HasPermission(MyPermissionsConstants.Mining))
            {
                MessageUtil.ShowNotification(player, "You don't have permission to terraform here.", 2000, null, new Vector4(1, 0, 0, 1));
                return;
            }

            bool isExcavating = ActiveAction == MyHandItemActionEnum.Primary;

            var radius = isExcavating ? Definition.ExcavateRadius : Definition.FillRadius;
            uint availableForDeposit;
            StringBuilder requiredMaterials = null;
            if (isExcavating)
            {
                availableForDeposit = 0;
            }
            else
            {
                availableForDeposit = Definition.FillVolume;
                var andHead = -1;
                var missing = 0;
                if (!WhitelistExtensions.IsCreative(player))
                {
                    var inv = Holder.GetInventory(MyCharacterConstants.MainInventory);
                    availableForDeposit = Math.Min(availableForDeposit, (uint) _placementBuffer.AvailableVolume(inv, Definition.FillMaterial));
                    if (availableForDeposit == 0)
                    {
                        foreach (var item in Definition.FillMaterial.Items)
                        {
                            var amount = inv.GetItemAmountFuzzy(item.Key);
                            if (amount >= item.Value) continue;
                            if (requiredMaterials == null)
                                requiredMaterials = new StringBuilder("You require ");
                            var itemDef = MyDefinitionManager.Get<MyInventoryItemDefinition>(item.Key);
                            andHead = requiredMaterials.Length;
                            missing++;
                            requiredMaterials.Append(itemDef?.DisplayNameOf() ?? item.Key.ToString()).Append(", ");
                        }
                    }
                }

                if (missing > 0 && requiredMaterials != null)
                {
                    if (andHead != -1 && missing >= 2)
                        requiredMaterials.Insert(andHead, "and ");
                    requiredMaterials.Remove(requiredMaterials.Length - 2, 2);
                }
            }

            var availableForExcavate = isExcavating ? Definition.ExcavateVolume : 0;

            uint totalDeposited;
            uint totalExcavated;
            bool triedToChange;
            bool intersectedDynamic;
            var system = MySession.Static.Components.Get<RailGraderSystem>();
            var result = system.DoGrading(_gradeComponents, Target.Position, radius, availableForDeposit,
                availableForExcavate, _miningBuffer, Definition.FillMaterial.Material.Index,
                out totalDeposited, out totalExcavated, 
                triedToChange: out triedToChange, intersectedDynamic: out intersectedDynamic,
                out var dynamicBoxes, out var voxel, out var voxelRadius);

            #region Give Items

            var ranOutOfInventorySpace = false;
            if (triedToChange && isExcavating && !WhitelistExtensions.IsCreative(player))
            {
                _miningBuffer.PutMaterialsInto(
                    Holder.GetInventory(MyCharacterConstants.MainInventory),
                    Definition.ExcavateDefinition);
                ranOutOfInventorySpace = _miningBuffer.DropMaterials(Target.Position, Definition.ExcavateDefinition);
            }

            if (ranOutOfInventorySpace)
                MessageUtil.ShowNotification(player, "Inventory is full", color: new Vector4(1, 0, 0, 1));

            #endregion

            #region Take Items

            if (!WhitelistExtensions.IsCreative(player))
            {
                _placementBuffer.ConsumeVolume(
                    Holder.GetInventory(MyCharacterConstants.MainInventory),
                    Definition.FillMaterial, (int) totalDeposited);
            }

            #endregion

            if (totalDeposited > 0 || totalExcavated > 0)
            {
                var duraCost = (int) Math.Round(totalDeposited * Definition.FillDurabilityPerVol + totalExcavated * Definition.ExcavateDurabilityPerVol);
                if (duraCost > 0)
                    UpdateDurability(-duraCost);
                GraderUsed?.Invoke(this, _gradeComponents, totalDeposited, totalExcavated);
                system.RaiseDoGrade(_gradeComponents, Target.Position, voxelRadius, availableForDeposit, availableForExcavate,
                    Definition.FillMaterial.Material.Index, totalExcavated, totalDeposited, voxel.Id, dynamicBoxes);
                return;
            }

            if (!isExcavating && intersectedDynamic && triedToChange)
                MessageUtil.ShowNotification(player, "Cannot fill where there are players or dynamic grids", color: new Vector4(1, 0, 0, 1));
            if (!isExcavating && requiredMaterials != null && triedToChange)
                MessageUtil.ShowNotification(player, requiredMaterials?.ToString(), color: new Vector4(1, 0, 0, 1));
        }


        private bool IsLocallyControlled => MySession.Static.PlayerEntity == Holder;

        private bool HasPermission(MyStringId id)
        {
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
                return false;
            return MyAreaPermissionSystem.Static == null || MyAreaPermissionSystem.Static.HasPermission(player.IdentityId, Target.Position, id);
        }

        public override void Activate()
        {
            if (IsLocallyControlled && RailConstants.Debug.DrawGradingShapes)
                MySession.Static.Components.Get<MyUpdateComponent>().AddFixedUpdate(DebugDraw);
            base.Activate();
        }

        public override void Deactivate()
        {
            MySession.Static?.Components.Get<MyUpdateComponent>().RemoveFixedUpdate(DebugDraw);
            base.Deactivate();
        }

        private void GatherGradeComponents(Vector3D pos)
        {
            _gradeComponents.Clear();
            RailGraderSystem.GatherGradeComponents(pos, _gradeComponents, Math.Max(Definition.ExcavateRadius, Definition.FillRadius));
        }

        private void DebugDraw()
        {
            if (Holder == null)
                return;
            RailGraderSystem.DebugDrawGradeComponents(Holder.GetPosition());
        }

        private readonly List<RailGradeComponent> _gradeComponents = new List<RailGradeComponent>();
    }

    [MyDefinitionType(typeof(MyObjectBuilder_RailGraderBehaviorDefinition))]
    [MyDependency(typeof(MyVoxelMiningDefinition), Recursive = true)]
    public class RailGraderBehaviorDefinition : MyToolBehaviorDefinition
    {
        public MyVoxelMiningDefinition ExcavateDefinition { get; private set; }
        public float ExcavateRadius { get; private set; }
        public uint ExcavateVolume { get; private set; }
        public float ExcavateDurabilityPerVol { get; private set; }

        public VoxelPlacementBuffer.VoxelPlacementDefinition FillMaterial { get; private set; }
        public float FillRadius { get; private set; }
        public uint FillVolume { get; private set; }
        public float FillDurabilityPerVol { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_RailGraderBehaviorDefinition) def;

            ExcavateDefinition = ob.ExcavateVolume > 0 && ob.ExcavateRadius > 0
                ? Assert.Definition<MyVoxelMiningDefinition>(ob.ExcavateDefinition, $"For rail grader behavior {def.Id}'s excavate definition")
                : null;
            ExcavateRadius = ob.ExcavateRadius;
            ExcavateVolume = ob.ExcavateVolume;
            ExcavateDurabilityPerVol = ob.ExcavateDurability / ob.ExcavateVolume;

            FillMaterial = new VoxelPlacementBuffer.VoxelPlacementDefinition(ob.FillMaterial, Log);
            FillRadius = ob.FillRadius;
            FillVolume = ob.FillVolume;
            FillDurabilityPerVol = ob.FillDurability / ob.FillVolume;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailGraderBehaviorDefinition : MyObjectBuilder_ToolBehaviorDefinition
    {
        public SerializableDefinitionId ExcavateDefinition;
        public float ExcavateRadius;
        public uint ExcavateVolume;
        public float ExcavateDurability;

        public MyObjectBuilder_VoxelMiningDefinition.MiningDef FillMaterial;
        public float FillRadius;
        public uint FillVolume;
        public float FillDurability;
    }
}