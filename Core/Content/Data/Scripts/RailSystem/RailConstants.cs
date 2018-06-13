namespace Equinox76561198048419394.RailSystem
{
    public static class RailConstants
    {
        public const float BendableSegmentLength = 10f;

        public const float DetachDistance = 2.5f;

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
