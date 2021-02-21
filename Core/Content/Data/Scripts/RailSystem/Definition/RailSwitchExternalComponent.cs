using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.RailSystem.Bendy;
using Equinox76561198048419394.RailSystem.Physics;
using Equinox76561198048419394.RailSystem.Util;
using Medieval.Entities.UseObject;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Entity;
using VRage.Components.Entity.Camera;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Utils;
using VRageMath;
using PoolManager = VRage.Library.Collections.PoolManager;

namespace Equinox76561198048419394.RailSystem.Definition
{
    [MyComponent(typeof(MyObjectBuilder_RailSwitchExternalComponent))]
    [MyDefinitionRequired(typeof(RailSwitchInternalComponentDefinition))]
    [MyDependency(typeof(MyComponentEventBus), Critical = false)]
    [MyDependency(typeof(MyModelAttachmentComponent), Critical = false)]
    [ReplicatedComponent]
    public class RailSwitchExternalComponent : MyEntityComponent, IMyGenericUseObjectInterface, IMyComponentEventProvider, IMyEventProxy, IRailSwitch,
        IComponentDebugDraw, IRailPhysicsComponent
    {
        private const string EventName = "Switched";

        private Node _controllerJunction;
        private SwitchableNodeSide _controller;
        private readonly List<BendyLayer> _layers = new List<BendyLayer>();
        private MyComponentEventBus _eventBus;
        private MyModelAttachmentComponent _modelAttachment;
        private BendyController _bendyController;
        
        public RailPhysicsNode PhysicsNode { get; }

        public RailSwitchExternalComponent()
        {
            PhysicsNode = new RailPhysicsNode(this);
        }

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            _eventBus = Container.Get<MyComponentEventBus>();
            _modelAttachment = Container.Get<MyModelAttachmentComponent>();
            PhysicsNode.AddToContainer();
        }

        public override void OnBeforeRemovedFromContainer()
        {
            PhysicsNode.RemoveFromContainer();
            _eventBus = null;
            _modelAttachment = null;
            base.OnBeforeRemovedFromContainer();
        }

        private TimeSpan _animationWarpEndTime;

        public void FlagAnimationWarp()
        {
            _animationWarpEndTime = MySession.Static.ElapsedGameTime + TimeSpan.FromMilliseconds(50);
        }

        private void BindLayer(BendyLayer l)
        {
            _layers.Add(l);
            l.NodeCreated += OnNodeMoved;
            l.NodeMoved += OnNodeMoved;
            l.NodeRemoved += OnNodeRemoved;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _layers.Clear();
            _bendyController = MySession.Static.Components.Get<BendyController>();
            foreach (var kv in _bendyController.Layers)
                OnLayerAdded(kv.Key, kv.Value);
            _bendyController.LayerAdded += OnLayerAdded;
            if (Definition.AllowDynamic)
                Entity.PositionComp.OnPositionChanged += OnPositionChanged;
            FlagAnimationWarp();

            _dirty = false;
            MarkDirty();
        }

        private void OnLayerAdded(string arg1, BendyLayer arg2)
        {
            if (Definition.AcceptLayer(arg1))
                BindLayer(arg2);
        }

        private bool _dirty;

        private void MarkDirty()
        {
            if (Entity == null || !Entity.InScene)
                return;
            if (!_dirty)
                AddScheduledCallback(RunDirty);
            _dirty = true;
        }

        [Update(false)]
        private void RunDirty(long dt)
        {
            _dirty = false;
            CheckNode();
        }

        private void OnPositionChanged(MyPositionComponentBase obj)
        {
            CheckNode();
        }

        public override void OnRemovedFromScene()
        {
            if (Entity != null)
                Entity.PositionComp.OnPositionChanged -= OnPositionChanged;
            foreach (var l in _layers)
            {
                l.NodeCreated -= OnNodeMoved;
                l.NodeMoved -= OnNodeMoved;
                l.NodeRemoved -= OnNodeRemoved;
            }

            _bendyController.LayerAdded -= OnLayerAdded;
            _layers.Clear();
            _bendyController = null;

            BindController(null);
            base.OnRemovedFromScene();
        }

        private static readonly MyStringId SquareMaterial = MyStringId.GetOrCompute("Square");

        public void DebugDraw()
        {
            if (!RailConstants.Debug.DrawSwitchControllers)
                return;
            if (((IMyUtilities) MyAPIUtilities.Static).IsDedicated)
                return;

            const float vertOffset = 1f;
            if (Entity == null || !Entity.InScene)
                return;
            if (Vector3D.DistanceSquared(MyCameraComponent.ActiveCamera.GetPosition(), Entity.GetPosition()) > 100 * 100)
                return;
            var intersection = _controller?.Junction;
            if (intersection == null)
                return;

            var intersectPos = intersection.Position + intersection.Up * vertOffset + _controller.Tangent * 0.5f;
            var connection = new LineD(Entity.GetPosition(), intersectPos);
            MyTransparentGeometry.AddLineBillboard(SquareMaterial, new Vector4(1, 0, 1, 1), connection.From, (Vector3) connection.Direction,
                (float) connection.Length, 0.05f);
        }

        private void CheckNode()
        {
            if (Entity == null || !Entity.InScene)
            {
                BindController(null);
                return;
            }

            if (!Definition.AllowDynamic)
            {
                var phys = Entity.ParentedPhysics();
                if (phys != null && !phys.IsStatic)
                {
                    BindController(null);
                    return;
                }
            }

            var samplePos = Vector3D.Transform(Definition.ControllerLocation, Entity.WorldMatrix);
            Node best = null;
            var bestDistSq = (double) Definition.MaxAttachmentDistance * Definition.MaxAttachmentDistance;
            foreach (var l in _layers)
                using (var e = l.Nodes.SortedByDistance(samplePos))
                    while (e.MoveNext())
                    {
                        var curr = e.Current;
                        if (curr.DistanceSquared > bestDistSq)
                            break;
                        var candidate = (Node) curr.UserData;
                        if (candidate == null)
                            continue;
                        var good = false;
                        foreach (var edge in candidate.Edges)
                            if (edge.Owner.Container?.Get<RailSegmentComponent>() != null)
                            {
                                good = true;
                                break;
                            }

                        if (!good)
                            continue;
                        bestDistSq = curr.DistanceSquared;
                        best = candidate;
                    }

            BindController(best);
        }

        private void OnNodeRemoved(Node obj)
        {
            if (obj == _controllerJunction || _controllerJunction?.ConnectionTo(obj) != null)
                CheckNode();
        }

        private void OnNodeMoved(Node obj)
        {
            if (obj == null)
                return;
            var samplePos = Vector3D.Transform(Definition.ControllerLocation, Entity.WorldMatrix);
            var movedDist = Vector3D.DistanceSquared(samplePos, obj.Position);
            if (obj == _controllerJunction || _controllerJunction?.ConnectionTo(obj) != null)
            {
                MarkDirty();
                return;
            }

            var bestDistSq = _controllerJunction != null && _controllerJunction.InScene
                ? Vector3D.DistanceSquared(samplePos, _controllerJunction.Position)
                : Definition.MaxAttachmentDistanceSq;
            if (movedDist <= bestDistSq)
                MarkDirty();
        }

        private void BindController(Node intersection)
        {
            SwitchableNodeSide newController = null;
            if (intersection != null)
            {
                var data = SwitchableNodeData.GetOrCreate(intersection);

                bool useNegative;
                var negativeOptions = 0;
                var positiveOptions = 0;
                foreach (var k in intersection.Neighbors)
                {
                    if (SwitchableNodeSide.IsValidForSwitch(intersection, k, true))
                        negativeOptions++;
                    else
                        positiveOptions++;
                }

                var positiveWanted = (Entity.GetPosition() - intersection.Position).Dot(intersection.Tangent) > 0;

                if (positiveOptions >= 2 && positiveWanted)
                    useNegative = false;
                else if (negativeOptions >= 2 && !positiveWanted)
                    useNegative = true;
                else if (negativeOptions >= 2 && data.Negative == null)
                    useNegative = true;
                else if (positiveOptions >= 2 && data.Positive == null)
                    useNegative = false;
                else if (negativeOptions >= 2)
                    useNegative = true;
                else
                    useNegative = false;

                newController = useNegative ? data.NegativeOrCreate : data.PositiveOrCreate;
            }

            var newJunction = newController?.Junction;


            if (newController != _controller)
            {
                if (_controller != null)
                    _controller.SwitchChanged -= OnSwitchChanged;
                _controller = newController;
                if (_controller != null)
                {
                    _controller.SwitchChanged += OnSwitchChanged;
                    FlagAnimationWarp();
                    if (_controller.Target != null)
                        OnSwitchChanged(_controller, _controller.Junction, _controller.Target);
                }
            }

            // ReSharper disable once InvertIf
            if (newJunction != _controllerJunction)
            {
                if (_controllerJunction != null)
                {
                    _controllerJunction.NeighborAdded -= NeighborsChanged;
                    _controllerJunction.NeighborRemoved -= NeighborsChanged;
                }

                _controllerJunction = newJunction;
                // ReSharper disable once InvertIf
                if (_controllerJunction != null)
                {
                    _controllerJunction.NeighborAdded += NeighborsChanged;
                    _controllerJunction.NeighborRemoved += NeighborsChanged;
                }
            }

            LinkNeighbors();
        }

        private void NeighborsChanged(Node self, Node target, Edge via)
        {
            MarkDirty();
        }

        private void LinkNeighbors()
        {
            if (!RailPhysicsNode.IsAuthority)
                return;
            using (PoolManager.Get(out HashSet<RailPhysicsNode> neighborsForRemoval))
            {
                PhysicsNode.GetNeighbors(neighborsForRemoval);
                if (_controllerJunction != null)
                    foreach (var edge in _controllerJunction.Edges)
                    {
                        var neighbor = edge.Owner?.Container?.Get<IRailPhysicsComponent>()?.PhysicsNode;
                        if (RailwayPhysicsGroup.Enabled && neighbor != null && !neighborsForRemoval.Remove(neighbor))
                            PhysicsNode.Link(neighbor);
                    }

                foreach (var neighbor in neighborsForRemoval)
                    PhysicsNode.Unlink(neighbor);
            }
        }

        public RailSwitchExternalComponentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (RailSwitchExternalComponentDefinition) def;
        }

        public void Switch()
        {
            _controller?.SwitchRelative(1);
        }

        #region Use Object

        public void Use(string dummyName, UseActionEnum actionEnum, MyEntity user)
        {
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            if (actionEnum != UseActionEnum.Manipulate)
                return;
            Switch();
        }

        private static readonly MyStringId TextSwitch = MyStringId.GetOrCompute("Switch");
        private static readonly MyStringId TextConfig = MyStringId.GetOrCompute("Change Side");

        public MyActionDescription GetActionInfo(string dummyName, UseActionEnum actionEnum)
        {
            switch (actionEnum)
            {
                case UseActionEnum.Manipulate:
                    return new MyActionDescription {IsPrimary = true, Text = TextSwitch};
                case UseActionEnum.OpenTerminal:
                    return new MyActionDescription {IsPrimary = false, Text = TextConfig};
                default:
                    return default(MyActionDescription);
            }
        }

        public UseActionEnum SupportedActions => PrimaryAction | SecondaryAction | UseActionEnum.UseFinished;
        public UseActionEnum PrimaryAction => _controller != null ? UseActionEnum.Manipulate : UseActionEnum.None;
        public UseActionEnum SecondaryAction => UseActionEnum.None;
        public bool ContinuousUsage => false;

        #endregion

        public bool HasEvent(string eventName)
        {
            return eventName == EventName;
        }

        #region Animator

        private bool _isAnimating;

        private void BeginAnimating()
        {
            if (_isAnimating || Entity == null || !Entity.InScene)
                return;
            _isAnimating = true;
            AddFixedUpdate(Animate);
        }

        private void StopAnimating()
        {
            if (!_isAnimating)
                return;
            _isAnimating = false;
            RemoveFixedUpdate(Animate);
        }

        private void OnSwitchChanged(SwitchableNodeSide self, Node junction, Node destination)
        {
            BeginAnimating();
            _eventBus?.Invoke(EventName);
        }

        [FixedUpdate(false)]
        private void Animate()
        {
            const float eps = 1e-3f;

            if (_controller?.Target == null || _modelAttachment == null)
                return;

            var warp = MySession.Static.ElapsedGameTime < _animationWarpEndTime;

            var cotangent = Vector3.Cross(_controller.Junction.Up, _controller.Junction.Tangent);
            cotangent.Normalize();

            var canAmplify = true;
            if (_controller.Candidates.Count > 3)
                canAmplify = false;
            else
            {
                var hasPositive = false;
                var hasNegative = false;
                foreach (var candidate in _controller.Candidates)
                {
                    var dest = candidate.Opposition(_controller.Junction);
                    var coTanDot = (dest.Position - _controller.Junction.Position).Dot(cotangent);
                    if (coTanDot < -eps)
                    {
                        if (hasNegative)
                        {
                            canAmplify = false;
                            break;
                        }

                        hasNegative = true;
                    }
                    else if (coTanDot > eps)
                    {
                        if (hasPositive)
                        {
                            canAmplify = false;
                            break;
                        }

                        hasPositive = true;
                    }
                }
            }

            var direction = _controller.Target.Position - _controller.Junction.Position;
            direction.Normalize();

            var dirty = false;
            foreach (var animator in Definition.Animators)
            {
                var animatorDirection = direction;

                if (canAmplify)
                {
                    var proj = animatorDirection.Dot(cotangent);
                    animatorDirection += proj * animator.Amplify * Vector3D.Distance(_controller.Target.Position, _controller.Junction.Position) *
                                         (Vector3D) cotangent;
                    animatorDirection.Normalize();
                }

                _modelAttachment.SetAttachmentPointManagedState(animator.Attachment, false);
                foreach (var attached in _modelAttachment.GetAttachedEntities(animator.Attachment))
                {
                    var localDirection = Vector3.TransformNormal(animatorDirection, attached.PositionComp.WorldMatrixNormalizedInv);
                    localDirection = Vector3.Reject(localDirection, animator.Axis);
                    if (localDirection.Normalize() <= eps)
                        continue;
                    var cross = Vector3.Cross(animator.Arrow, localDirection);
                    var crossLength = cross.Normalize();
                    if (crossLength <= eps)
                        continue;
                    var modAngle = Math.Min(warp ? float.MaxValue : animator.RadiansPerSec * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS,
                        (float) Math.Abs(Math.Asin(crossLength)));
                    if (modAngle <= eps)
                        continue;
                    dirty = true;
                    var modMatrix = Matrix.CreateFromAxisAngle(cross, modAngle);
                    attached.PositionComp.LocalMatrix = modMatrix * attached.PositionComp.LocalMatrix;
                }
            }

            if (!dirty)
                StopAnimating();
        }

        #endregion
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailSwitchExternalComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_RailSwitchExternalComponentDefinition))]
    public class RailSwitchExternalComponentDefinition : MyEntityComponentDefinition
    {
        private readonly HashSet<string> _layers = new HashSet<string>();

        public float MaxAttachmentDistance { get; private set; }
        public float MaxAttachmentDistanceSq { get; private set; }
        public bool AllowDynamic { get; private set; }

        public HashSetReader<string> Layers => _layers;
        public List<ImmutableAnimator> Animators { get; } = new List<ImmutableAnimator>();

        public Vector3 ControllerLocation { get; private set; }

        public bool AcceptLayer(string ly)
        {
            if (_layers.Contains(ly))
                return true;
            foreach (var k in _layers)
                if (k.EndsWith("*") && k.StartsWith("*"))
                {
                    if (ly.IndexOf(k.Substring(1, k.Length - 2), StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                else if (k.StartsWith("*"))
                {
                    if (ly.EndsWith(k.Substring(1), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else if (k.EndsWith("*"))
                {
                    if (ly.StartsWith(k.Substring(0, k.Length - 1), StringComparison.OrdinalIgnoreCase))
                        return true;
                }

            return false;
        }

        public struct ImmutableAnimator
        {
            public readonly MyStringHash Attachment;
            public readonly float RadiansPerSec;
            public readonly Vector3 Axis;
            public readonly Vector3 Arrow;
            public readonly float Amplify;

            public ImmutableAnimator(MyObjectBuilder_RailSwitchExternalComponentDefinition.SwitchAnimator ob)
            {
                Attachment = MyStringHash.GetOrCompute(ob.Attachment);
                RadiansPerSec = MathHelper.ToRadians(ob.Rate ?? 45f);
                Axis = Vector3.Normalize(ob.Axis);
                Arrow = Vector3.Normalize(ob.Arrow);
                Amplify = ob.Amplify ?? 0f;
            }
        }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_RailSwitchExternalComponentDefinition) def;

            _layers.Clear();
            if (ob.Layers != null)
                foreach (var lay in ob.Layers)
                    _layers.Add(lay);

            MaxAttachmentDistance = ob.MaxAttachmentDistance ?? 1f;
            MaxAttachmentDistanceSq = MaxAttachmentDistance * MaxAttachmentDistance;
            ControllerLocation = ob.ControllerLocation ?? Vector3.Zero;
            AllowDynamic = ob.AllowDynamic ?? false;

            Animators.Clear();
            if (ob.Animators != null)
                foreach (var k in ob.Animators)
                    Animators.Add(new ImmutableAnimator(k));
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_RailSwitchExternalComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [XmlElement("Layer")]
        public string[] Layers;

        public float? MaxAttachmentDistance;

        public SerializableVector3? ControllerLocation;

        public bool? AllowDynamic;

        [XmlElement("Animator")]
        public SwitchAnimator[] Animators;

        public struct SwitchAnimator
        {
            [XmlAttribute]
            public string Attachment;

            [XmlElement]
            public float? Rate;

            [XmlElement]
            public SerializableVector3 Axis;

            [XmlElement]
            public SerializableVector3 Arrow;

            [XmlElement]
            public float? Amplify;
        }
    }
}