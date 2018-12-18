using Equinox76561198048419394.RailSystem.Bendy;

namespace Equinox76561198048419394.RailSystem.Definition
{
    public class SwitchableNodeData
    {
        public readonly Node Owner;
        public SwitchableNodeSide Negative { get; private set; }
        public SwitchableNodeSide Positive { get; private set; }

        public SwitchableNodeData(Node n)
        {
            Owner = n;
        }

        public SwitchableNodeSide NegativeOrCreate => Negative ?? (Negative = new SwitchableNodeSide(Owner, true));
        public SwitchableNodeSide PositiveOrCreate => Positive ?? (Positive = new SwitchableNodeSide(Owner, false));

        public bool IsSwitchedTo(Node other)
        {
            return SideFor(other)?.IsSwitchedTo(other) ?? false;
        }

        public void Destroy()
        {
            Negative?.Destroy();
            Negative = null;
            Positive?.Destroy();
            Positive = null;
        }

        internal void DebugDraw()
        {
            Negative?.DebugDraw();
            Positive?.DebugDraw();
        }

        public SwitchableNodeSide SideFor(Node n)
        {
            return SwitchableNodeSide.IsValidForSwitch(Owner, n, true) ? Negative : Positive;
        }

        public SwitchableNodeSide SideOrCreateFor(Node n)
        {
            return SwitchableNodeSide.IsValidForSwitch(Owner, n, true) ? NegativeOrCreate : PositiveOrCreate;
        }

        public void SwitchTo(Node n)
        {
            var edge = Owner.ConnectionTo(n);
            SideOrCreateFor(n).SwitchTo(edge);
        }

        public static SwitchableNodeData GetOrCreate(Node n)
        {
            return n.GetOrAdd(x => new SwitchableNodeData((Node) x));
        }
    }

    public static class SwitchableNodeExtensions
    {
        public static bool IsSwitchedTo(this Node junction, Node other)
        {
            var data = junction.Get<SwitchableNodeData>();
            return data != null && data.IsSwitchedTo(other);
        }

        public static void SwitchTo(this Node junction, Node destination)
        {
            if (junction.ConnectionTo(destination) == null) return;
            SwitchableNodeData.GetOrCreate(junction).SwitchTo(destination);
        }
    }
}