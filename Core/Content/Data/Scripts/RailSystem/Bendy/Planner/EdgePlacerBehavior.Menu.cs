using System.Xml.Serialization;
using Equinox76561198048419394.Core.UI;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Attributes;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy.Planner
{
    public partial class EdgePlacerBehavior
    {
        private static bool _showMaxCurvature = true;
        private static bool _showMaxGrade = true;
        private static bool _snapToEdges = true;
        private static bool _snapToEdgePlanes = true;
        private static bool _snapToVoxels = false;
        private static bool _snapToVoxelGradient = false;

        private static float? _gradeHint;
        private static float? _directionHint;
        private static float _verticalShift;

        public string ToolContextMenuId => "EdgePlacerMenu";
        public object[] ToolContextMenuArguments => new object[] { this };

        [MyContextMenuContextType(typeof(MyObjectBuilder_EdgePlacerConfigContext))]
        public sealed class MenuContext : MyContextMenuContext
        {
            private static readonly MyStringId DirectionHint = MyStringId.GetOrCompute("DirectionHint");
            private static readonly MyStringId GradeHint = MyStringId.GetOrCompute("GradeHint");
            private static readonly MyStringId VerticalShift = MyStringId.GetOrCompute("VerticalShift");
            private static readonly MyStringId ShowMaxCurvatureDataSource = MyStringId.GetOrCompute("ShowMaxCurvature");
            private static readonly MyStringId ShowMaxGradeDataSource = MyStringId.GetOrCompute("ShowMaxGrade");
            private static readonly MyStringId SnapToEdgesDataSource = MyStringId.GetOrCompute("SnapToEdges");
            private static readonly MyStringId SnapToEdgePlanesDataSource = MyStringId.GetOrCompute("SnapToEdgePlanes");
            private static readonly MyStringId SnapToVoxelsDataSource = MyStringId.GetOrCompute("SnapToVoxels");
            private static readonly MyStringId SnapToVoxelGradientDataSource = MyStringId.GetOrCompute("SnapToVoxelGradient");

            private EdgePlacerBehavior _owner;

            public override void Init(object[] contextParams)
            {
                _owner = (EdgePlacerBehavior)contextParams[0];
                m_dataSources.Add(DirectionHint, SimpleDataSources.Simple(
                    () => _directionHint.HasValue ? MathHelper.ToDegrees(_directionHint.Value) : -1,
                    value => _directionHint = value >= 0 ? (float?)MathHelper.ToRadians(value) : null));
                m_dataSources.Add(GradeHint, SimpleDataSources.Simple(() => _gradeHint.HasValue ? _gradeHint.Value * 100 : -1,
                    value => _gradeHint = value >= 0 ? (float?)(value / 100) : null));
                m_dataSources.Add(VerticalShift, SimpleDataSources.SimpleRef(() => ref _verticalShift));
                m_dataSources.Add(ShowMaxCurvatureDataSource, SimpleDataSources.SimpleRef(() => ref _showMaxCurvature));
                m_dataSources.Add(ShowMaxGradeDataSource, SimpleDataSources.SimpleRef(() => ref _showMaxGrade));
                m_dataSources.Add(SnapToEdgesDataSource, SimpleDataSources.SimpleRef(() => ref _snapToEdges));
                m_dataSources.Add(SnapToEdgePlanesDataSource, SimpleDataSources.SimpleRef(() => ref _snapToEdgePlanes));
                m_dataSources.Add(SnapToVoxelsDataSource, SimpleDataSources.SimpleRef(() => ref _snapToVoxels));
                m_dataSources.Add(SnapToVoxelGradientDataSource, SimpleDataSources.SimpleRef(() => ref _snapToVoxelGradient));
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EdgePlacerConfigContext : MyObjectBuilder_Base
    {
    }
}