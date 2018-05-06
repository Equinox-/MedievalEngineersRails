using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using ProtoBuf;
using VRage.Game;
using VRage.Game.Components;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.RailSystem.Bendy
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation, -10, typeof(MyObjectBuilder_BendyController))]
    public class BendyController : MySessionComponentBase
    {
        private readonly Dictionary<string, BendyLayer> _layers = new Dictionary<string, BendyLayer>(StringComparer.OrdinalIgnoreCase);

        public BendyLayer GetLayer(string id)
        {
            return _layers.GetValueOrDefault(id);
        }

        public BendyLayer GetOrCreateLayer(string id)
        {
            BendyLayer res;
            if (!_layers.TryGetValue(id, out res))
                _layers.Add(id, res = new BendyLayer(id.ToUpperInvariant()));
            return res;
        }

        public override void UpdateAfterSimulation()
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