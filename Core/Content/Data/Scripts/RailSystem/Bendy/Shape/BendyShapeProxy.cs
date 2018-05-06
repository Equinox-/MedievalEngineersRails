using VRage.Game.Components;
using VRage.Game.Entity;

namespace Equinox76561198048419394.RailSystem.Bendy.Shape
{
    public class BendyShapeProxy : MyEntityComponent
    {
        public MyEntity Owner { get; }
        
        public BendyShapeProxy(MyEntity bendy)
        {
            Owner = bendy;
            bendy.OnMarkForClose += ParentMarkedForClose; 
        }

        private void ParentMarkedForClose(MyEntity obj)
        {
            Entity?.Close();
        }
    }
}