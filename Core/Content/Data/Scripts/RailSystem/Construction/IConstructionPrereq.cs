namespace Equinox76561198048419394.RailSystem.Construction
{
    public interface IConstructionPrereq
    {
        string IncompleteMessage { get; }
        bool IsComplete { get; }
    }
}