using System;
using Equinox76561198048419394.Core.Debug;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.Game.Gui;
using VRage;
using VRage.Components;
using VRage.Components.Entity;
using VRage.Entity.EntityComponents;
using VRage.Game.Components;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem
{
    [MySessionComponent]
    public class RailwayDebugDrawRegistration : ModDebugScreenComponent
    {
        public override string FriendlyName => "Railway Debug Draw";
        public override Type ScreenType => typeof(RailwayDebugDraw);
        public override MyGuiScreenDebugBase Construct() => new RailwayDebugDraw();
    }
    
    public class RailwayDebugDraw : MyGuiScreenDebugBase
    {
        public override string GetFriendlyName() => "RailwayDebugDraw";

        public RailwayDebugDraw()
        {
            RecreateControls(true);
        }

        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);

            m_currentPosition = -m_size.Value / 2.0f + new Vector2(0.02f, 0.10f);
            m_currentPosition.Y += 0.01f;
            m_scale = 0.7f;

            AddCaption("Railway debug draw", Color.Yellow.ToVector4());
            AddShareFocusHint();

            AddCheckBox("Draw graph nodes", () => RailConstants.Debug.DrawGraphNodes, val => Write(ref RailConstants.Debug.DrawGraphNodes, val));
            AddCheckBox("Draw graph edges", () => RailConstants.Debug.DrawGraphEdges, val => Write(ref RailConstants.Debug.DrawGraphEdges, val));
            AddCheckBox("Draw grading shapes", () => RailConstants.Debug.DrawGradingShapes, val => Write(ref RailConstants.Debug.DrawGradingShapes, val));
            AddCheckBox("Draw switch controllers", () => RailConstants.Debug.DrawSwitchControllers, val => Write(ref RailConstants.Debug.DrawSwitchControllers, val));
            
            AddCheckBox("Draw bendy physics", () => RailConstants.Debug.DrawBendyPhysics, val => Write(ref RailConstants.Debug.DrawBendyPhysics, val));
            AddCheckBox("Draw bogie physics", () => RailConstants.Debug.DrawBogiePhysics, val => Write(ref RailConstants.Debug.DrawBogiePhysics, val));
            AddCheckBox("Draw bogie edges", () => RailConstants.Debug.DrawBogieEdges, val => Write(ref RailConstants.Debug.DrawBogieEdges, val));
        }

        private static void Write(ref bool target, bool value)
        {
            target = value;
            RailConstants.DebugCommit();
        }
    }
}