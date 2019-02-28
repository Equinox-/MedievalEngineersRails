using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Components.Physics;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Inventory;
using VRage.Session;

namespace Equinox76561198048419394.RailSystem.Construction
{
    public class ConstructableComponentReplicable : MyComponentReplicable<ConstructableComponent>
    {
    }

    [MyComponent(typeof(MyObjectBuilder_ConstructableComponent))]
    [MyDefinitionRequired(typeof(ConstructableComponentDefinition))]
    [ReplicatedComponent(typeof(ConstructableComponentReplicable))]
    public class ConstructableComponent : MyEntityComponent, IMyEventProxy
    {
        public ConstructableComponentDefinition Definition { get; private set; }

        private float _buildIntegrity;

        public float BuildIntegrity
        {
            get { return _buildIntegrity; }
            set
            {
                if (Math.Abs(_buildIntegrity - value) < .0001f) return;
                _buildIntegrity = value;
                IntegrityChanged?.Invoke();
            }
        }

        public float MaxIntegrity => Definition.MaxIntegrity;
        public float BuildPercent => BuildIntegrity / MaxIntegrity;

        private static ConstructableController Controller => MySession.Static.Components.Get<ConstructableController>();

        private ConstructionStockpile _stockpile;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            Definition = (ConstructableComponentDefinition) definition;
        }

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = new MyObjectBuilder_ConstructableComponent()
            {
                BInteg = BuildIntegrity,
                SPacked = ""
            };
            if (_stockpile == null || _stockpile.IsEmpty()) return ob;

            var stream = new MemStream(_stockpile.Items.Count * 2 + 1);
            stream.Write7BitEncoded((ulong) _stockpile.Items.Count);
            foreach (var k in _stockpile.Items)
            {
                stream.Write7BitEncoded((ulong) Controller.EncodeExisting(k.Key));
                stream.Write7BitEncoded((ulong) k.Value);
            }

            ob.SPacked = stream.ToBase64();
            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent bbase)
        {
            var ob = (MyObjectBuilder_ConstructableComponent) bbase;
            if (!string.IsNullOrEmpty(ob.SPacked))
            {
                try
                {
                    var stream = new MemStream(Convert.FromBase64String(ob.SPacked));
                    if (_stockpile == null)
                        _stockpile = new ConstructionStockpile();
                    _stockpile.Items.Clear();
                    var count = (int) stream.Read7BitEncoded();
                    for (var i = 0; i < count; i++)
                        _stockpile.AddItem(Controller.Decode((int) stream.Read7BitEncoded()),
                            (int) stream.Read7BitEncoded());
                }
                catch (Exception e)
                {
                    this.GetLogger().Warning($"Failed to deserialize packed stockpile data {e}");
                }
            }
            else
                _stockpile?.Clear();

            BuildIntegrity = ob.BInteg;
        }

        public ConstructableComponentDefinition.CcBuildModel CurrentBuildModel =>
            Definition.BuildModelFor(BuildIntegrity);

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            ModelChanged = null;
            IntegrityChanged = null;
            IntegrityChanged += () => MyMultiplayerModApi.Static.RaiseEvent(this, cc => cc.SyncIntegrity, BuildIntegrity);
            IntegrityChanged += CheckModel;
        }

        public override void OnBeforeRemovedFromContainer()
        {
            base.OnBeforeRemovedFromContainer();
            ModelChanged = null;
            IntegrityChanged = null;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            CheckModel();
        }

        public override bool IsSerialized => true;

        public string DescStockpile()
        {
            return _stockpile?.Describe() ?? "nil";
        }

        public bool StockpileEmpty => _stockpile == null || _stockpile.IsEmpty();

        private string _currentModel;

        private void CheckModel()
        {
            if (Entity == null || !Entity.InScene)
                return;
            var stage = Definition.BuildModelFor(BuildIntegrity / Definition.MaxIntegrity);
            if (stage.ModelFile == _currentModel) return;
            Entity.RefreshModels(stage.ModelFile, null);
            if (Entity.Render != null && Entity.Render.GetRenderObjectID() != uint.MaxValue)
            {
                Entity.Render.RemoveRenderObjects();
                Entity.Render.AddRenderObjects();
            }

            _currentModel = stage.ModelFile;
            ModelChanged?.Invoke();
        }

        public event Action ModelChanged;
        public event Action IntegrityChanged;

        #region Install

        public delegate void SupplyForInstall<in TU>(TU userData, MyDefinitionId request, ref int amount);

        public void InstallFromCreative()
        {
            Install<object>(InstallWorkerFromCreative, null);
        }

        public void InstallFrom(MyInventoryBase inv)
        {
            Install(InstallWorkerFromInventory, inv);
        }

        public void InstallFrom(IEnumerable<MyInventoryBase> invs)
        {
            Install(InstallWorkerFromInventories, invs);
        }

        public void Install<TU>(SupplyForInstall<TU> supplier, TU userData)
        {
            using (new StockpileChangeWatcher(this))
            {
                _tmpStockpile.Items.Clear();
                if (_stockpile != null)
                    foreach (var k in _stockpile.Items)
                        _tmpStockpile.AddItem(k.Key, k.Value);

                foreach (var c in Definition.Components)
                {
                    var req = (MyDefinitionId) c.Required;
                    var existing = _tmpStockpile.RemoveItemFuzzy(req, c.Count);
                    var remain = c.Count - existing;
                    if (remain <= 0) continue;

                    if (req.TypeId == typeof(MyObjectBuilder_ItemTagDefinition))
                    {
                        var tag = MyDefinitionManager.Get<MyItemTagDefinition>(req);
                        foreach (var subReq in tag.Items)
                        {
                            var removed = remain;
                            supplier.Invoke(userData, subReq.Id, ref removed);
                            if (removed == 0)
                                continue;
                            if (_stockpile == null)
                                _stockpile = new ConstructionStockpile();
                            _stockpile.AddItem(subReq.Id, removed);
                            remain -= removed;
                            if (remain <= 0)
                                break;
                        }
                    }
                    else
                    {
                        var removed = remain;
                        supplier.Invoke(userData, req, ref removed);
                        if (removed > 0)
                        {
                            if (_stockpile == null)
                                _stockpile = new ConstructionStockpile();
                            _stockpile.AddItem(req, removed);
                            remain -= removed;
                        }
                    }

                    if (remain > 0)
                        break;
                }

                _tmpStockpile.Clear();
            }
        }

        #region Install Worker

        private void InstallWorkerFromInventory(MyInventoryBase inv, MyDefinitionId request, ref int amount)
        {
            amount = Math.Min(amount, inv.GetItemAmount(request));
            if (amount == 0)
                return;
            if (!inv.RemoveItems(request, amount))
                amount = 0;
        }

        private void InstallWorkerFromInventories(IEnumerable<MyInventoryBase> invs, MyDefinitionId request,
            ref int amount)
        {
            var removeRemain = amount;
            amount = 0;
            foreach (var inv in invs)
            {
                var remove = Math.Min(removeRemain, inv.GetItemAmount(request));
                if (remove == 0)
                    continue;
                if (inv.RemoveItems(request, remove))
                {
                    amount += remove;
                    removeRemain -= remove;
                }

                if (removeRemain <= 0)
                    break;
            }
        }

        private void InstallWorkerFromCreative(object userData, MyDefinitionId request, ref int amount)
        {
            // do nothing
        }

        #endregion

        #endregion

        #region Uninstall

        public delegate void DelTryRemoveItem<in TU>(TU userData, MyDefinitionId toRemove, ref int removeCount);

        public void Uninstall<TU>(DelTryRemoveItem<TU> uninstaller, TU userData)
        {
            if (_stockpile == null)
                return;
            using (new StockpileChangeWatcher(this))
            {
                _tmpStockpile.Items.Clear();
                foreach (var k in _stockpile.Items)
                    _tmpStockpile.AddItem(k.Key, k.Value);

                {
                    // remove stockpile items that are in use
                    int componentsRemain = LockedComponents;
                    foreach (var c in Definition.Components)
                    {
                        componentsRemain -=
                            _tmpStockpile.RemoveItemFuzzy(c.Required, Math.Min(c.Count, componentsRemain));
                        if (componentsRemain <= 0)
                            break;
                    }

                    Assert.Equals(0, componentsRemain, "Unexpected number of components remaining");
                }

                {
                    // flush remaining items from the stockpile
                    foreach (var item in _tmpStockpile.Items)
                    {
                        var count = item.Value;
                        uninstaller.Invoke(userData, item.Key, ref count);
                        if (count > 0)
                            Assert.Equals(count, _stockpile.RemoveItem(item.Key, count),
                                "Removed count not equal to uninstalled count");
                    }

                    if (_stockpile.IsEmpty())
                        _stockpile = null;
                }
                _tmpStockpile.Clear();
            }
        }

        public void UninstallTo(MyInventoryBase invCap)
        {
            Uninstall(UninstallToInventory, invCap);
        }

        public void UninstallAndDrop()
        {
            Uninstall<object>(UninstallToWorld, null);
        }

        #region Uninstall Workers

        private void UninstallToWorld(object userdata, MyDefinitionId toremove, ref int removecount)
        {
            var item = new MyInventoryItem();
            item.Init(toremove, removecount);
            MyPhysicsComponentBase phys = null;
            {
                var tmp = Entity;
                while (tmp != null && phys == null)
                {
                    phys = tmp.Physics;
                    tmp = tmp.Parent;
                }
            }
            MySession.Static.Components.Get<MyFloatingObjects>()?.Spawn(item, Entity.WorldMatrix, phys);
        }

        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void UninstallToInventory(MyInventoryBase inv, MyDefinitionId id, ref int amount)
        {
            var toAdd = Math.Min(amount, inv.ComputeAmountThatFits(id));
            // ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
            if (inv.AddItems(id, toAdd))
                amount = toAdd;
            else
                amount = 0;
        }

        #endregion

        #endregion

        public void IncreaseIntegrity(float hammerTime, out ConstructableComponentDefinition.CcComponent requiredComponent, out int requiredCount)
        {
            var di = hammerTime * Definition.IntegrityPerSecond;
            var maxIntegrity = ComputeMaxPossibleIntegrity(out requiredComponent, out requiredCount);
            if (maxIntegrity >= BuildIntegrity + di)
            {
                requiredComponent = default(ConstructableComponentDefinition.CcComponent);
                requiredCount = 0;
            }

            BuildIntegrity = Math.Min(maxIntegrity, BuildIntegrity + di);
        }

        public void DecreaseIntegrity(float hammerTime)
        {
            var di = -hammerTime * Definition.IntegrityPerSecond;
            BuildIntegrity = Math.Max(0, BuildIntegrity + di);
        }

        private float ComputeMaxPossibleIntegrity(out ConstructableComponentDefinition.CcComponent requiredComponent,
            out int requiredCount)
        {
            requiredComponent = default(ConstructableComponentDefinition.CcComponent);
            requiredCount = 0;

            _tmpStockpile.Items.Clear();
            if (_stockpile != null)
                foreach (var k in _stockpile.Items)
                    _tmpStockpile.AddItem(k.Key, k.Value);

            var availableComponents = 0;
            foreach (var c in Definition.Components)
            {
                var removed = _tmpStockpile.RemoveItemFuzzy(c.Required, c.Count);
                availableComponents += removed;
                if (removed == c.Count) continue;
                requiredComponent = c;
                requiredCount = c.Count - removed;
                break;
            }

            _tmpStockpile.Clear();
            return availableComponents * Definition.MaxIntegrity / Definition.TotalComponents;
        }

        private int LockedComponents => RequiredComponents(BuildIntegrity);

        private int RequiredComponents(float integrity)
        {
            return (int) Math.Ceiling(Definition.TotalComponents * integrity / Definition.MaxIntegrity);
        }

        #region Sync

        private struct StockpileChangeWatcher : IDisposable
        {
            private ConstructableComponent _component;
            private Dictionary<MyDefinitionId, int> _snapshot;

            public StockpileChangeWatcher(ConstructableComponent c)
            {
                _component = c;
                if (c._stockpile != null && !c._stockpile.IsEmpty())
                {
                    PoolManager.Get(out _snapshot);
                    _snapshot.Clear();
                    foreach (var kv in c._stockpile.Items)
                        _snapshot[kv.Key] = kv.Value;
                }
                else
                {
                    _snapshot = null;
                }
            }

            public void Dispose()
            {
                #region Update Palette

                if (_component._stockpile != null)
                    foreach (var id in _component._stockpile.Items.Keys)
                        Controller.Encode(id);

                #endregion

                #region Network

                var alwaysFullSync = false;
                if (_snapshot != null && _snapshot.Count > 0 &&
                    (_component._stockpile == null || _component._stockpile.IsEmpty()))
                {
                    // full resync, clear
                    MyMultiplayerModApi.Static.RaiseEvent(_component, cc => cc.SyncFullStateBcast, SyncComponentBlit.Empty);
                }
                else if (alwaysFullSync || ((_snapshot == null || _snapshot.Count == 0) &&
                                            (_component._stockpile != null && !_component._stockpile.IsEmpty())))
                {
                    // full resync, set
                    MyMultiplayerModApi.Static.RaiseEvent(_component, cc => cc.SyncFullStateBcast,
                        _component._stockpile?.Items?.Select(x => (SyncComponentBlit) x).ToArray() ?? SyncComponentBlit.Empty);
                }
                else if (_snapshot != null && _component._stockpile != null)
                {
                    // find changes
                    foreach (var test in _component._stockpile.Items)
                    {
                        int oldVal;
                        if (_snapshot.TryGetValue(test.Key, out oldVal) && oldVal == test.Value)
                            _snapshot.Remove(test.Key);
                        else
                            _snapshot[test.Key] = test.Value;
                    }

                    var changes = new SyncComponentBlit[_snapshot.Count];
                    var i = 0;
                    foreach (var k in _snapshot)
                        changes[i++] = new SyncComponentBlit() {Id = k.Key, Count = _component._stockpile.Items.GetValueOrDefault(k.Key)};
                    MyMultiplayerModApi.Static.RaiseEvent(_component, cc => cc.SyncPartialState, changes,
                        _component.ComputeComponentHash());
                }

                #endregion


                _component = null;
                if (_snapshot == null) return;
                _snapshot.Clear();
                PoolManager.Return(_snapshot);
                _snapshot = null;
            }
        }

        private struct SyncComponentBlit
        {
            public static readonly SyncComponentBlit[] Empty = new SyncComponentBlit[0];

            public MyDefinitionId Id;
            public int Count;

            public static implicit operator SyncComponentBlit(KeyValuePair<MyDefinitionId, int> kv)
            {
                return new SyncComponentBlit {Id = kv.Key, Count = kv.Value};
            }
        }

        [Event]
        [Reliable]
        [Broadcast]
        private void SyncPartialState(SyncComponentBlit[] changed, int hash)
        {
            ParseState(false, changed);
            if (ComputeComponentHash() == hash) return;
            this.GetLogger().Warning($"Constructable state verification failed, reacquiring");
            MyMultiplayerModApi.Static.RaiseEvent(this, cc => cc.SyncRequestFullState);
        }

        [Event]
        [Reliable]
        [Broadcast]
        private void SyncIntegrity(float integrity)
        {
            BuildIntegrity = integrity;
        }

        [Event]
        [Reliable]
        [Broadcast]
        private void SyncFullStateBcast(SyncComponentBlit[] state)
        {
            ParseState(true, state);
        }

        [Event]
        [Reliable]
        [Client]
        private void SyncFullState(SyncComponentBlit[] state)
        {
            ParseState(true, state);
        }

        private void ParseState(bool reset, IReadOnlyCollection<SyncComponentBlit> info)
        {
            if (info.Count > 0)
            {
                if (_stockpile == null)
                    _stockpile = new ConstructionStockpile();
                else if (reset)
                    _stockpile.Clear();
                foreach (var entry in info)
                    if (entry.Count == 0)
                        _stockpile.Items.Remove(entry.Id);
                    else
                        _stockpile.Items[entry.Id] = entry.Count;
            }
            else if (reset)
            {
                _stockpile = null;
            }
        }

        [Event]
        [Reliable]
        [Server]
        private void SyncRequestFullState()
        {
            MyMultiplayerModApi.Static.RaiseEvent(this, cc => cc.SyncFullState,
                _stockpile?.Items.Select(x => (SyncComponentBlit) x).ToArray() ?? SyncComponentBlit.Empty,
                MyEventContext.Current.Sender);
        }

        private int ComputeComponentHash()
        {
            var hash = 0;
            if (_stockpile == null) return hash;
            foreach (var kv in _stockpile.Items)
                if (kv.Value > 0)
                {
                    hash = (hash * 31) ^ kv.Key.TypeId.ToString().GetHashCode();
                    hash = (hash * 31) ^ kv.Key.SubtypeId.GetHashCode();
                    hash = (hash * 31) ^ kv.Value.GetHashCode();
                }

            return hash;
        }

        #endregion

        private static readonly ConstructionStockpile _tmpStockpile = new ConstructionStockpile();
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_ConstructableComponent : MyObjectBuilder_EntityComponent
    {
        /// <summary>
        /// Base64 packed stockpile data
        /// </summary>
        [XmlAttribute]
        public string SPacked;

        /// <summary>
        /// Build integrity
        /// </summary>
        [XmlAttribute]
        public float BInteg;
    }
}