namespace Equinox76561198048419394.RailSystem
{
    public static class RailConstants
    {
        public struct DebugOptions
        {
            public bool DrawGraphNodes;
            public bool DrawGraphEdges;
            public bool AssertsWithStacks;
        }

        public static DebugOptions Debug = new DebugOptions {AssertsWithStacks = true, DrawGraphEdges = true, DrawGraphNodes = true};

        // Maximum nodes in a single call
        public const int MaxNodesPlaced = 128;

        // Factor applied to tolerances when doing long-placing
        public const float LongToleranceFactor = 0.75f;
        public const float LongBezControlLimit = 500f;

        public const float DefaultMinLength = 7f;
        public const float DefaultMaxLength = 13f;

        public const float DefaultMaxGradeRatio = .05f;
        public const float DefaultMaxAngleDegrees = 20f;

        /// <summary>
        /// Minimum spacing between nodes
        /// </summary>
        public const float NodeMergeDistance = 1f;

        public const float NodeMergeDistanceSq = NodeMergeDistance * NodeMergeDistance;
    }
}