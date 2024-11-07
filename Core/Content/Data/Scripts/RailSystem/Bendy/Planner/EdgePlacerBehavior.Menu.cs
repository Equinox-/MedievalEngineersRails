using System;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.UI;
using Equinox76561198048419394.RailSystem.Util.Curve;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Attributes;
using VRage.Game.Input;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy.Planner
{
    public partial class EdgePlacerBehavior
    {
        private readonly MyInputContext _edgePlacerControls = new MyInputContext("Edge placer controls");

        private static bool _showMaxCurvature = true;
        private static bool _showMaxGrade = true;

        private static float? _gradeHint;
        private static float? _directionHint;
        private static float _verticalShift;

        public EdgePlacerBehavior()
        {
            _edgePlacerControls.UnregisterAllActions();
            _edgePlacerControls.RegisterAction(MyStringHash.GetOrCompute("CharacterUse"), ToggleMenu);
        }

        private MyContextMenu _menu;

        private const string MenuSubtype = "EdgePlacerMenu";

        private void ToggleMenu()
        {
            if (_menu != null && _menu.Visible && MyContextMenuScreen.GetContextMenu(MenuSubtype) != null)
            {
                _menu.Close();
                _menu = null;
            }
            else
            {
                _menu?.Close();
                _menu = MyContextMenuScreen.OpenMenu(Holder, MenuSubtype, this);
            }
        }

        private void UpdateMenuContext()
        {
            var ctx = (MenuContext)_menu?.Context;
            if (ctx == null)
                return;
        }

        [MyContextMenuContextType(typeof(MyObjectBuilder_EdgePlacerConfigContext))]
        public sealed class MenuContext : MyContextMenuContext
        {
            private static readonly MyStringId SliderParams = MyStringId.GetOrCompute("SliderParams");
            private static readonly MyStringId ShowMaxCurvatureDataSource = MyStringId.GetOrCompute("ShowMaxCurvature");
            private static readonly MyStringId ShowMaxGradeDataSource = MyStringId.GetOrCompute("ShowMaxGrade");

            private EdgePlacerBehavior _owner;

            public override void Init(object[] contextParams)
            {
                _owner = (EdgePlacerBehavior)contextParams[0];
                m_dataSources.Add(SliderParams, new SimpleArrayDataSource<float>(3, index =>
                {
                    switch (index)
                    {
                        case 0:
                            return _directionHint.HasValue ? MathHelper.ToDegrees(_directionHint.Value) : -1;
                        case 1:
                            return _gradeHint.HasValue ? _gradeHint.Value * 100 : -1;
                        case 2:
                            return _verticalShift;
                        default:
                            throw new IndexOutOfRangeException();
                    }
                }, (index, value) =>
                {
                    switch (index)
                    {
                        case 0:
                            _directionHint = value >= 0 ? (float?)MathHelper.ToRadians(value) : null;
                            break;
                        case 1:
                            _gradeHint = value >= 0 ? (float?)(value / 100) : null;
                            break;
                        case 2:
                            _verticalShift = value;
                            break;
                        default:
                            throw new IndexOutOfRangeException();
                    }
                }));
                m_dataSources.Add(ShowMaxCurvatureDataSource, new SimpleRefDataSource<bool>(() => ref _showMaxCurvature));
                m_dataSources.Add(ShowMaxGradeDataSource, new SimpleRefDataSource<bool>(() => ref _showMaxGrade));
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EdgePlacerConfigContext : MyObjectBuilder_Base
    {
    }
}