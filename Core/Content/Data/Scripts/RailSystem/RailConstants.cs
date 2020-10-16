using Equinox76561198048419394.RailSystem.Bendy.Shape;
using Equinox76561198048419394.RailSystem.Definition;
using VRage.Components;

namespace Equinox76561198048419394.RailSystem
{
    public static class RailConstants
    {
        public struct DebugOptions
        {
            public bool DrawGraphNodes;
            public bool DrawGraphEdges;
            public bool AssertsWithStacks;
            public bool DrawBogiePhysics;
            public bool DrawBendyPhysics;
            public bool DrawSwitchControllers;
            public bool DrawBogieEdges;
            public bool DrawGradingShapes;
        }

        public static DebugOptions Debug;

        static RailConstants()
        {
            DebugOff();
        }

        public static void DebugOn()
        {
            Debug = new DebugOptions
            {
                AssertsWithStacks = false, DrawGraphEdges = false, DrawGraphNodes = false, DrawBogiePhysics = true,
                DrawBendyPhysics = false, DrawSwitchControllers = true, DrawBogieEdges = true, DrawGradingShapes = true
            };
            DebugCommit();
        }

        public static void DebugOff()
        {
            Debug = default;
            DebugCommit();
        }

        public static void DebugCommit()
        {
            DebugDraw.SetEnabled(typeof(RailSwitchExternalComponent), Debug.DrawSwitchControllers);
            DebugDraw.SetEnabled(typeof(RailSegmentComponent), Debug.DrawSwitchControllers);
            DebugDraw.SetEnabled(typeof(BendyPhysicsComponent), Debug.DrawBendyPhysics);
        }

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