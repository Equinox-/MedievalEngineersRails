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
using VRage.Components;
using VRage.Components.Entity;
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

namespace Equinox76561198048419394.RailSystem.Physics
{
    [MyComponent(typeof(MyObjectBuilder_BogieComponent))]
    [MyDependency(typeof(MyModelAttachmentComponent), Critical = false, Recursive = false)]
    [MyDefinitionRequired(typeof(BogieComponentDefinition))]
    public class BogieComponent : MyEntityComponent
    {
        public BogieComponentDefinition Definition { get; private set; }
        private readonly PowerObserver _powerObserver = new PowerObserver();

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            Definition = (BogieComponentDefinition) definition;
        }

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            _powerObserver.OnAddedToContainer(Container);
        }

        public override void OnBeforeRemovedFromContainer()
        {
            _powerObserver.OnRemovedFromContainer();
            base.OnBeforeRemovedFromContainer();
        }

        private BendyLayer Graph;

        private MyModelAttachmentComponent _attacher;

        public override void OnAddedToScene()
        {
            Graph = MySession.Static.Components.Get<BendyController>()?.GetOrCreateLayer(Definition.Layer);
            _attacher = Container.Get<MyModelAttachmentComponent>();
            if (_attacher != null)
            {
                _attacher.OnEntityAttached += FixupSkinEntity;
                foreach (var e in _attacher.GetAttachedEntities(SkinHash))
                    FixupSkinEntity(_attacher, e);
            }

            base.OnAddedToScene();
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
                _attacher = null;
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

        [FixedUpdate]
        private void Simulate()
        {
            var root = Entity;
            while (root != null && root.Physics == null)
                root = root.Parent;
            if (root?.Physics == null || root.Physics.IsStatic)
            {
                if (!RailConstants.Debug.DrawBogiePhysics)
                    return;

                var drawPivot = Entity.GetPosition() + 4 * Entity.PositionComp.WorldMatrix.Up;
                var colorTarget = new Vector4(1, 1, 0, 1);
                MySimpleObjectDraw.DrawLine(Entity.GetPosition(), drawPivot, DebugMtl, ref colorTarget,
                    .01f);
                return;
            }

            // Clean good controllers
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
                foreach (var k in _goodControllers)
                    if (_activeControllers.Add(k))
                        sync = true;

                if (sync)
                {
                    AddScheduledCallback(ControllerSync, 30);
                }
            }


            var physics = root.Physics;
            var pivotWorld = Entity.PositionComp.WorldMatrix.Translation;

            var best = double.MaxValue;
            var bestTangent = Vector3.Zero;
            Edge bestEdge = null;
            RailSegmentComponent bestEdgeSegment;
            RailSegmentDefinition.RailSegmentCaps? bestEdgeCaps = null;
            float bestTime = 0;
            using (var e = Graph.Edges.SortedByDistance(pivotWorld))
                while (e.MoveNext())
                {
                    if (Math.Sqrt(e.Current.DistanceSquared) - TotalBias > best)
                        break;
                    var edge = (Edge) e.Current.UserData;
                    if (edge.Curve == null)
                        continue;
                    var edgeSegment = edge.Owner.Entity.Components.Get<RailSegmentComponent>();
                    var edgeCaps = edgeSegment?.Definition.CapabilitiesFor(edge.Owner.Entity.GetBuildRatio());
                    if (edgeSegment == null || !edgeCaps.HasValue)
                        continue; // no capabilities at this stage
                    float t0 = 0, t1 = 1;
                    CurveExtensions.NearestPoint(edge.Curve, pivotWorld, 16, ref t0, ref t1);
                    var p0 = edge.Curve.Sample(t0);
                    var p1 = edge.Curve.Sample(t1);
                    var dir = p1 - p0;
                    var factor = (float) MathHelper.Clamp(dir.Dot(pivotWorld - p0) / dir.LengthSquared(), 0, 1);
                    var t = t0 + (t1 - t0) * factor;
                    var pos = edge.Curve.Sample(t);
                    var dist = Vector3D.Distance(pos, pivotWorld);
                    if (dist - TotalBias > best)
                        continue;
                    var tangent = (Vector3) edge.Curve.SampleDerivative(t);
                    tangent.Normalize();

                    const float switchingEpsilon = 0.25f;
                    var switched = false;
                    if (t < switchingEpsilon)
                        switched = edge.From.IsSwitchedTo(edge.To);
                    else if (t >= 1 - switchingEpsilon)
                        switched = edge.To.IsSwitchedTo(edge.From);
                    if (switched)
                        dist -= SwitchingDistanceBias + AlignmentTangentBias;
                    else
                        dist -= AlignmentTangentBias * Math.Abs(Entity.PositionComp.WorldMatrix.Forward.Dot(tangent));

                    if (RailConstants.Debug.DrawBogieEdges)
                        edge.Draw(0, 1, switched ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0, 1, 1), 2);

                    // ReSharper disable once InvertIf
                    if (dist < best)
                    {
                        best = dist;
                        bestTangent = tangent;
                        bestEdge = edge;
                        bestEdgeSegment = edgeSegment;
                        bestEdgeCaps = edgeCaps;
                        bestTime = t;
                    }
                }

            var selfVelocity = physics.GetVelocityAtPoint(pivotWorld);
            SetAnimVar(SpeedZVar, selfVelocity.Dot((Vector3) Entity.PositionComp.WorldMatrix.Forward));
            if (bestEdge == null)
            {
                if (!RailConstants.Debug.DrawBogiePhysics)
                    return;

                var drawPivot = pivotWorld + 4 * Entity.PositionComp.WorldMatrix.Up;
                var colorTarget = new Vector4(1, 0, 0, 1);
                MySimpleObjectDraw.DrawLine(pivotWorld, drawPivot, DebugMtl, ref colorTarget,
                    .01f);
                return;
            }

            if (best > Definition.DetachDistance)
            {
                if (!RailConstants.Debug.DrawBogiePhysics)
                    return;

                var drawPivot = pivotWorld + 4 * Entity.PositionComp.WorldMatrix.Up;
                var colorTarget = new Vector4(1, 0, 1, 1);
                MySimpleObjectDraw.DrawLine(pivotWorld, drawPivot, DebugMtl, ref colorTarget,
                    .01f);
                return;
            }

            var up = (Vector3) Vector3D.Lerp(bestEdge.From.Up, bestEdge.To.Up, bestTime);
            // Not aligned vertically, abort
            if (Entity.PositionComp.WorldMatrix.Up.Dot(up) < 0.5)
            {
                return;
            }

            if (Entity.PositionComp.WorldMatrix.Forward.Dot(bestTangent) < 0)
                bestTangent = -bestTangent;
            var curvePosition = bestEdge.Curve.Sample(bestTime) + up * Definition.VerticalOffset;
            var normal = Vector3.Cross(bestTangent, up);
            normal.Normalize();

            up = Vector3.Cross(normal, bestTangent);
            up.Normalize();

            var impulse = Vector3.Zero;
            var allowDeactivation = true;
            var effectiveMass = root.Physics.Mass;
            var inertiaTensor = root.Physics.InertiaTensor;

            var qCurrent = Quaternion.CreateFromRotationMatrix(Entity.PositionComp.WorldMatrix);
            var qDesired = Quaternion.CreateFromRotationMatrix(Matrix.CreateWorld(Vector3.Zero, bestTangent, up));
            var qConj = Quaternion.Multiply(Quaternion.Conjugate(qCurrent), qDesired);
            var localAngularDesired = 2 * qConj.W * new Vector3(qConj.X, qConj.Y, qConj.Z);
            if (localAngularDesired.LengthSquared() > .01f)
                allowDeactivation = false;
            var desiredAngular = Vector3.Transform(localAngularDesired, qCurrent) * 2;
            var rotApply = desiredAngular;
            var desiredAngularRet = desiredAngular - 0.25f * physics.AngularVelocity;
            var dAR_up = Vector3.Dot( desiredAngularRet , up) * up; // projection onto up direction
            var angularImpulse = Vector3.TransformNormal( dAR_up * Definition.OrientationConvergenceFactorHorizontal + Vector3.Subtract(desiredAngularRet,dAR_up) *
                                Definition.OrientationConvergenceFactor, inertiaTensor) / MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;
            var com = physics.GetCenterOfMassWorld();

            // a) spring joint along normal to get dot(normal, (pivot*matrix - position)) == 0
            var err = (Vector3) (curvePosition - pivotWorld);

            // preemptive up force to counteract gravity.
            var gravityHere = MyGravityProviderSystem.CalculateTotalGravityInPoint(pivotWorld);
            impulse += Vector3.Dot(gravityHere, up) * up * physics.Mass * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;

            impulse += SolveImpulse(err, normal, physics.LinearVelocity, effectiveMass);

            // b) half spring joint along up to get dot(up, (pivot*matrix - position)) >= 0
            impulse += SolveImpulse(err, up, physics.LinearVelocity, effectiveMass, 1); // only up force

            if (err.LengthSquared() > .01f)
                allowDeactivation = false;

            var braking = false;
            // Hack until I fix EquinoxCore
            if (Definition.MaxVelocity > 0 && (_powerObserver.IsPowered || Definition.NeedsPower == PowerObserver.RequiredPowerEnum.None))
            {
                var cvel = physics.LinearVelocity.Dot(bestTangent);
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
                    var control = Vector3.TransformNormal(component.MoveIndicator, player.WorldMatrix).Dot(bestTangent);
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

                if (controllers > 0)
                {
                    braking |= Math.Sign(cvel) != Math.Sign(totalControl) && Math.Abs(cvel) > 0.01 &&
                               Math.Abs(totalControl) > 0.01;
                    SetAnimVar(BrakingVar, braking ? 1 : 0);
                    _powerFactor = MathHelper.Clamp(_powerFactor * (1 - PowerSmooth) + totalControl * PowerSmooth, -1, 1);
                    if (Math.Abs(totalControl) < .01f)
                        _powerFactor = 0;

                    var velocityLimit = Definition.MaxVelocity * velocityMod;
                    velocityLimit = Math.Min(velocityLimit, bestEdgeCaps.Value.MaxSpeed);

                    var forceFactorBase = MathHelper.Clamp(1 - Math.Abs(cvel) / velocityLimit, 0, 1);
                    var forceFactorControl = Math.Abs(_powerFactor);
                    if (sprinters > 0)
                        forceFactorControl *= 5 * ((float) sprinters) / controllers;
                    forceFactorControl *= .9f + controllers * .1f;

                    var dir = Math.Sign(_powerFactor);
                    if (dir != Math.Sign(cvel))
                        forceFactorBase = 1;

                    var force = Definition.MaxForce * forceFactorControl * forceFactorBase * dir * bestTangent *
                                MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;
                    physics.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE, force, com,
                        Vector3.Zero);
                }
            }


            Vector3 frictiveImpulse;
            {
                var frictiveNormalForce = Math.Max(0, impulse.Dot(up) / MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS);

                var frictiveCoefficient = Math.Max(Definition.CoefficientOfFriction, braking ? Definition.BrakingCoefficientOfFriction : 0);
                if (physics.LinearVelocity.LengthSquared() > .01f)
                    frictiveCoefficient *= 0.75f;
                var frictiveForce = frictiveCoefficient * (bestEdgeCaps?.Friction ?? 1) * frictiveNormalForce;

                // clamp frictive impulse to at-max stopping.
                var tangentMomentumAfterUpdate =
                    (physics.Mass * (physics.LinearVelocity + gravityHere * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS) + impulse).Dot(bestTangent);

                var frictiveFloatImpulse = frictiveForce * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;

                if (frictiveFloatImpulse > Math.Abs(tangentMomentumAfterUpdate))
                    // Stationary, allow deactivation
                    frictiveImpulse = -tangentMomentumAfterUpdate * bestTangent;
                else
                {
                    frictiveImpulse = -Math.Sign(tangentMomentumAfterUpdate) * frictiveFloatImpulse * bestTangent;
                    allowDeactivation = false;
                }

                impulse += frictiveImpulse;
            }

            if (!MyAPIGateway.Utilities.IsDedicated && RailConstants.Debug.DrawBogiePhysics)
            {
                var drawPivot = pivotWorld + 4 * up;
                var colorTarget = Vector4.One;
                MySimpleObjectDraw.DrawLine(pivotWorld, curvePosition, DebugMtl, ref colorTarget,
                    .01f);

                var colorMarker = new Vector4(0, 1, 0, 1);
                MySimpleObjectDraw.DrawLine(drawPivot, drawPivot + frictiveImpulse * 10, DebugMtl, ref colorMarker,
                    .01f);

                var colorImpulse = new Vector4(1, 0, 0, 1);
                MySimpleObjectDraw.DrawLine(drawPivot, drawPivot + impulse, DebugMtl, ref colorImpulse,
                    .01f);

                var colorAngImpulse = new Vector4(0, 0, 1, 1);
                MySimpleObjectDraw.DrawLine(drawPivot, drawPivot + rotApply, DebugMtl, ref colorAngImpulse,
                    .01f);
            }

            if (allowDeactivation)
            {
//                physics.Sleep();
//                if (!physics.IsActive)
//                    return;
            }

            physics.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE, impulse, com, Vector3.Zero);
            physics.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE, Vector3.Zero, pivotWorld, angularImpulse);
        }

        private const float _edgeWidth = 0.05f;
        private const float _nodeWidth = 0.01f;
        private static Vector4 _edgeColor = new Vector4(0, 0, 1, 0.1f);
        private static readonly Vector4 _edgeColorBad = new Vector4(1, 0, 0, 0.1f);
        private static readonly Vector4 _nodeColor = new Vector4(0, 0, 1, 0.1f);
        private static readonly MyStringId _squareMaterial = MyStringId.GetOrCompute("Square");
        private const float _nodeMarkerSize = 1;
        private const float _edgeMarkerVertOffset = 0.325f;


        private const float PowerSmooth = .005f;
        private float _powerFactor;

        private static Vector3 SolveAngularVel(Vector3 from, Vector3 to, bool reversible)
        {
            if (from.Dot(to) < 0 && reversible)
                from = -from;
            var axis = Vector3.Cross(from, to);
            return axis / (2 * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS);
        }

        private static Matrix CreateInertiaTensor(MyEntity e)
        {
            var scale = e.PositionComp.LocalAABB.Size;
            scale *= scale;
            var sv = e.Physics.Mass / 12;

            var m = Matrix.CreateScale(sv * (scale.Y + scale.Z), sv * (scale.X + scale.Z), sv * (scale.X + scale.Y));
            return (Matrix) e.PositionComp.WorldMatrixInvScaled.GetOrientation() * m *
                   (Matrix) e.PositionComp.WorldMatrix.GetOrientation();
        }

        private static Vector3 SolveImpulse(Vector3 desired, Vector3 actual, Matrix tensor)
        {
            var vel = desired.Normalize();
            var actualVel = actual.Dot(desired);

            var actualDv = desired * (vel - actualVel);
            return Vector3.TransformNormal(actualDv, tensor);
        }

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
            return dir * (desiredVel - velOnAxis) * mass;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_BogieComponent : MyObjectBuilder_EntityComponent
    {
    }
}
