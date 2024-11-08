using Equinox76561198048419394.RailSystem.Bendy.Shape;
using Equinox76561198048419394.RailSystem.Definition;
using Equinox76561198048419394.RailSystem.Voxel;
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
            public bool DrawGradingFillShapes;
            public bool DrawGradingCutShapes;

            public bool DrawSdfCacheFill;
            public bool DrawSdfCacheCut;
            public bool DrawSdfDensityField;
        }

        public static DebugOptions Debug;

        public static float GravityCompensation = -1;
        public static bool ApplyOppositeDynamicGravityForces;
        public static bool ApplyOppositeDynamicNonGravityForces;

        static RailConstants()
        {
            DebugOff();
        }

        public static void DebugOn()
        {
            Debug = new DebugOptions
            {
                AssertsWithStacks = false, DrawGraphEdges = false, DrawGraphNodes = false, DrawBogiePhysics = true,
                DrawBendyPhysics = false, DrawSwitchControllers = true, DrawBogieEdges = true, DrawGradingFillShapes = true
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
            DebugDraw.SetEnabled(typeof(RailGraderSystem), Debug.DrawSdfCacheCut || Debug.DrawSdfCacheFill || Debug.DrawGradingCutShapes || Debug.DrawGradingFillShapes);
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

        public const float NodeMergeDistance = .05f;

        public static float AngularConstraintStrength = 1f;
        public static float LinearConstraintStrength = 1f;

        public const float NodeMergeDistanceSq = NodeMergeDistance * NodeMergeDistance;

        public const float NodeRoughDistance = 0.5f;
        public const float NodeRoughDistanceSq = NodeRoughDistance * NodeRoughDistance;

        public const float EdgeLineDistance = NodeRoughDistance;
        public const float EdgeLineDistanceSq = EdgeLineDistance * EdgeLineDistance;

        public const float EdgePlaneDistance = 10f;
        public const float EdgePlaneDistanceSq = EdgePlaneDistance * EdgePlaneDistance;
    }
}