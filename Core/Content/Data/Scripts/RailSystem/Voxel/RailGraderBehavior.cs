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
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Inventory;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using VRage.Definitions;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Library.Logging;
using VRage.Network;
using VRage.ObjectBuilder;
using VRage.ObjectBuilder.Merging;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions;
using VRage.ObjectBuilders.Definitions.Equipment;
using VRage.Session;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Voxel
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_RailGraderBehaviorDefinition))]
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
        }


        private static readonly MyStorageData _storage = new MyStorageData();

        private static void DoGrading(long holderEntityId, DefinitionIdBlit graderId, Vector3D target)
        {
            var Holder = MyEntities.GetEntityById(holderEntityId);
            var
            
            if (Holder == null)
            {
                MyEventContext.ValidationFailed();
                return;
            }

            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
            {
                MyEventContext.ValidationFailed();
                return;
            }

            if (!player.HasPermission(target, MyPermissionsConstants.Mining))
            {
                player.ShowNotification("You don't have permission to terraform here.", 2000, null, new Vector4(1, 0, 0, 1));
                MyEventContext.ValidationFailed();
                return;
            }

            bool isExcavating = ActiveAction == MyHandItemActionEnum.Primary;

            var radius = isExcavating ? Definition.ExcavateRadius : Definition.FillRadius;
            var voxelRadius = (int) Math.Ceiling(radius);
            var centerWorld = Target.Position;
            Vector3I center;
            MyVoxelCoordSystems.WorldPositionToVoxelCoord(_voxel.PositionLeftBottomCorner, ref centerWorld, out center);
            var voxMin = center - voxelRadius - 1;
            var voxMax = center + voxelRadius + 1;
            _storage.Resize(voxMin, voxMax);
            _voxel.Storage.ReadRange(_storage, MyStorageDataTypeFlags.ContentAndMaterial, 0, voxMin, voxMax);

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
                if (!MyAPIGateway.Session.IsCreative())
                    foreach (var item in Definition.FillMaterial.MinedItems)
                    {
                        var amount = 0;
                        foreach (var inv in Holder.Components.GetComponents<MyInventoryBase>())
                            amount += inv.GetItemAmountFuzzy(item.Key);
                        amount /= item.Value;
                        if (amount == 0)
                        {
                            if (requiredMaterials == null && IsLocallyControlled)
                                requiredMaterials = new StringBuilder("You require ");
                            var itemDef = MyDefinitionManager.Get<MyInventoryItemDefinition>(item.Key);
                            andHead = requiredMaterials.Length;
                            missing++;
                            if (IsLocallyControlled)
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

            uint totalDeposited = 0;
            uint availableForExcavate = Definition.ExcavateVolume;
            uint totalExcavated = 0;
            bool triedToChange = false;
            bool changed = false;

            #region Mutate

            bool intersectedPlayer = false;

            for (var i = 0; i <= voxelRadius && (!triedToChange || (availableForExcavate > 0 && availableForDeposit > 0)); i++)
            for (var e = new ShellEnumerator(center - i, center + i); e.MoveNext() && (!triedToChange || (availableForExcavate > 0 && availableForDeposit > 0));)
            {
                var vCoord = e.Current;
                var dataCoord = e.Current - voxMin;
                Vector3D worldCoord;
                MyVoxelCoordSystems.VoxelCoordToWorldPosition(_voxel.PositionLeftBottomCorner, ref vCoord, out worldCoord);
                var cval = _storage.Get(MyStorageDataTypeEnum.Content, ref dataCoord);

                byte? excavationDensity = null;
                if (isExcavating && cval > 0 && (!triedToChange || availableForExcavate > 0))
                {
                    float density = 0;
                    foreach (var c in _gradeComponents.Select(x => x.Excavation).Where(x => x != null))
                        density = Math.Max(density, c.GetDensity(ref worldCoord));
                    if (density > 0)
                        excavationDensity = (byte) ((1 - density) * byte.MaxValue);
                }

                byte? fillDensity = null;
                if (!isExcavating && cval < byte.MaxValue && (!triedToChange || availableForDeposit > 0))
                {
                    float density = 0;
                    foreach (var c in _gradeComponents.Select(x => x.Support).Where(x => x != null))
                        density = Math.Max(density, c.GetDensity(ref worldCoord));
                    if (density > 0)
                        fillDensity = (byte) (density * byte.MaxValue);
                }

                if ((!fillDensity.HasValue || cval >= fillDensity.Value) && (!excavationDensity.HasValue || cval <= excavationDensity.Value))
                    continue;

                if (excavationDensity.HasValue && excavationDensity.Value < cval)
                {
                    triedToChange = true;
                    var toExtract = (uint) Math.Min(availableForExcavate, cval - excavationDensity.Value);
                    if (toExtract > 0)
                    {
                        var mid = _storage.Get(MyStorageDataTypeEnum.Material, ref dataCoord);
                        if (mid < _excavated.Length)
                            _excavated[mid] += toExtract;
                        DisableFarming(worldCoord);
                        _storage.Set(MyStorageDataTypeEnum.Content, ref dataCoord, (byte) (cval - toExtract));
                        totalExcavated += toExtract;
                        availableForExcavate -= toExtract;
                        changed = true;
                    }

                    continue;
                }

                if (!fillDensity.HasValue || fillDensity.Value <= cval)
                    continue;
                triedToChange = true;
                var toFill = Math.Min(availableForDeposit, fillDensity.Value - cval);
                if (toFill <= 0)
                    continue;

                // would this deposit in midair?
                {
                    var test = worldCoord;
                    test += 2 * Vector3D.Normalize(MyGravityProviderSystem.CalculateNaturalGravityInPoint(worldCoord));
                    Vector3I vtest;
                    MyVoxelCoordSystems.WorldPositionToVoxelCoord(_voxel.PositionLeftBottomCorner, ref test, out vtest);
                    vtest = Vector3I.Clamp(vtest, voxMin, voxMax) - voxMin;
                    if (vtest != vCoord && _storage.Get(MyStorageDataTypeEnum.Content, ref vtest) == 0)
                        continue;
                }

                // would it touch something dynamic?
                {
                    var box = new BoundingBoxD(worldCoord - 0.25, worldCoord + 0.25);
                    var bad = false;
                    foreach (var k in MyEntities.GetEntitiesInAABB(ref box))
                    {
                        if (k.Physics == null || k.Physics.IsStatic)
                            continue;
                        var obb = new OrientedBoundingBoxD(k.PositionComp.LocalAABB, k.WorldMatrix);
                        if (!obb.Intersects(ref box)) continue;
                        bad = true;
                        break;
                    }

                    if (bad)
                    {
                        intersectedPlayer = true;
                        continue;
                    }
                }

                changed = true;
                DisableFarming(worldCoord);
                availableForDeposit = (uint) (availableForDeposit - toFill);
                totalDeposited += (uint) toFill;
                _storage.Set(MyStorageDataTypeEnum.Content, ref dataCoord, (byte) (cval + toFill));
                if (fillDensity.Value <= cval * 1.5f) continue;
                var t = -Vector3I.One;
                for (var itrContent = new Vector3I_RangeIterator(ref t, ref Vector3I.One); itrContent.IsValid(); itrContent.MoveNext())
                {
                    var tpos = dataCoord + itrContent.Current;
                    _storage.Set(MyStorageDataTypeEnum.Material, ref tpos, Definition.FillMaterial.Material.Index);
                }
            }

            #endregion Mutate

            #region Give Items

            if (triedToChange && isExcavating)
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
                        var pos = MyAPIGateway.Entities.FindFreePlace(centerWorld, radius) ?? centerWorld;
                        MyFloatingObjects.Spawn(MyInventoryItem.Create(k.Key, amount), MatrixD.CreateTranslation(pos), null);
                    }
                }
            }

            #endregion

            #region Take Items

            _depositAccumulation += (int) totalDeposited;
            if (!MyAPIGateway.Session.IsCreative())
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

            if (changed)
            {
                _voxel.Storage.WriteRange(_storage, MyStorageDataTypeFlags.ContentAndMaterial, voxMin, voxMax);
                GraderUsed?.Invoke(this, _gradeComponents, totalDeposited, totalExcavated);
                return;
            }

            if (!IsLocallyControlled) return;
            if (!isExcavating && intersectedPlayer && triedToChange)
                MyAPIGateway.Utilities.ShowNotification("Cannot fill where there are players or dynamic grids", 2000, null, new Vector4(1, 0, 0, 1));
            if (!isExcavating && requiredMaterials != null && triedToChange)
                MyAPIGateway.Utilities.ShowNotification(requiredMaterials?.ToString(), 2000, null, new Vector4(1, 0, 0, 1));
        }

        private bool IsLocallyControlled => MySession.Static.PlayerEntity == Holder;

        private bool HasPermission(MyStringId id)
        {
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
                return false;
            return MyAreaPermissionSystem.Static == null || MyAreaPermissionSystem.Static.HasPermission(player.IdentityId, Target.Position, id);
        }

        private static void DisableFarming(Vector3D pos)
        {
            FarmingExtensions.DisableItemsIn(new BoundingBoxD(pos - 1, pos + 1));
        }

        private readonly List<RailGradeComponent> _gradeComponents = new List<RailGradeComponent>();
    }

    [MyDefinitionType(typeof(MyObjectBuilder_RailGraderBehaviorDefinition))]
    public class RailGraderBehaviorDefinition : MyToolBehaviorDefinition
    {
        public MyDefinitionId ExcavateDefinition { get; private set; }
        public float ExcavateRadius { get; private set; }
        public uint ExcavateVolume { get; private set; }

        public MiningEntry FillMaterial { get; private set; }
        public float FillRadius { get; private set; }
        public uint FillVolume { get; private set; }

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

            FillMaterial = new MiningEntry(ob.FillMaterial);
            FillRadius = ob.FillRadius;
            FillVolume = ob.FillVolume;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailGraderBehaviorDefinition : MyObjectBuilder_ToolBehaviorDefinition
    {
        public SerializableDefinitionId ExcavateDefinition;
        public float ExcavateRadius;
        public uint ExcavateVolume;

        public MyObjectBuilder_VoxelMiningDefinition_MiningDef FillMaterial;
        public float FillRadius;
        public uint FillVolume;
    }
}