using System;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.UI;
using Equinox76561198048419394.RailSystem.Util.Curve;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Attributes;
using VRage.Game.Input;
using VRage.Input;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy.Planner
{
    public partial class EdgePlacerBehavior
    {
        private readonly MyInputContext _edgePlacerControls = new MyInputContext("Edge placer controls");

        private static bool _showMaxCurvature = false;
        private static bool _showMaxGrade = false;

        private float _smoothness = CubicCurve.DefaultSmoothness;
        private float? _gradeHint;
        private float? _directionHint;
        private float _verticalShift;

        private void ApplyV2Options(ref TempEdgePlan.NodeData node)
        {
            node.Smoothness = _smoothness;
            node.PinnedDirection = _directionHint;
            node.PinnedGrade = _gradeHint;
            node.VerticalShift = _verticalShift;
            node.Dirty = true;
        }

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
            if (ctx.UpdateFromOwner())
                _menu.Update();
        }

        [MyContextMenuContextType(typeof(MyObjectBuilder_EdgePlacerConfigContext))]
        public sealed class MenuContext : MyContextMenuContext
        {
            private static readonly MyStringId SliderParams = MyStringId.GetOrCompute("SliderParams");
            private static readonly MyStringId Mode = MyStringId.GetOrCompute("Mode");
            private static readonly MyStringId ShowMaxCurvatureDataSource = MyStringId.GetOrCompute("ShowMaxCurvature");
            private static readonly MyStringId ShowMaxGradeDataSource = MyStringId.GetOrCompute("ShowMaxGrade");

            private EdgePlacerBehavior _owner;

            public override void Init(object[] contextParams)
            {
                _owner = (EdgePlacerBehavior)contextParams[0];
                UpdateFromOwner();

                m_dataSources.Add(SliderParams, new SimpleArrayDataSource<float>(4, index =>
                {
                    switch (index)
                    {
                        case 0:
                            return _owner._smoothness;
                        case 1:
                            return _owner._directionHint.HasValue ? MathHelper.ToDegrees(_owner._directionHint.Value) : -1;
                        case 2:
                            return _owner._gradeHint.HasValue ? _owner._gradeHint.Value * 100 : -1;
                        case 3:
                            return _owner._verticalShift;
                        default:
                            throw new IndexOutOfRangeException();
                    }
                }, (index, value) =>
                {
                    var hasNode = _owner._tempPlan.TryGetNearestNode(_owner.LookingAtPosition, out var theoreticalNode, RailConstants.NodeMergeDistanceSq);
                    switch (index)
                    {
                        case 0:
                            _owner._smoothness = value;
                            if (hasNode)
                                _owner._tempPlan.GetNode(theoreticalNode).Smoothness = value;
                            break;
                        case 1:
                            _owner._directionHint = value >= 0 ? (float?)MathHelper.ToRadians(value) : null;
                            break;
                        case 2:
                            _owner._gradeHint = value >= 0 ? (float?)(value / 100) : null;
                            break;
                        case 3:
                            _owner._verticalShift = value;
                            break;
                        default:
                            throw new IndexOutOfRangeException();
                    }

                    MaybeUpdateNode();
                }));
                m_dataSources.Add(ShowMaxCurvatureDataSource, new SimpleRefDataSource<bool>(() => ref _showMaxCurvature));
                m_dataSources.Add(ShowMaxGradeDataSource, new SimpleRefDataSource<bool>(() => ref _showMaxGrade));
                m_dataSources.Add(Mode, new SimpleArrayDataSource<bool>(3,
                    index => index == (int)_owner._mode,
                    (index, value) =>
                    {
                        if (!value) return;
                        var newMode = (ModeV2)index;
                        if (newMode != _owner._mode)
                            _owner.SetMode(newMode);
                    }
                ));
            }

            private TempEdgePlan.NodeId? _prevNodeId;

            public bool UpdateFromOwner()
            {
                if (!_owner._tempPlan.TryGetNearestNode(_owner.LookingAtPosition, out var nearbyNode, RailConstants.NodeMergeDistanceSq))
                {
                    _prevNodeId = null;
                    return false;
                }

                if (_prevNodeId == nearbyNode)
                    return false;
                _prevNodeId = nearbyNode;
                ref var node = ref _owner._tempPlan.GetNode(nearbyNode);
                _owner._smoothness = node.Smoothness;
                _owner._directionHint = node.PinnedDirection;
                _owner._gradeHint = node.PinnedGrade;
                _owner._verticalShift = node.VerticalShift;
                return true;
            }

            public void PinNode()
            {
                if (!_owner._tempPlan.TryGetNearestNode(_owner.LookingAtPosition, out var nearbyNode, RailConstants.NodeMergeDistanceSq))
                    return;
                _owner._tempPlan.PinNode(nearbyNode);
                ref var node = ref _owner._tempPlan.GetNode(nearbyNode);
                _owner._directionHint = node.PinnedDirection;
                _owner._gradeHint = node.PinnedGrade;
            }

            private void MaybeUpdateNode()
            {
                if (!_owner._tempPlan.TryGetNearestNode(_owner.LookingAtPosition, out var nearbyNode, RailConstants.NodeMergeDistanceSq))
                    return;
                ref var node = ref _owner._tempPlan.GetNode(nearbyNode);
                _owner.ApplyV2Options(ref node);
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EdgePlacerConfigContext : MyObjectBuilder_Base
    {
    }
}