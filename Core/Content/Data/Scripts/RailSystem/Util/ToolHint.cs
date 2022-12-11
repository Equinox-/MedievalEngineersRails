using VRage.GUI.Crosshair;

namespace Equinox76561198048419394.RailSystem.Util
{
    public readonly struct ToolHint
    {
        public readonly string Text;
        public readonly MyCrosshairIconInfo? Icon;

        public ToolHint(string text, MyCrosshairIconInfo? icon)
        {
            Text = text;
            Icon = icon;
        }

        public static implicit operator ToolHint(string text) => new ToolHint(text, null);
    }
}