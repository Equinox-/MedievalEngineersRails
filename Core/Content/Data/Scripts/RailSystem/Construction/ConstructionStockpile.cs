using System;
using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.ModAPI;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.ObjectBuilders.Definitions.Inventory;

namespace Equinox76561198048419394.RailSystem.Construction
{
    public class ConstructionStockpile
    {
        public Dictionary<MyDefinitionId, int> Items { get; } = new Dictionary<MyDefinitionId, int>(MyDefinitionId.Comparer);

        public MyObjectBuilder_ConstructionStockpile GetObjectBuilder()
        {
            return new MyObjectBuilder_ConstructionStockpile()
            {
                Items = Items.Select(x => new MyObjectBuilder_ConstructionStockpile.StockpileItem
                {
                    Id = x.Key,
                    Amount = x.Value
                }).ToList()
            };
        }

        public void Init(MyObjectBuilder_ConstructionStockpile ob)
        {
            Items.Clear();
            if (ob.Items == null)
                return;
            foreach (var k in ob.Items)
                AddItem(k.Id, k.Amount);
        }

        public void AddItem(MyDefinitionId id, int amount)
        {
            Assert.NotEqualObj((Type) id.TypeId, typeof(MyObjectBuilder_ItemTagDefinition), "Trying to add a tag " + id);
            if (amount > 0)
                Items[id] = Items.GetValueOrDefault(id) + amount;
        }

        public string Describe()
        {
            return string.Join(" | ", Items.Select(x => x.Key + "=" + x.Value));
        }

        public int RemoveItem(MyDefinitionId id, int amount)
        {
            Assert.NotEqualObj((Type) id.TypeId, typeof(MyObjectBuilder_ItemTagDefinition), "Trying to remove a tag " + id);
            int current;
            if (!Items.TryGetValue(id, out current)) return 0;

            if (current <= amount)
                Items.Remove(id);
            else
                Items[id] = current - amount;
            return Math.Min(current, amount);
        }
        public int RemoveItemFuzzy(MyDefinitionId id, int amount)
        {
            if (id.TypeId != typeof(MyObjectBuilder_ItemTagDefinition))
                return RemoveItem(id, amount);
            var def = MyDefinitionManager.Get<MyItemTagDefinition>(id);
            var removed = 0;
            foreach (var k in def.Items)
            {
                var removedPart = RemoveItem(k.Id, amount);
                amount -= removedPart;
                removed += removedPart;
                if (amount <= 0) break;
            }
            return removed;
        }

        public bool IsEmpty()
        {
            return Items.Count == 0;
        }

        public void Clear()
        {
            Items.Clear();
        }
    }
}