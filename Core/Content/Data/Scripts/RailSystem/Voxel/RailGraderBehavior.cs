using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
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
using VRage.Definitions;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Library.Logging;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Equipment;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Voxel
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_RailGraderBehaviorDefinition))]
    [StaticEventOwner]
    public class RailGraderBehavior : MyToolBehaviorBase
    {
        private const double GRADE_SCAN_DISTANCE = 25D;

        public MyEntity Owner => Holder;

        public delegate void DelGradeAction(RailGraderBehavior behavior, IReadOnlyCollection<RailGradeComponent> grades, uint added, uint removed);

        public static event DelGradeAction GraderUsed;

        public new RailGraderBehaviorDefinition Definition { get; private set; }
        private MyVoxelMiningDefinition _excavateDefinition;

        private MyVoxelBase _voxel;

        public override void Init(MyEntity holder, MyHandItem handItem, MyHandItemBehaviorDefinition def)
        {
            base.Init(holder, handItem, def);
            Definition = (RailGraderBehaviorDefinition) def;
            _excavateDefinition = Definition.ExcavateVolume > 0 && Definition.ExcavateRadius > 0
                ? Assert.Definition<MyVoxelMiningDefinition>(Definition.ExcavateDefinition, $"For rail grader behavior {def.Id}'s excavate definition")
                : null;
        }

        private Vector3D _cachedTargetDirection;

        protected override bool ValidateTarget()
        {
            if (Vector3D.DistanceSquared(_cachedTargetDirection, Target.Position) < 1)
                return _gradeComponents.Count > 0;

            _voxel = null;
            _gradeComponents.Clear();
            _voxel = (Target.Entity as MyVoxelBase ?? MyGamePruningStructure.GetClosestPlanet(Target.Position))?.RootVoxel;

            var sphere = new BoundingSphereD(Target.Position, GRADE_SCAN_DISTANCE);
            foreach (var e in MyEntities.GetEntitiesInSphere(ref sphere))
            foreach (var k in e.Components.GetComponents<RailGradeComponent>())
                if ((k.Excavation != null && k.Excavation.IsInside(Target.Position)) || (k.Support != null && k.Support.IsInside(Target.Position)))
                    _gradeComponents.Add(k);

            _cachedTargetDirection = Target.Position;
            return _gradeComponents.Count > 0;
        }

        protected override bool Start(MyHandItemActionEnum action)
        {
            return _gradeComponents.Count != 0;
        }

        private int _depositAccumulation;
        private readonly uint[] _excavated = new uint[byte.MaxValue];

        protected override void Hit()
        {
            if (!MyAPIGateway.Session.IsServerDecider())
                return;
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
                return;

            if (!HasPermission(MyPermissionsConstants.Mining))
            {
                player.ShowNotification("You don't have permission to terraform here.", 2000, null, new Vector4(1, 0, 0, 1));
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
                if (!player.IsCreative())
                    foreach (var item in Definition.FillMaterial.MinedItems)
                    {
                        var amount = 0;
                        foreach (var inv in Holder.Components.GetComponents<MyInventoryBase>())
                            amount += inv.GetItemAmountFuzzy(item.Key);
                        amount = amount * Definition.FillMaterial.Volume / item.Value;
                        if (amount == 0)
                        {
                            if (requiredMaterials == null)
                                requiredMaterials = new StringBuilder("You require ");
                            var itemDef = MyDefinitionManager.Get<MyInventoryItemDefinition>(item.Key);
                            andHead = requiredMaterials.Length;
                            missing++;
                            requiredMaterials.Append(itemDef?.DisplayNameOf() ?? item.Key.ToString()).Append(", ");
                        }

                        availableForDeposit = (uint) Math.Min(availableForDeposit, amount);
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
            var result = RailGrader.DoGrading(_gradeComponents, Target.Position, radius, availableForDeposit,
                availableForExcavate, _excavated, Definition.FillMaterial.Material.Index,
                out totalDeposited, out totalExcavated, testDynamic: true,
                triedToChange: out triedToChange, intersectedDynamic: out intersectedDynamic);

            #region Give Items

            if (triedToChange && isExcavating && !player.IsCreative())
            {
                for (var i = 0; i < _excavated.Length; i++)
                {
                    if (_excavated[i] == 0) continue;
                    MyVoxelMiningDefinition.MiningEntry einfo;
                    if (_excavateDefinition == null || !_excavateDefinition.MiningEntries.TryGetValue(i, out einfo)) continue;
                    var outputInventory = Holder.GetInventory(MyCharacterConstants.MainInventory);
                    int count = (int) Math.Floor(_excavated[i] / (float) einfo.Volume);
                    if (count == 0) continue;
                    _excavated[i] -= (uint) Math.Max(0, count * einfo.Volume);
                    foreach (var k in einfo.MinedItems)
                    {
                        var amount = k.Value * count;
                        if (outputInventory != null && outputInventory.AddItems(k.Key, amount)) continue;
                        var pos = MyAPIGateway.Entities.FindFreePlace(Target.Position, radius) ?? Target.Position;
                        MyFloatingObjects.Spawn(MyInventoryItem.Create(k.Key, amount), MatrixD.CreateTranslation(pos), null);
                    }
                }
            }

            #endregion

            #region Take Items

            _depositAccumulation += (int) totalDeposited;
            if (!player.IsCreative())
                if (_depositAccumulation > 0 && !isExcavating && Definition.FillMaterial.MinedItems != null)
                {
                    int amnt = (int) Math.Floor(_depositAccumulation / (float) Definition.FillMaterial.Volume);
                    _depositAccumulation -= amnt * Definition.FillMaterial.Volume;
                    if (amnt > 0)
                        foreach (var k in Definition.FillMaterial.MinedItems)
                        {
                            var required = amnt * k.Value;
                            if (required == 0)
                                return;
                            foreach (var inv in Holder.Components.GetComponents<MyInventoryBase>())
                            {
                                var count = Math.Min(required, inv.GetItemAmountFuzzy(k.Key));
                                if (count > 0 && inv.RemoveItemsFuzzy(k.Key, count))
                                    required -= count;
                            }
                        }
                }

            #endregion

            if (totalDeposited > 0 || totalExcavated > 0)
            {
                var duraCost = (int) Math.Round(totalDeposited * Definition.FillDurabilityPerVol + totalExcavated * Definition.ExcavateDurabilityPerVol);
                if (duraCost > 0)
                    UpdateDurability(-duraCost);
                GraderUsed?.Invoke(this, _gradeComponents, totalDeposited, totalExcavated);
                MyAPIGateway.Multiplayer?.RaiseStaticEvent((x) => DoGrade, _gradeComponents.Select(x => x.Blit()).ToArray(), Target.Position,
                    new GradingConfig()
                    {
                        Radius = radius,
                        DepositAvailable = availableForDeposit,
                        ExcavateAvailable = availableForExcavate,
                        MaterialToDeposit = Definition.FillMaterial.Material.Index,
                        ExcavateExpected = totalExcavated,
                        DepositExpected = totalDeposited
                    });
                return;
            }

            if (!isExcavating && intersectedDynamic && triedToChange)
                player.ShowNotification("Cannot fill where there are players or dynamic grids", 2000, null, new Vector4(1, 0, 0, 1));
            if (!isExcavating && requiredMaterials != null && triedToChange)
                player.ShowNotification(requiredMaterials?.ToString(), 2000, null, new Vector4(1, 0, 0, 1));
        }

        private struct GradingConfig
        {
            public float Radius;
            public uint DepositAvailable;
            public uint ExcavateAvailable;
            public byte MaterialToDeposit;
            public uint DepositExpected;
            public uint ExcavateExpected;
        }

        private const int _gradingDesyncTol = 5;

        [Event]
        [Broadcast]
        private static void DoGrade(RailGradeComponentBlit[] components, Vector3D target, GradingConfig config)
        {
            uint deposited;
            uint excavated;
            bool triedToChange;
            bool intersectedDynamic;
            var cbox = new IRailGradeComponent[components.Length];
            for (var i = 0; i < components.Length; i++)
                cbox[i] = components[i];
            RailGrader.DoGrading(cbox, target, config.Radius, config.DepositAvailable, config.ExcavateAvailable, null, config.MaterialToDeposit, out deposited,
                out excavated, testDynamic: true,
                triedToChange: out triedToChange,
                intersectedDynamic: out intersectedDynamic);

            if (Math.Abs(config.DepositExpected - deposited) > _gradingDesyncTol || Math.Abs(config.ExcavateExpected - excavated) > _gradingDesyncTol)
            {
                MyLog.Default.Warning($"Grading desync occured!  {config.DepositExpected} != {deposited}, {config.ExcavateExpected} != {excavated}");
                var time = DateTime.Now;
                if ((time - _lastGradingDesync) > TimeSpan.FromSeconds(30))
                {
                    var red = new Vector4(1, 0, 0, 1);
                    MyAPIGateway.Utilities.ShowNotification("Grading desync occured!  If you experience movement problems try reconnecting.", 5000, null, red);
                    _lastGradingDesync = time;
                }
            }
        }

        private static DateTime _lastGradingDesync = new DateTime(0);

        private bool IsLocallyControlled => MySession.Static.PlayerEntity == Holder;

        private bool HasPermission(MyStringId id)
        {
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
                return false;
            return MyAreaPermissionSystem.Static == null || MyAreaPermissionSystem.Static.HasPermission(player.IdentityId, Target.Position, id);
        }

        private readonly List<RailGradeComponent> _gradeComponents = new List<RailGradeComponent>();
    }

    [MyDefinitionType(typeof(MyObjectBuilder_RailGraderBehaviorDefinition))]
    public class RailGraderBehaviorDefinition : MyToolBehaviorDefinition
    {
        public MyDefinitionId ExcavateDefinition { get; private set; }
        public float ExcavateRadius { get; private set; }
        public uint ExcavateVolume { get; private set; }
        public float ExcavateDurabilityPerVol { get; private set; }

        public MiningEntry FillMaterial { get; private set; }
        public float FillRadius { get; private set; }
        public uint FillVolume { get; private set; }
        public float FillDurabilityPerVol { get; private set; }

        public struct MiningEntry
        {
            public readonly MyVoxelMaterialDefinition Material;
            public readonly IReadOnlyDictionary<MyDefinitionId, int> MinedItems;
            public readonly int Volume;

            public MiningEntry(MyObjectBuilder_VoxelMiningDefinition_MiningDef item)
            {
                var dictionary = new Dictionary<MyDefinitionId, int>();
                if (item.MinedItems != null)
                {
                    foreach (MyObjectBuilder_VoxelMiningDefinition_MinedItem minedItem in item.MinedItems)
                    {
                        var sid = new SerializableDefinitionId
                        {
                            TypeIdString = minedItem.Type,
                            SubtypeName = minedItem.Subtype
                        };
                        dictionary[sid] = minedItem.Amount;
                    }
                }

                int num = item.Volume.HasValue ? Math.Max(item.Volume.Value, 1) : 64;
                MyVoxelMaterialDefinition materialDefinition = MyDefinitionManager.Get<MyVoxelMaterialDefinition>(item.VoxelMaterial);
                if (materialDefinition == null || materialDefinition.Index == byte.MaxValue)
                {
                    MyLog.Default.Error("Cannot find voxel material {0}", item.VoxelMaterial);
                    Material = MyVoxelMaterialDefinition.Default;
                    MinedItems = dictionary;
                    Volume = 64;
                }
                else
                {
                    Material = materialDefinition;
                    MinedItems = dictionary;
                    Volume = num;
                }
            }
        }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_RailGraderBehaviorDefinition) def;

            ExcavateDefinition = ob.ExcavateDefinition;
            ExcavateRadius = ob.ExcavateRadius;
            ExcavateVolume = ob.ExcavateVolume;
            ExcavateDurabilityPerVol = ob.ExcavateDurability / ob.ExcavateVolume;

            FillMaterial = new MiningEntry(ob.FillMaterial);
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

        public MyObjectBuilder_VoxelMiningDefinition_MiningDef FillMaterial;
        public float FillRadius;
        public uint FillVolume;
        public float FillDurability;
    }
}