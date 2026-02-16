using System.Xml.Serialization;
using Equinox76561198048419394.Core.UI;
using Equinox76561198048419394.Core.Util;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Attributes;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Voxel
{
    public partial class RailGraderBehavior
    {
        public string ToolContextMenuId => "RailGraderMenu";
        public object[] ToolContextMenuArguments => new object[] { this };

        internal static class Settings
        {
            public static RailGradeComponentDefinition.ShapeOptions Fill;
            public static RailGradeComponentDefinition.ShapeOptions Excavate;
            public static bool DrawFill;
            public static bool DrawExcavate;
            public static bool ReplaceSurfaceMaterial = true;
        }

        void IToolWithUpdate.Update()
        {
            if (!Settings.DrawExcavate && !Settings.DrawFill) return;
            GatherGradeComponents(in Holder.PositionComp.WorldMatrixRef.Translation());
            foreach (var gradeComp in _gradeComponents)
            {
                if (Settings.DrawFill)
                    gradeComp.GetSupport(Settings.Fill)?.DrawShape(RailGraderSystem.FillColor);
                if (Settings.DrawExcavate)
                    gradeComp.GetExcavate(Settings.Excavate)?.DrawShape(RailGraderSystem.ExcavateColor);
            }
        }

        [MyContextMenuContextType(typeof(MyObjectBuilder_RailGraderBehaviorContext))]
        public class RailGraderBehaviorContext : MyContextMenuContext
        {
            public override void Init(object[] contextParams)
            {
                var system = MySession.Static.Components.Get<RailGraderSystem>();
                m_dataSources.Clear();
                RegisterShape("Fill", system.SupportPresets, () => ref Settings.Fill, () => ref Settings.DrawFill);
                RegisterShape("Excavate", system.ExcavationPresets, () => ref Settings.Excavate, () => ref Settings.DrawExcavate);
                m_dataSources.Add(MyStringId.GetOrCompute("FillReplaceSurface"), SimpleDataSources.SimpleRef(() => ref Settings.ReplaceSurfaceMaterial));
                return;

                void RegisterShape(string prefix,
                    SimpleDropdownDataSourceFactory<MyStringHash> presets,
                    SimpleRefDataSource<RailGradeComponentDefinition.ShapeOptions>.DelGetRef options,
                    SimpleRefDataSource<bool>.DelGetRef draw)
                {
                    m_dataSources.Add(MyStringId.GetOrCompute(prefix + "Draw"), SimpleDataSources.SimpleRef(draw));
                    m_dataSources.Add(MyStringId.GetOrCompute(prefix + "Preset"), presets.Create(() => ref options().Preset));
                    m_dataSources.Add(MyStringId.GetOrCompute(prefix + "WidthIncrease"), SimpleDataSources.SimpleRef(() => ref options().WidthIncrease));
                    m_dataSources.Add(MyStringId.GetOrCompute(prefix + "HeightIncrease"), SimpleDataSources.SimpleRef(() => ref options().HeightIncrease));
                    m_dataSources.Add(MyStringId.GetOrCompute(prefix + "AngleOfRepose"), SimpleDataSources.Simple(
                        () => MathHelper.ToDegrees(options().ReposeAngleRadians),
                        val => options().ReposeAngleRadians = MathHelper.ToRadians(val)));
                }
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailGraderBehaviorContext : MyObjectBuilder_Base
    {
    }
}