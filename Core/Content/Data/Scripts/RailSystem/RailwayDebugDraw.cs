using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Debug;
using Equinox76561198048419394.Core.UI;
using Equinox76561198048419394.RailSystem.Voxel;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using VRage.Components.Entity.Camera;
using VRage.Game;
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
            var grading = MySession.Static.Components.Get<RailGraderSystem>();

            m_currentPosition = -m_size.Value / 2.0f + new Vector2(0.02f, 0.10f);
            m_currentPosition.Y += 0.01f;
            m_scale = 0.7f;

            AddCaption("Railway debug draw", Color.Yellow.ToVector4());
            AddShareFocusHint();

            AddLabel("Graph", Color.Yellow, 1);
            AddCheckBox("Draw graph nodes", () => RailConstants.Debug.DrawGraphNodes, val => Write(out RailConstants.Debug.DrawGraphNodes, val));
            AddCheckBox("Draw graph edges", () => RailConstants.Debug.DrawGraphEdges, val => Write(out RailConstants.Debug.DrawGraphEdges, val));
            AddCheckBox("Draw switch controllers", () => RailConstants.Debug.DrawSwitchControllers,
                val => Write(out RailConstants.Debug.DrawSwitchControllers, val));

            m_currentPosition.Y += 0.01f;
            AddLabel("Grading", Color.Yellow, 1);
            {
                AddShape("fill", () => ref RailConstants.Debug.DrawGradingFill, grading.SupportPresets);
                AddShape("cut", () => ref RailConstants.Debug.DrawGradingCut, grading.ExcavationPresets);

                void AddShape(string name, SimpleRefDataSource<RailGradeComponentDefinition.ShapeOptions>.DelGetRef opts,
                    SimpleDropdownDataSourceFactory<MyStringHash> presets)
                {
                    AddLabel($"Draw shape preset ({name})", Color.Yellow, 0.75f);
                    AddPresetsCombo(() => ref opts().Preset, presets);
                    AddSlider("Width Increase", opts().WidthIncrease, 0, 10, s => opts().WidthIncrease = s.Value);
                    AddSlider("Height Increase", opts().HeightIncrease, 0, 10, s => opts().HeightIncrease = s.Value);
                    AddSlider("Angle of Repose", MathHelper.ToDegrees(opts().ReposeAngleRadians), 0, 90,
                        s => opts().ReposeAngleRadians = MathHelper.ToRadians(s.Value));
                }

                void AddPresetsCombo(SimpleRefDataSource<MyStringHash>.DelGetRef getRef, SimpleDropdownDataSourceFactory<MyStringHash> presets)
                {
                    var combo = AddCombo();
                    combo.AddItem(-1, "Disabled");
                    var ds = presets.Create(getRef);
                    var items = new List<ContextMenuDropdownDataSource.DropdownItem>();
                    ds.GetItems(items);
                    for (var i = 0; i < items.Count; i++)
                        combo.AddItem(i, items[i].Text, toolTip: items[i].Tooltip);
                    combo.SelectItemByKey(getRef() == default ? -1 : ds.Selected);
                    combo.ItemSelected += (_, id) =>
                    {
                        if (id == -1)
                            getRef() = default;
                        else
                            ds.Selected = (int)id;
                        RailConstants.DebugCommit();
                    };
                }
            }
            AddCheckBox("Draw SDF cache (fill)", () => RailConstants.Debug.DrawSdfCacheFill, val => Write(out RailConstants.Debug.DrawSdfCacheFill, val));
            AddCheckBox("Draw SDF cache (cut)", () => RailConstants.Debug.DrawSdfCacheCut, val => Write(out RailConstants.Debug.DrawSdfCacheCut, val));
            AddCheckBox("Draw SDF cache (density)", () => RailConstants.Debug.DrawSdfDensityField,
                val => Write(out RailConstants.Debug.DrawSdfDensityField, val));
            AddLabel("Grading Preset", Color.Yellow, 0.75f);

            m_currentPosition.Y += 0.01f;
            AddLabel("Physics", Color.Yellow, 1);
            AddCheckBox("Draw bendy physics", () => RailConstants.Debug.DrawBendyPhysics, val => Write(out RailConstants.Debug.DrawBendyPhysics, val));
            AddCheckBox("Draw bogie physics", () => RailConstants.Debug.DrawBogiePhysics, val => Write(out RailConstants.Debug.DrawBogiePhysics, val));
            AddCheckBox("Draw bogie edges", () => RailConstants.Debug.DrawBogieEdges, val => Write(out RailConstants.Debug.DrawBogieEdges, val));

            m_currentPosition.Y += 0.01f;
            AddLabel("Physics options", Color.Yellow, 1);
            AddSlider("Angular constraint strength", 1, 0, 1, ctl => RailConstants.AngularConstraintStrength = ctl.Value);
            AddSlider("Linear constraint strength", 1, 0, 1, ctl => RailConstants.LinearConstraintStrength = ctl.Value);
            AddSlider("Gravity compensation", -1, -1, 1, ctl => RailConstants.GravityCompensation = ctl.Value);
            AddCheckBox("Dynamic back gravity forces", () => RailConstants.ApplyOppositeDynamicGravityForces,
                val => RailConstants.ApplyOppositeDynamicGravityForces = val);
            AddCheckBox("Dynamic back non-gravity forces", () => RailConstants.ApplyOppositeDynamicNonGravityForces,
                val => RailConstants.ApplyOppositeDynamicNonGravityForces = val);
        }

        private static void Write(out bool target, bool value)
        {
            target = value;
            RailConstants.DebugCommit();
        }
    }
}