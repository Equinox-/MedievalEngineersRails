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

            AddLabel("Graph", Color.Yellow, 1);
            AddCheckBox("Draw graph nodes", () => RailConstants.Debug.DrawGraphNodes, val => Write(out RailConstants.Debug.DrawGraphNodes, val));
            AddCheckBox("Draw graph edges", () => RailConstants.Debug.DrawGraphEdges, val => Write(out RailConstants.Debug.DrawGraphEdges, val));
            AddCheckBox("Draw switch controllers", () => RailConstants.Debug.DrawSwitchControllers, val => Write(out RailConstants.Debug.DrawSwitchControllers, val));

            m_currentPosition.Y += 0.01f;
            AddLabel("Grading", Color.Yellow, 1);
            AddCheckBox("Draw shapes (fill)", () => RailConstants.Debug.DrawGradingFillShapes, val => Write(out RailConstants.Debug.DrawGradingFillShapes, val));
            AddCheckBox("Draw shapes (cut)", () => RailConstants.Debug.DrawGradingCutShapes, val => Write(out RailConstants.Debug.DrawGradingCutShapes, val));
            AddCheckBox("Draw SDF cache (fill)", () => RailConstants.Debug.DrawSdfCacheFill, val => Write(out RailConstants.Debug.DrawSdfCacheFill, val));
            AddCheckBox("Draw SDF cache (cut)", () => RailConstants.Debug.DrawSdfCacheCut, val => Write(out RailConstants.Debug.DrawSdfCacheCut, val));
            AddCheckBox("Draw SDF cache (density)", () => RailConstants.Debug.DrawSdfDensityField, val => Write(out RailConstants.Debug.DrawSdfDensityField, val));

            m_currentPosition.Y += 0.01f;
            AddLabel("Physics", Color.Yellow, 1);
            AddCheckBox("Draw bendy physics", () => RailConstants.Debug.DrawBendyPhysics, val => Write(out RailConstants.Debug.DrawBendyPhysics, val));
            AddCheckBox("Draw bogie physics", () => RailConstants.Debug.DrawBogiePhysics, val => Write(out RailConstants.Debug.DrawBogiePhysics, val));
            AddCheckBox("Draw bogie edges", () => RailConstants.Debug.DrawBogieEdges, val => Write(out RailConstants.Debug.DrawBogieEdges, val));

            m_currentPosition.Y += 0.01f;
            AddLabel("Physics options", Color.Yellow, 1);
            AddSlider("Angular constraint strength", 1, 0, 1, ctl => RailConstants.AngularConstraintStrength = ctl.Value);
            AddSlider("Linear constraint strength", 1, 0, 1, ctl => RailConstants.LinearConstraintStrength = ctl.Value);
        }

        private static void Write(out bool target, bool value)
        {
            target = value;
            RailConstants.DebugCommit();
        }
    }
}