using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using ProtoBuf;
using VRage.Collections;
using VRage.Components;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.Session;

namespace Equinox76561198048419394.RailSystem.Bendy
{
    [MySessionComponent(typeof(MyObjectBuilder_BendyController), AlwaysOn = true, AllowAutomaticCreation = true)]
    public class BendyController : MySessionComponent
    {
        private readonly Dictionary<string, BendyLayer> _layers = new Dictionary<string, BendyLayer>(StringComparer.OrdinalIgnoreCase);
        public event Action<string, BendyLayer> LayerAdded;

        public DictionaryReader<string, BendyLayer> Layers => _layers;

        public BendyLayer GetLayer(string id)
        {
            return _layers.GetValueOrDefault(id);
        }

        public BendyLayer GetOrCreateLayer(string id)
        {
            BendyLayer res;
            if (!_layers.TryGetValue(id, out res))
            {
                var fixedId = id.ToUpperInvariant();
                _layers.Add(id, res = new BendyLayer(this, fixedId));
                LayerAdded?.Invoke(fixedId, res);
            }

            return res;
        }

        [FixedUpdate]
        public void Update()
        {
            foreach (var k in _layers.Values)
                k.UpdateAfterSimulation();
        }
    }

    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    [MyObjectBuilderDefinition(null)]
    [ProtoContract]
    public class MyObjectBuilder_BendyController : MyObjectBuilder_SessionComponent
    {
    }
}