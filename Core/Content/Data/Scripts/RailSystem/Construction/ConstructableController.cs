using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Session;

namespace Equinox76561198048419394.RailSystem.Construction
{
    [MySessionComponent(typeof(MyObjectBuilder_ConstructableController), AllowAutomaticCreation = true, AlwaysOn = true)]
    [StaticEventOwner]
    public class ConstructableController : MySessionComponent
    {
        private readonly List<MyDefinitionId> _definitionPalette = new List<MyDefinitionId>();
        private readonly Dictionary<MyDefinitionId, int> _definitionPaletteMap = new Dictionary<MyDefinitionId, int>();

        public int EncodeExisting(MyDefinitionId def)
        {
            lock (this)
            {
                int idx;
                if (_definitionPaletteMap.TryGetValue(def, out idx)) return idx;
            }

            this.GetLogger().Warning($"Trying to encode invalid palette item {def}; palette is {string.Join(", ", _definitionPalette)}");
            return 0;
        }

        public int Encode(MyDefinitionId def)
        {
            lock (this)
            {
                int idx;
                if (_definitionPaletteMap.TryGetValue(def, out idx)) return idx;
            }

            int result;
            lock (this)
            {
                int idx;
                if (_definitionPaletteMap.TryGetValue(def, out idx)) return idx;
                _definitionPaletteMap[def] = _definitionPalette.Count;
                _definitionPalette.Add(def);
                result = idx;
            }

            MyMultiplayerModApi.Static.RaiseStaticEvent(s => PaletteUpdateClient, result, (SerializableDefinitionId) def, HashPalette());
            return result;
        }

        public MyDefinitionId Decode(int idx)
        {
            if (idx < _definitionPalette.Count) return _definitionPalette[idx];
            this.GetLogger().Warning($"Trying to decode invalid palette item {idx}; palette is {string.Join(", ", _definitionPalette)}");
            return MyDefinitionManager.GetOfType<MyInventoryItemDefinition>().FirstOrDefault()?.Id ?? default(MyDefinitionId);
        }

        protected override void Deserialize(MyObjectBuilder_SessionComponent obj)
        {
            base.Deserialize(obj);
            lock (this)
            {
                var ob = (MyObjectBuilder_ConstructableController) obj;
                _definitionPalette.Clear();
                _definitionPaletteMap.Clear();
                if (ob.Palette == null) return;
                foreach (var k in ob.Palette)
                {
                    _definitionPaletteMap[k] = _definitionPalette.Count;
                    _definitionPalette.Add(k);
                }
            }
        }

        protected override MyObjectBuilder_SessionComponent Serialize()
        {
            var ob = (MyObjectBuilder_ConstructableController) base.Serialize();
            ob.Palette = _definitionPalette.Select(x => (SerializableDefinitionId) x).ToArray();
            return ob;
        }

        protected override bool IsSerialized => true;

        private int HashPalette()
        {
            int hash = 0;
            foreach (var k in _definitionPalette)
                hash = (hash * 31) ^ k.GetHashCode();
            return hash;
        }

        [Event]
        [Reliable]
        [Broadcast]
        private static void PaletteUpdateClient(int index, SerializableDefinitionId id, int verifyHash)
        {
            var mgr = MySession.Static.Components?.Get<ConstructableController>();
            if (mgr == null)
            {
                MySession.Static.Log.Warning($"Requested partial palette update and we don't have a constructable controller");
                return;
            }

            lock (mgr)
            {
                while (mgr._definitionPalette.Count <= index)
                    mgr._definitionPalette.Add(default(MyDefinitionId));
                mgr._definitionPalette[index] = id;
                mgr._definitionPaletteMap[id] = index;
            }

            int hash = mgr.HashPalette();
            if (hash == verifyHash) return;

            mgr.GetLogger().Warning($"Palette verification failed, reacquiring");
            MyMultiplayerModApi.Static.RaiseStaticEvent(s => PaletteRequestUpdateServer);
        }

        [Event]
        [Reliable]
        [Client]
        private static void PaletteFullUpdateClient(SerializableDefinitionId[] ids)
        {
            var mgr = MySession.Static.Components?.Get<ConstructableController>();
            if (mgr == null)
            {
                MySession.Static.Log.Warning($"Requested full palette update and we don't have a constructable controller");
                return;
            }

            lock (mgr)
            {
                mgr._definitionPalette.Clear();
                mgr._definitionPaletteMap.Clear();
                for (var idx = 0; idx < ids.Length; idx++)
                {
                    mgr._definitionPalette.Add(ids[idx]);
                    mgr._definitionPaletteMap[ids[idx]] = idx;
                }
            }
        }

        [Event]
        [Reliable]
        [Server]
        private static void PaletteRequestUpdateServer()
        {
            var mgr = MySession.Static.Components?.Get<ConstructableController>();
            if (mgr == null)
                return;
            var blits = mgr._definitionPalette.Select(x => (SerializableDefinitionId) x).ToArray();
            MyMultiplayerModApi.Static.RaiseStaticEvent(s => PaletteFullUpdateClient, blits, MyEventContext.Current.Sender);
        }
    }

    public class MyObjectBuilder_ConstructableController : MyObjectBuilder_SessionComponent
    {
        public SerializableDefinitionId[] Palette;
    }
}