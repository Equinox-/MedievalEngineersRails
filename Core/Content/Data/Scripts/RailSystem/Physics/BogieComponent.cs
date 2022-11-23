using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Controller;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.RailSystem.Bendy;
using Equinox76561198048419394.RailSystem.Construction;
using Equinox76561198048419394.RailSystem.Definition;
using Equinox76561198048419394.RailSystem.Util;
using Equinox76561198048419394.RailSystem.Util.Curve;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities.Entity.Stats;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.ModAPI;
using VRage;
using VRage.Components;
using VRage.Components.Block;
using VRage.Components.Entity;
using VRage.Components.Entity.CubeGrid;
using VRage.Components.Physics;
using VRage.Definitions.Components.Character;
using VRage.Entities.Gravity;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Components.Entity.Stats;
using VRage.Session;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.RailSystem.Physics
{
    [MyComponent(typeof(MyObjectBuilder_BogieComponent))]
    [MyDependency(typeof(MyModelAttachmentComponent), Critical = false, Recursive = false)]
    [MyDependency(typeof(MyBlockComponent), Critical = true)]
    [MyDefinitionRequired(typeof(BogieComponentDefinition))]
    public class BogieComponent : MyEntityComponent, IRailPhysicsComponent
    {
        private BogieComponentDefinition Definition { get; set; }
        private readonly PowerObserver _powerObserver = new PowerObserver();

        [Automatic]
        private readonly MyBlockComponent _blockComponent = null;

        [Automatic]
        private readonly MyModelAttachmentComponent _attacher = null;

        private const int SequentialSleepTicksBeforePause = 60;
        private const int AttemptSleepEvery = 60;

        private int _nextSleepAttempt = 0;
        private int _sequentialSleepingTicks = 0;

        private readonly SerializableEdge _prevEdge = new SerializableEdge();

        public RailPhysicsNode PhysicsNode { get; }

        public BogieComponent()
        {
            PhysicsNode = new RailPhysicsNode(this);
        }

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            Definition = (BogieComponentDefinition) definition;
        }

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            _powerObserver.OnAddedToContainer(Container);
            PhysicsNode.AddToContainer();
        }

        public override void OnBeforeRemovedFromContainer()
        {
            _powerObserver.OnRemovedFromContainer();
            PhysicsNode.RemoveFromContainer();
            base.OnBeforeRemovedFromContainer();
        }

        private BendyLayer _graph;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _graph = MySession.Static.Components.Get<BendyController>()?.GetOrCreateLayer(Definition.Layer);
            if (_attacher != null)
            {
                _attacher.OnEntityAttached += FixupSkinEntity;
                foreach (var e in _attacher.GetAttachedEntities(SkinHash))
                    FixupSkinEntity(_attacher, e);
            }

            _blockComponent.Move += BlockMoved;
            BlockMoved(_blockComponent, _blockComponent.GridData);
            _powerObserver.RequiredPower = Definition.NeedsPower;
        }

        private void FixupSkinEntity(MyModelAttachmentComponent attacher, MyEntity entity)
        {
            var pt = attacher.GetEntityAttachmentPoint(entity);
            if (pt != SkinHash)
                return;

            if (!entity.Components.Contains<MyAnimationControllerComponent>())
                return;

            // Inflate local AABB to fix bad lodding.
            var pos = entity.PositionComp;
            pos.LocalAABB = BoundingBox.CreateFromHalfExtent(pos.LocalAABB.Center, pos.LocalAABB.HalfExtents * 5);
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (_attacher != null)
            {
                _attacher.OnEntityAttached -= FixupSkinEntity;
            }

            _blockComponent.Move -= BlockMoved;
            BlockMoved(null, null);
        }

        private MyPhysicsComponentBase _gridPhysicsComponent;
        private MyPositionComponentBase _gridPositionComponent;

        private void BlockMoved(MyBlockComponent block, MyGridDataComponent gridData)
        {
            var nextGridPhys = gridData?.Container?.Get<MyPhysicsComponentBase>();
            var nextGridPos = gridData?.Container?.Get<MyPositionComponentBase>();
            if (nextGridPos == _gridPositionComponent && nextGridPhys == _gridPhysicsComponent)
                return;
            if (_gridPositionComponent != null)
                _gridPositionComponent.OnPositionChanged -= WakePhysics;
            _gridPhysicsComponent = nextGridPhys;
            _gridPositionComponent = nextGridPos;
            if (_gridPositionComponent != null)
                WakePhysics(null);
            else
                RemoveFixedUpdate(Simulate);
        }

        private bool _physicsAwake;

        private void WakePhysics(MyPositionComponentBase obj)
        {
            if (_gridPositionComponent != null)
                _gridPositionComponent.OnPositionChanged -= WakePhysics;
            if (!_physicsAwake)
            {
                // Don't reset physics sleep cooldown (_nextSleepAttempt) but do reset the update scheduler sleep cooldown.
                _sequentialSleepingTicks = 0;
                _physicsAwake = true;
                AddFixedUpdate(Simulate);
            }
        }

        private void SleepPhysics()
        {
            if (_physicsAwake)
            {
                if (_gridPositionComponent != null)
                    _gridPositionComponent.OnPositionChanged += WakePhysics;
                _physicsAwake = false;
                RemoveFixedUpdate(Simulate);
            }
        }

        private static readonly MyStringHash SkinHash = MyStringHash.GetOrCompute("Skin");
        private static readonly MyStringId DebugMtl = MyStringId.GetOrCompute("Square");
        private static readonly MyStringId SpeedZVar = MyStringId.GetOrCompute("rail_speed_z");
        private static readonly MyStringId BrakingVar = MyStringId.GetOrCompute("brake");

        private readonly HashSet<MyEntity> _activeControllers = new HashSet<MyEntity>();
        private readonly HashSet<MyEntity> _goodControllers = new HashSet<MyEntity>();
        private readonly List<MyEntity> _removedControllers = new List<MyEntity>();

        private static readonly MyStringId AnimRailSync = MyStringId.GetOrCompute("equi_rail_sync");

        private void SetAnimVar(MyStringId key, float val)
        {
            if (_attacher != null)
                foreach (var e in _attacher.GetAttachedEntities(SkinHash))
                    e.Components.Get<MyAnimationControllerComponent>()?.Variables.SetValue(key, val);
            Entity.Components.Get<MyAnimationControllerComponent>()?.Variables.SetValue(key, val);

            MyAnimationControllerComponent acc;
            foreach (var k in _activeControllers)
                if (k.Components.TryGet(out acc))
                    acc.Variables.SetValue(key, val);
        }

        private void FindAttachedAnimControllers(MyEntity e, HashSet<MyEntity> dest)
        {
            foreach (var k in e.Components.GetComponents<EquiPlayerAttachmentComponent>())
            foreach (var ch in k.GetAttachedCharacters())
                dest.Add(ch);
            if (e.Hierarchy != null)
                foreach (var child in e.Hierarchy.Children)
                    FindAttachedAnimControllers(child.Entity, dest);
        }

        private static readonly MyDefinitionId SprintingEffect =
            new MyDefinitionId(typeof(MyObjectBuilder_CompositeEntityEffect), MyStringHash.GetOrCompute("Sprint"));

        public const float SwitchingDistanceBias = 0.33f;
        public const float AlignmentTangentBias = 0.0125f;
        public const float TotalBias = SwitchingDistanceBias + AlignmentTangentBias;

        [Update(false)]
        private void ControllerSync(long dt)
        {
            var attt = Entity.Components.Get<MyModelAttachmentComponent>();
            if (attt != null)
                foreach (var e in attt.GetAttachedEntities(SkinHash))
                    e.Components.Get<MyAnimationControllerComponent>()?.TriggerAction(AnimRailSync);
            Entity.Components.Get<MyAnimationControllerComponent>()?.TriggerAction(AnimRailSync);
            foreach (var k in _activeControllers)
                k.Get<MyAnimationControllerComponent>()?.TriggerAction(AnimRailSync);
        }

        private struct SimulationResult
        {
            /// <summary>
            /// Is non-static and near a bendy edge.
            /// </summary>
            public bool Active;

            public float AnimationVelocity;

            public FindEdgeResult FindEdgeResult;

            public bool AllowDeactivation;
            public Vector3 Gravity;
            public Vector3 PoweredImpulse;
            public Vector3 ConstraintImpulse;
            public Vector3 AngularImpulse;
        }

        private struct FindEdgeResult
        {
            public float EdgeFactor;
            public double Score;
            public Vector3 Tangent;
            public RailSegmentDefinition.RailSegmentCaps SegmentCaps;

            public Vector3 Normal;
            public Vector3 Up;
            public Vector3D Position;
            public IEdge Edge;
            public MyPhysicsComponentBase Physics;
        }

        private static RailSegmentComponent RailSegmentFor(Edge edge)
        {
            return edge.Owner.Container.Get<RailSegmentComponent>();
        }

        private bool CanUseEdge(Edge prevEdgeReal, Edge edge)
        {
            if (prevEdgeReal == null || prevEdgeReal == edge)
                return true;

            Node commonJunction;
            if (prevEdgeReal.Opposition(edge.From) != null)
                commonJunction = edge.From;
            else if (prevEdgeReal.Opposition(edge.To) != null)
                commonJunction = edge.To;
            else
                commonJunction = null;
            if (commonJunction == null)
                return true;

            var edgeOpposite = edge.Opposition(commonJunction);
            var sourceNode = prevEdgeReal.Opposition(commonJunction);
            var sourceNodeData = commonJunction.Get<SwitchableNodeData>();
            var switchableSourceData = sourceNodeData?.SideFor(sourceNode);
            var switchableThisData = sourceNodeData?.SideFor(edgeOpposite);

            if (switchableSourceData == null || switchableThisData == null)
                return true;

            if (switchableSourceData == switchableThisData)
            {
                if (RailConstants.Debug.DrawBogieEdges)
                    edge.Draw(0, 1, new Vector4(1, 0, 0, 1), 2);
                // We're coming from this direction, and it isn't the edge we used in the previous tick,
                // so we can't jump to it.
                return false;
            }

            if (switchableThisData.SwitchedEdge == edge)
                return true;

            if (RailConstants.Debug.DrawBogieEdges)
                edge.Draw(0, 1, new Vector4(1, 0, 0, 1), 2);
            // We're coming from this direction, and it isn't the edge we used in the previous tick,
            // so we can't jump to it.
            return false;
        }

        private struct PartialFindEdgeResult
        {
            public float EdgeFactor;
            public double Score;
            public RailSegmentDefinition.RailSegmentCaps SegmentCaps;
            public IEdge Edge;
        }

        private static void VisitEdge(
            IEdge edge,
            ref Vector3D bogieWorldPos,
            ref Vector3D bogieWorldTangent,
            RailSegmentDefinition.RailSegmentCaps edgeCaps,
            ref PartialFindEdgeResult result)
        {
            var invTransform = edge.TransformInv;
            var bogieLocalPos = Vector3D.TransformNoProjection(ref bogieWorldPos, ref invTransform);
            var bogieLocalTangent = Vector3D.TransformNormal(bogieWorldTangent, ref invTransform);
            var curve = edge.Curve;
            float t0 = 0, t1 = 1;
            CurveExtensions.NearestPoint(curve, bogieLocalPos, 16, ref t0, ref t1);
            var p0 = curve.Sample(t0);
            var p1 = curve.Sample(t1);
            var dir = p1 - p0;
            var factor = (float) MathHelper.Clamp(dir.Dot(bogieLocalPos - p0) / dir.LengthSquared(), 0, 1);
            var t = t0 + (t1 - t0) * factor;
            var pos = curve.Sample(t);
            var score = Vector3D.Distance(pos, bogieLocalPos);
            if (score - TotalBias > result.Score)
                return;
            var tangent = (Vector3) curve.SampleDerivative(t);
            tangent.Normalize();

            const float switchingEpsilon = 0.25f;
            var switched = false;
            var realEdge = edge as Edge;
            if (realEdge != null)
            {
                if (t < switchingEpsilon)
                    switched = realEdge.From.IsSwitchedTo(realEdge.To);
                else if (t >= 1 - switchingEpsilon)
                    switched = realEdge.To.IsSwitchedTo(realEdge.From);
            }

            var forwardDotTangent = Math.Abs(bogieLocalTangent.Dot(tangent));
            if (switched)
                score -= SwitchingDistanceBias + AlignmentTangentBias;
            else
                score -= AlignmentTangentBias * forwardDotTangent;

            if (RailConstants.Debug.DrawBogieEdges && realEdge != null)
                realEdge.Draw(0, 1, switched ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0, 1, 1), 2);

            // ReSharper disable once InvertIf
            if (score < result.Score)
            {
                result.Score = score;
                result.Edge = edge;
                result.SegmentCaps = edgeCaps;
                result.EdgeFactor = t;
            }
        }

        private bool TryFindEdge(out FindEdgeResult result)
        {
            var prevEdgeReal = _prevEdge?.GetEdge(_graph);
            if (RailConstants.Debug.DrawBogieEdges && prevEdgeReal != null)
                prevEdgeReal.Draw(0, 1, new Vector4(0, 1, 1, 1), 3);
            var bogiePos = Entity.PositionComp.WorldMatrix.Translation;
            var bogieTangent = Entity.PositionComp.WorldMatrix.Forward;

            var partialResult = default(PartialFindEdgeResult);
            partialResult.Score = Definition.DetachDistance + TotalBias;
            
            using (var e = _graph.Edges.SortedByDistance(bogiePos))
                while (e.MoveNext())
                {
                    if (Math.Sqrt(e.Current.DistanceSquared) - TotalBias > partialResult.Score)
                        break;
                    var edge = (Edge) e.Current.UserData;
                    if (edge.Curve == null)
                        continue;
                    var edgeSegment = RailSegmentFor(edge);
                    var edgeCaps = edgeSegment?.Definition.CapabilitiesFor(edge.Owner.Entity.GetBuildRatio());
                    if (edgeSegment == null || !edgeCaps.HasValue)
                        continue; // no capabilities at this stage

                    if (!CanUseEdge(prevEdgeReal, edge))
                        continue;
                    VisitEdge(edge, ref bogiePos, ref bogieTangent, edgeCaps.Value, ref partialResult);
                }

            using (var e = _graph.DynamicEdges.SortedByDistance(bogiePos))
                while (e.MoveNext())
                {
                    if (Math.Sqrt(e.Current.DistanceSquared) - TotalBias > partialResult.Score)
                        break;
                    var component = (DynamicBendyComponent) e.Current.UserData;
                    var edgeSegment = component.Container.Get<RailSegmentComponent>();
                    var edgeCaps = edgeSegment?.Definition.CapabilitiesFor(component.Entity.GetBuildRatio());
                    if (edgeSegment == null || !edgeCaps.HasValue)
                        continue; // no capabilities at this stage

                    foreach (var edge in component.LocalEdges)
                    {
                        VisitEdge(edge, ref bogiePos, ref bogieTangent, edgeCaps.Value, ref partialResult);
                    }
                }

            result = default;
            if (partialResult.Score > Definition.DetachDistance)
                return false;
            result.Edge = partialResult.Edge;
            result.EdgeFactor = partialResult.EdgeFactor;
            result.Score = partialResult.Score;
            result.SegmentCaps = partialResult.SegmentCaps;
            var transform = partialResult.Edge.Transform;
            result.Up = (Vector3) Vector3D.TransformNormal(
                Vector3D.Lerp(partialResult.Edge.FromUp, partialResult.Edge.ToUp, result.EdgeFactor),
                ref transform);
            // Not aligned vertically, abort
            if (Entity.PositionComp.WorldMatrix.Up.Dot(result.Up) < 0.5)
                return false;

            result.Position = Vector3D.Transform(partialResult.Edge.Curve.Sample(partialResult.EdgeFactor), ref transform)
                              + result.Up * Definition.VerticalOffset;
            result.Tangent = (Vector3) Vector3D.TransformNormal(partialResult.Edge.Curve.SampleDerivative(partialResult.EdgeFactor), ref transform);
            if (result.Tangent.Dot((Vector3) bogieTangent) < 0)
                result.Tangent = -result.Tangent;
            result.Tangent.Normalize();

            result.Normal = Vector3.Cross(result.Tangent, result.Up);
            result.Normal.Normalize();

            result.Up = Vector3.Cross(result.Normal, result.Tangent);
            result.Up.Normalize();
            result.Physics = (partialResult.Edge as DynamicBendyComponent.DynamicEdge)?.Owner.Entity.ParentedPhysics();
            return true;
        }

        private void LinkNeighbors(in FindEdgeResult edgeResult)
        {
            if (!RailPhysicsNode.IsAuthority)
                return;
            using (VRage.Library.Collections.PoolManager.Get(out HashSet<RailPhysicsNode> forRemoval))
            {
                PhysicsNode.GetNeighbors(forRemoval);
                // Add neighbor edges to the nearby node
                if (edgeResult.Edge is Edge realEdge)
                {
                    var searchNode = edgeResult.EdgeFactor < 0.5 ? realEdge.From : realEdge.To;
                    foreach (var edge in searchNode.Edges)
                    {
                        var physicsNode = RailSegmentFor(edge)?.PhysicsNode;
                        if (RailwayPhysicsGroup.Enabled && physicsNode != null && !forRemoval.Remove(physicsNode))
                            PhysicsNode.Link(physicsNode);
                    }

                    // Don't remove anything within two hops
                    foreach (var neighbor1 in searchNode.Neighbors)
                    foreach (var edge in neighbor1.Edges)
                    {
                        if (forRemoval.Count == 0)
                            break;
                        var physicsNode = RailSegmentFor(edge)?.PhysicsNode;
                        if (physicsNode != null)
                            forRemoval.Remove(physicsNode);
                    }
                }

                foreach (var removal in forRemoval)
                    PhysicsNode.Unlink(removal);
            }
        }

        private void SolveTorque(ref FindEdgeResult edgeResult, ref SimulationResult simResult)
        {
            ref var up = ref edgeResult.Up;
            ref var tangent = ref edgeResult.Tangent;

            var inertiaTensor = _gridPhysicsComponent.InertiaTensor;
            var entityWorldMatrix = (Matrix) Entity.PositionComp.WorldMatrix;
            var gridWorldMatrix = (Matrix) _gridPositionComponent.WorldMatrix;
            var gridWorldMatrixInv = (Matrix) _gridPositionComponent.WorldMatrixInvScaled;

            var qCurrent = Quaternion.CreateFromRotationMatrix(entityWorldMatrix);
            var qDesired = Quaternion.CreateFromRotationMatrix(Matrix.CreateWorld(Vector3.Zero, tangent, up));
            var qErrorEntity = Quaternion.Multiply(Quaternion.Conjugate(qCurrent), qDesired);
            var angVelDesiredEntity = 2 * qErrorEntity.W * new Vector3(qErrorEntity.X, qErrorEntity.Y, qErrorEntity.Z)
                                      / MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;
            var angVelDesiredGrid = Vector3.TransformNormal(Vector3.TransformNormal(angVelDesiredEntity, ref entityWorldMatrix), ref gridWorldMatrixInv);
            // Don't boost the angular velocity up too high -- 10 rad/sec is the max
            var angVelScalarSq = angVelDesiredGrid.LengthSquared();
            if (angVelScalarSq > 100)
                angVelDesiredGrid *= (float) Math.Sqrt(100 / angVelScalarSq);
            var angVelCurrentGrid = Vector3.TransformNormal(_gridPhysicsComponent.AngularVelocity, ref gridWorldMatrixInv);

            if (angVelDesiredGrid.LengthSquared() > .01f && angVelCurrentGrid.LengthSquared() > 1e-2f)
                simResult.AllowDeactivation = false;
            var localUp = Vector3.TransformNormal(up, ref gridWorldMatrixInv);
            var angVelDelta = angVelDesiredGrid - angVelCurrentGrid;

            var angVelDeltaUp = Vector3.Dot(angVelDelta, localUp) * localUp;
            var angVelDeltaOther = angVelDelta - angVelDeltaUp;
            var angVelDeltaCorrected = (angVelDeltaUp * Definition.OrientationConvergenceFactorHorizontal) +
                                       (angVelDeltaOther * Definition.OrientationConvergenceFactor);
            var angularImpulseWorld = Vector3.TransformNormal(Vector3.TransformNormal(angVelDeltaCorrected, inertiaTensor), ref gridWorldMatrix);
            simResult.AngularImpulse = angularImpulseWorld
                * RailConstants.AngularConstraintStrength / MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;
        }

        private void SolveLinearImpulse(ref FindEdgeResult edgeResult, in Vector3 gravityHere, in Vector3 gridVelocity, ref SimulationResult simResult)
        {
            var position = Entity.GetPosition();
            var effectiveMass = _gridPhysicsComponent.Mass;

            ref var up = ref edgeResult.Up;
            ref var tangent = ref edgeResult.Tangent;
            ref var normal = ref edgeResult.Normal;
            ref var curvePosition = ref edgeResult.Position;

            // a) spring joint along normal to get dot(normal, (pivot*matrix - position)) == 0
            var err = (Vector3) (curvePosition - position);

            // preemptive up force to counteract gravity.
            var gravityImpulse = Vector3.Dot(gravityHere, up) * up * effectiveMass * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;
            simResult.ConstraintImpulse -= gravityImpulse * RailConstants.GravityCompensation;

            simResult.ConstraintImpulse += SolveImpulse(err, normal, gridVelocity, effectiveMass);

            // b) half spring joint along up to get dot(up, (pivot*matrix - position)) >= 0
            simResult.ConstraintImpulse += SolveImpulse(err, up, gridVelocity, effectiveMass, 1); // only up force

            // Is the grid notably moving at all, and (side-to-side spring too far || up spring too far || moving along tangent) 
            if (gridVelocity.LengthSquared() > 1e-2f &&
                (Math.Abs(err.Dot(normal)) > .01f || Math.Max(0, err.Dot(up)) > .025f || Math.Abs(gridVelocity.Dot(tangent)) > .01f))
                simResult.AllowDeactivation = false;
        }

        private void SolvePhysicsLegacy(ref FindEdgeResult edgeResult, ref SimulationResult simResult)
        {
            var position = Entity.GetPosition();
            var effectiveMass = _gridPhysicsComponent.Mass;
            var inertiaTensor = _gridPhysicsComponent.InertiaTensor;

            ref var up = ref edgeResult.Up;
            ref var tangent = ref edgeResult.Tangent;
            ref var normal = ref edgeResult.Normal;
            ref var curvePosition = ref edgeResult.Position;

            var qCurrent = Quaternion.CreateFromRotationMatrix(Entity.PositionComp.WorldMatrix);
            var qDesired = Quaternion.CreateFromRotationMatrix(Matrix.CreateWorld(Vector3.Zero, tangent, up));
            var qConj = Quaternion.Multiply(Quaternion.Conjugate(qCurrent), qDesired);
            var localAngularDesired = 2 * qConj.W * new Vector3(qConj.X, qConj.Y, qConj.Z);
            var gridAngularVelocity = _gridPhysicsComponent.AngularVelocity;
            if (localAngularDesired.LengthSquared() > .01f && gridAngularVelocity.LengthSquared() > 1e-2f)
                simResult.AllowDeactivation = false;
            var desiredAngular = Vector3.Transform(localAngularDesired, qCurrent) * 2;
            var dAngAccel = desiredAngular - 0.25f * gridAngularVelocity;

            var dAngAccelUp = Vector3.Dot(dAngAccel, up) * up;
            var dAngAccelOther = dAngAccel - dAngAccelUp;
            var dAngAccelCorrected = (dAngAccelUp * Definition.OrientationConvergenceFactorHorizontal) +
                                     (dAngAccelOther * Definition.OrientationConvergenceFactor);
            simResult.AngularImpulse = Vector3.TransformNormal(dAngAccelCorrected, inertiaTensor)
                * RailConstants.AngularConstraintStrength / MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;
            var gridVelocity = _gridPhysicsComponent.LinearVelocity;

            // a) spring joint along normal to get dot(normal, (pivot*matrix - position)) == 0
            var err = (Vector3) (curvePosition - position);

            // preemptive up force to counteract gravity.
            simResult.Gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(position);
            simResult.ConstraintImpulse += Vector3.Dot(simResult.Gravity, up) * up * effectiveMass * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;

            simResult.ConstraintImpulse += SolveImpulse(err, normal, gridVelocity, effectiveMass);

            // b) half spring joint along up to get dot(up, (pivot*matrix - position)) >= 0
            simResult.ConstraintImpulse += SolveImpulse(err, up, gridVelocity, effectiveMass, 1); // only up force

            // Is the grid notably moving at all, and (side-to-side spring too far || up spring too far || moving along tangent) 
            if (gridVelocity.LengthSquared() > 1e-2f &&
                (Math.Abs(err.Dot(normal)) > .01f || Math.Max(0, err.Dot(up)) > .025f || Math.Abs(gridVelocity.Dot(tangent)) > .01f))
                simResult.AllowDeactivation = false;
        }

        private void SimulationDryRun(out SimulationResult simResult)
        {
            simResult = default;
            simResult.Active = false;
            if (_gridPhysicsComponent == null)
                return;
            var entityWorldMatrix = Entity.PositionComp.WorldMatrix;
            var position = entityWorldMatrix.Translation;
            simResult.AnimationVelocity = _gridPhysicsComponent.GetVelocityAtPoint(position).Dot((Vector3) entityWorldMatrix.Forward);
            if (_gridPositionComponent == null || _gridPhysicsComponent.IsStatic || !TryFindEdge(out simResult.FindEdgeResult))
                return;
            LinkNeighbors(in simResult.FindEdgeResult);

            simResult.Active = true;
            ref var edgeResult = ref simResult.FindEdgeResult;

            simResult.ConstraintImpulse = Vector3.Zero;
            simResult.AllowDeactivation = true;
            var effectiveMass = _gridPhysicsComponent.Mass;

            ref var up = ref edgeResult.Up;
            ref var tangent = ref edgeResult.Tangent;
            var gridVelocity = _gridPhysicsComponent.LinearVelocity - (edgeResult.Physics?.GetVelocityAtPoint(edgeResult.Position) ?? Vector3.Zero);
            var gravityHere = MyGravityProviderSystem.CalculateTotalGravityInPoint(position);

            SolveTorque(ref edgeResult, ref simResult);
            SolveLinearImpulse(ref edgeResult, in gravityHere, in gridVelocity, ref simResult);

            var braking = false;
            // Hack until I fix EquinoxCore
            if (Definition.MaxVelocity > 0 && (_powerObserver.IsPowered || Definition.NeedsPower == PowerObserver.RequiredPowerEnum.None))
            {
                var trackVelocity = gridVelocity.Dot(tangent);
                var velocityMod = 0f;

                // get nearest character
                var totalControl = 0f;
                var controllers = 0;
                var sprinters = 0;
                foreach (var player in _activeControllers)
                {
                    var component = player?.Get<MyCharacterMovementComponent>();
                    if (component == null)
                        continue;
                    var control = Vector3.TransformNormal(component.MoveIndicator, player.WorldMatrix).Dot(tangent);
                    totalControl += control;
                    controllers++;

                    var speed = new MyMovementSpeed {Forward = 1f};
                    var speedMod = component.ApplyMovementSpeedEffects(speed);
                    velocityMod = Math.Max(velocityMod, speedMod.Forward);

                    if (!component.WantsSprint) continue;
                    var stats = component.Container?.Get<MyEntityStatComponent>();
                    stats?.AddEffect(SprintingEffect);
                    sprinters++;
                }

                if (controllers > 0 && Math.Abs(totalControl) > 1e-3f)
                {
                    braking |= Math.Sign(trackVelocity) != Math.Sign(totalControl) && Math.Abs(trackVelocity) > 0.01 &&
                               Math.Abs(totalControl) > 0.01;
                    SetAnimVar(BrakingVar, braking ? 1 : 0);
                    _powerFactor = MathHelper.Clamp(_powerFactor * (1 - PowerSmooth) + totalControl * PowerSmooth, -1, 1);
                    if (Math.Abs(totalControl) < .01f)
                        _powerFactor = 0;

                    var velocityLimit = Definition.MaxVelocity * velocityMod;
                    velocityLimit = Math.Min(velocityLimit, edgeResult.SegmentCaps.MaxSpeed);

                    var forceFactorBase = MathHelper.Clamp(1 - Math.Abs(trackVelocity) / velocityLimit, 0, 1);
                    var forceFactorControl = Math.Abs(_powerFactor);
                    if (sprinters > 0)
                        forceFactorControl *= 5 * ((float) sprinters) / controllers;
                    forceFactorControl *= .9f + controllers * .1f;

                    var dir = Math.Sign(_powerFactor);
                    if (dir != Math.Sign(trackVelocity))
                        forceFactorBase = 1;

                    simResult.PoweredImpulse = Definition.MaxForce * forceFactorControl * forceFactorBase * dir * tangent *
                                               MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;
                    simResult.AllowDeactivation = false;
                }
            }


            {
                var frictiveNormalForce = Math.Max(0, simResult.ConstraintImpulse.Dot(up) / MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS);

                var frictiveCoefficient = Math.Max(Definition.CoefficientOfFriction, braking ? Definition.BrakingCoefficientOfFriction : 0);
                if (gridVelocity.LengthSquared() > .01f)
                    frictiveCoefficient *= 0.75f;
                var frictiveForce = frictiveCoefficient * edgeResult.SegmentCaps.Friction * frictiveNormalForce;

                // clamp frictive impulse to at-max stopping.
                var tangentMomentumAfterUpdate =
                    (effectiveMass * (gridVelocity + gravityHere * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS) + simResult.ConstraintImpulse).Dot(
                        tangent);

                var frictiveFloatImpulse = frictiveForce * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;

                Vector3 frictiveImpulse;
                if (frictiveFloatImpulse > Math.Abs(tangentMomentumAfterUpdate))
                    frictiveImpulse = -tangentMomentumAfterUpdate * tangent;
                else
                    frictiveImpulse = -Math.Sign(tangentMomentumAfterUpdate) * frictiveFloatImpulse * tangent;

                simResult.ConstraintImpulse += frictiveImpulse;
            }
        }

        [Update(MyEngineConstants.UPDATE_STEP_SIZE_IN_MILLISECONDS * 10)]
        public void FindControllers(long dt)
        {
            _goodControllers.Clear();
            FindAttachedAnimControllers(Entity, _goodControllers);

            _removedControllers.Clear();
            foreach (var k in _activeControllers)
                if (!_goodControllers.Contains(k))
                    _removedControllers.Add(k);

            foreach (var k in _removedControllers)
                _activeControllers.Remove(k);
            _removedControllers.Clear();

            var sync = false;
            var wake = false;
            foreach (var k in _goodControllers)
            {
                if (_activeControllers.Add(k))
                    sync = wake = true;
                var ctl = k?.Get<MyCharacterMovementComponent>();
                if (ctl != null && ctl.MoveIndicator != Vector3.Zero)
                    wake = true;
            }

            if (sync)
                AddScheduledCallback(ControllerSync, 30);
            if (wake)
                WakePhysics(null);
        }

        private DateTime _lastErrorLog;
        private bool _failedPreviousFrame;

        [FixedUpdate]
        private void Simulate()
        {
            var position = Entity.GetPosition();
            SimulationResult simulationResult;
            try
            {
                SimulationDryRun(out simulationResult);
                _failedPreviousFrame = false;
            }
            catch (Exception err)
            {
                if (!_failedPreviousFrame && (DateTime.Now - _lastErrorLog) > TimeSpan.FromMinutes(1))
                {
                    this.GetLogger().Warning($"Failure when running bogie simulation on {Entity.Id}: {err}");
                    _lastErrorLog = DateTime.Now;
                }
                _failedPreviousFrame = true;
                return;
            }
            _prevEdge.SetEdge(simulationResult.FindEdgeResult.Edge as Edge);
            if (!simulationResult.Active)
            {
                SetAnimVar(SpeedZVar, 0);
                SleepPhysics();
                return;
            }

            SetAnimVar(SpeedZVar, simulationResult.AnimationVelocity);

            if (!MyAPIGateway.Utilities.IsDedicated && RailConstants.Debug.DrawBogiePhysics)
            {
                var drawPivot = position + 4 * simulationResult.FindEdgeResult.Up;
                var colorTarget = Vector4.One;
                MySimpleObjectDraw.DrawLine(position, simulationResult.FindEdgeResult.Position, DebugMtl, ref colorTarget,
                    .01f);

                var colorImpulse = new Vector4(1, 0, 0, 1);
                MySimpleObjectDraw.DrawLine(drawPivot, drawPivot + (simulationResult.PoweredImpulse + simulationResult.ConstraintImpulse), DebugMtl,
                    ref colorImpulse,
                    .01f);

                var colorAngImpulse = new Vector4(0, 0, 1, 1);
                MySimpleObjectDraw.DrawLine(drawPivot, drawPivot + simulationResult.AngularImpulse, DebugMtl, ref colorAngImpulse,
                    .01f);

                var tensor = (MatrixD) _gridPhysicsComponent.InertiaTensor;
                tensor /= tensor.Scale.Length();
                tensor.Translation = Vector3D.Zero;
                tensor = _gridPositionComponent.WorldMatrix * tensor;
                tensor.Translation = _gridPhysicsComponent.GetCenterOfMassWorld();
                MyRenderProxy.DebugDrawOBB(tensor, Color.Red, 1, false, false);

                var allowed = simulationResult.AllowDeactivation ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0, 0, 1);
                var sleeping = _gridPhysicsComponent.IsActive ? new Vector4(1, 0, 0, 1) : new Vector4(0, 1, 0, 1);
                var v0 = position + simulationResult.FindEdgeResult.Up * 2;
                var v1 = position + simulationResult.FindEdgeResult.Up * 2.2f;
                var shift = simulationResult.FindEdgeResult.Tangent * .2f;
                MySimpleObjectDraw.DrawLine(v0 + shift, v1 + shift, DebugMtl, ref allowed, 0.1f);
                MySimpleObjectDraw.DrawLine(v0 - shift, v1 - shift, DebugMtl, ref sleeping, 0.1f);

                var currPrevEdge = _prevEdge.GetEdge(_graph);
                if (currPrevEdge != null)
                {
                    var prevNodeLink = new Vector4(1, 1, 1, 1);
                    currPrevEdge.Draw(0, 1, prevNodeLink, 4);
                }
            }

            if (simulationResult.AllowDeactivation)
            {
                if (_nextSleepAttempt <= 0)
                {
                    if (SleepUtility.AttemptPhysicsSleep(_gridPhysicsComponent.Entity))
                    {
                        if (_sequentialSleepingTicks < SequentialSleepTicksBeforePause)
                            _sequentialSleepingTicks++;
                        else
                            SleepPhysics();
                        return;
                    }

                    // We attempted to sleep, but failed.  Try again in ATTEMPT_SLEEP_EVERY
                    _nextSleepAttempt = AttemptSleepEvery;
                }
                else
                {
                    // We could sleep, but we're still on cooldown -- decrease cooldown.
                    _nextSleepAttempt--;
                }
            }
            else
            {
                // We can't sleep -- reset cooldown
                _nextSleepAttempt = 0;
            }

            _sequentialSleepingTicks = 0;

            var totalImpulse = simulationResult.ConstraintImpulse + simulationResult.PoweredImpulse; 
            _gridPhysicsComponent.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE,
                totalImpulse, _gridPhysicsComponent.GetCenterOfMassWorld(), Vector3.Zero);
            _gridPhysicsComponent.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE, Vector3.Zero, Entity.GetPosition(),
                simulationResult.AngularImpulse);

            if (simulationResult.FindEdgeResult.Physics != null &&
                !simulationResult.FindEdgeResult.Physics.IsStatic &&
                (RailConstants.ApplyOppositeDynamicGravityForces || RailConstants.ApplyOppositeDynamicNonGravityForces))
            {
                var opposite = -totalImpulse;
                var gravForce = opposite.Project(simulationResult.Gravity);
                var nonGravForce = opposite - gravForce;
                var filtered = (RailConstants.ApplyOppositeDynamicGravityForces ? gravForce : Vector3.Zero) +
                            (RailConstants.ApplyOppositeDynamicNonGravityForces ? nonGravForce : Vector3.Zero);
                simulationResult.FindEdgeResult.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE,
                    filtered, simulationResult.FindEdgeResult.Position, Vector3.Zero);
            }
        }


        private const float PowerSmooth = .005f;
        private float _powerFactor;

        private static Vector3 SolveImpulse(Vector3 error, Vector3 dir, Vector3 vel,
            float mass, int signFlags = 3)
        {
            var errorOnAxis = dir.Dot(error);
            var velOnAxis = dir.Dot(vel);

// velOnAxis = -errorOnAxis / 1 step
            var desiredVel = errorOnAxis / (MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS * 2);
            if ((signFlags & 1) == 0 && errorOnAxis > 0)
                return Vector3.Zero;
            if ((signFlags & 2) == 0 && errorOnAxis < 0)
                return Vector3.Zero;
            return RailConstants.LinearConstraintStrength * dir * (desiredVel - velOnAxis) * mass;
        }

        public override bool IsSerialized => _prevEdge.GetEdge(_graph) != null;

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_BogieComponent) base.Serialize(copy);
            var currEdge = _prevEdge.GetEdge(_graph);
            ob.PrevEdgeFrom = currEdge?.From.Position;
            ob.PrevEdgeTo = currEdge?.To.Position;
            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_BogieComponent) builder;
            if (ob.PrevEdgeFrom != null && ob.PrevEdgeTo != null)
                _prevEdge.SetEdge(ob.PrevEdgeFrom.Value, ob.PrevEdgeTo.Value);
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_BogieComponent : MyObjectBuilder_EntityComponent
    {
        public SerializableVector3D? PrevEdgeFrom;

        public SerializableVector3D? PrevEdgeTo;
    }
}