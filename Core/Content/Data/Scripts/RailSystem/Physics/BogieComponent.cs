﻿using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Bendy;
using Equinox76561198048419394.RailSystem.Construction;
using Equinox76561198048419394.RailSystem.Definition;
using Equinox76561198048419394.RailSystem.Util;
using Equinox76561198048419394.RailSystem.Util.Curve;
using Sandbox.Engine.Physics;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using VRage.Components.Entity;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Physics
{
    [MyComponent(typeof(MyObjectBuilder_BogieComponent))]
    [MyDefinitionRequired]
    public class BogieComponent : MyEntityComponent
    {
        public BogieComponentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            Definition = (BogieComponentDefinition) definition;
        }

        private BendyLayer Graph;

        public override void OnAddedToScene()
        {
            Graph = MySession.Static.Components.Get<BendyController>()?.GetOrCreateLayer(Definition.Layer);
            AddFixedUpdate(Simulate);
        }

        public override void OnRemovedFromScene()
        {
            RemoveFixedUpdate(Simulate);
        }

        private bool _debugDraw = false;

        private static readonly MyStringHash SkinHash = MyStringHash.GetOrCompute("Skin");
        private static readonly MyStringId DebugMtl = MyStringId.GetOrCompute("Square");
        private static readonly MyStringId SpeedZVar = MyStringId.GetOrCompute("rail_speed_z");
        private static readonly MyStringId BrakingVar = MyStringId.GetOrCompute("brake");

        private readonly HashSet<MyAnimationControllerComponent> _controllers =
            new HashSet<MyAnimationControllerComponent>();

        private readonly HashSet<MyAnimationControllerComponent> _goodControllers =
            new HashSet<MyAnimationControllerComponent>();

        private readonly List<MyAnimationControllerComponent> _tmp = new List<MyAnimationControllerComponent>();

        private static readonly MyStringId AnimRailSync = MyStringId.GetOrCompute("equi_rail_sync");

        private void SetAnimVar(MyStringId key, float val)
        {
            var attacher = Entity.Components.Get<MyModelAttachmentComponent>();
            if (attacher != null)
                foreach (var e in attacher.GetAttachedEntities(MyStringHash.GetOrCompute("Skin")))
                    e.Components.Get<MyAnimationControllerComponent>()?.Variables.SetValue(key, val);
            Entity.Components.Get<MyAnimationControllerComponent>()?.Variables.SetValue(key, val);

            if (key == SpeedZVar)
            {
                _goodControllers.Clear();
                FindAttachedAnimControllers(Entity);

                _tmp.Clear();
                foreach (var k in _controllers)
                    if (!_goodControllers.Contains(k))
                        _tmp.Add(k);

                foreach (var k in _tmp)
                    _controllers.Remove(k);
                _tmp.Clear();

                bool sync = false;
                foreach (var k in _goodControllers)
                    if (_controllers.Add(k))
                        sync = true;

                foreach (var k in _controllers)
                    k.Variables.SetValue(key, val);

                if (sync)
                    AddScheduledCallback((dt) =>
                    {
                        var attt = Entity.Components.Get<MyModelAttachmentComponent>();
                        if (attt != null)
                            foreach (var e in attt.GetAttachedEntities(MyStringHash.GetOrCompute("Skin")))
                                e.Components.Get<MyAnimationControllerComponent>()?.TriggerAction(AnimRailSync);
                        Entity.Components.Get<MyAnimationControllerComponent>()?.TriggerAction(AnimRailSync);
                        foreach (var k in _goodControllers)
                            k.TriggerAction(AnimRailSync);
                    }, 30);
            }
        }

        private void FindAttachedAnimControllers(MyEntity e)
        {
            MyAnimationControllerComponent c;
            foreach (var k in e.Components.GetComponents<EquiPlayerAttachmentComponent>())
                if (k.AttachedCharacter != null && k.AttachedCharacter.Components.TryGet(out c))
                    _goodControllers.Add(c);
            if (e.Hierarchy != null)
                foreach (var child in e.Hierarchy.Children)
                    FindAttachedAnimControllers(child.Entity);
        }

        private void FindControllerEntity(Vector3D ctlPos, MyEntity e, ref MyCharacterMovementComponent controller,
            ref double bestDistanceSq)
        {
            MyCharacterMovementComponent cmp;
            foreach (var k in e.Components.GetComponents<EquiPlayerAttachmentComponent>())
                if (k.AttachedCharacter != null && k.AttachedCharacter.Components.TryGet(out cmp))
                {
                    var d = Vector3D.DistanceSquared(k.AttachedCharacter.GetPosition(), ctlPos);
                    if (d < bestDistanceSq)
                    {
                        bestDistanceSq = d;
                        controller = cmp;
                    }
                }

            if (e.Hierarchy != null)
                foreach (var child in e.Hierarchy.Children)
                    FindControllerEntity(ctlPos, child.Entity, ref controller, ref bestDistanceSq);
        }

        private void Simulate()
        {
            var pivotWorld = Entity.PositionComp.WorldMatrix.Translation;

            double best = double.MaxValue;
            Edge bestEdge = null;
            RailSegmentComponent bestEdgeSegment;
            RailSegmentDefinition.RailSegmentCaps? bestEdgeCaps = null;
            float bestTime = 0;
            using (var e = Graph.Edges.SortedByDistance(pivotWorld))
                while (e.MoveNext())
                {
                    if (e.Current.Value > best)
                        break;
                    var edge = Graph.Edges.GetUserData<Edge>(e.Current.Key);
                    if (edge.Curve == null)
                        continue;
                    var edgeSegment = edge.Owner.Entity.Components.Get<RailSegmentComponent>();
                    var edgeCaps = edgeSegment?.Definition.CapabilitiesFor(edge.Owner.Entity.GetBuildRatio());
                    if (edgeSegment != null && !edgeCaps.HasValue)
                        continue; // no capabilities at this stage
                    float t0 = 0, t1 = 1;
                    CurveExtensions.NearestPoint(edge.Curve, pivotWorld, 16, ref t0, ref t1);
                    var p0 = edge.Curve.Sample(t0);
                    var p1 = edge.Curve.Sample(t1);
                    var dir = p1 - p0;
                    var factor = (float) MathHelper.Clamp(dir.Dot(pivotWorld - p0) / dir.LengthSquared(), 0, 1);
                    var t = t0 + (t1 - t0) * factor;
                    var pos = edge.Curve.Sample(t);
                    var dist = Vector3D.DistanceSquared(pos, pivotWorld);
                    if (dist < best)
                    {
                        best = dist;
                        bestEdge = edge;
                        bestEdgeSegment = edgeSegment;
                        bestEdgeCaps = edgeCaps;
                        bestTime = t;
                    }
                }

            var root = Entity;

            while (root != null && root.Physics == null)
                root = root.Parent;

            if (root == null)
                return;

            var physics = root.Physics;
            SetAnimVar(SpeedZVar, physics.LinearVelocity.Dot((Vector3) Entity.PositionComp.WorldMatrix.Forward));
            if (bestEdge == null)
                return;
            if (!bestEdgeCaps.HasValue)
                bestEdgeCaps = RailSegmentDefinition.RailSegmentCaps.Default;

            var up = (Vector3) Vector3D.Lerp(bestEdge.From.Up, bestEdge.To.Up, bestTime);
            var tangent = (Vector3) bestEdge.Curve.SampleDerivative(bestTime);
            if (Entity.PositionComp.WorldMatrix.Forward.Dot(tangent) < 0)
                tangent = -tangent;
            tangent.Normalize();
            var position = bestEdge.Curve.Sample(bestTime) + up * Definition.VerticalOffset;
            var normal = Vector3.Cross(tangent, up);
            normal.Normalize();

            // a) spring joint along normal to get dot(normal, (pivot*matrix - position)) == 0
            var impulse = Vector3.Zero;
            var err = (Vector3) (position - pivotWorld);

            if (err.LengthSquared() > Definition.DetachDistance * Definition.DetachDistance)
                return;

            var effectiveMass = physics.Mass;
            var inertiaTensor = CreateInertiaTensor(root);

            // preemptive up force to counteract gravity.
            var gravityHere = MyGravityProviderSystem.CalculateTotalGravityInPoint(pivotWorld);
            impulse += Vector3.Dot(gravityHere, up) * up * physics.Mass * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;

            impulse += SolveImpulse(err, normal, physics.LinearVelocity, effectiveMass);

            // b) half spring joint along up to get dot(up, (pivot*matrix - position)) >= 0
            impulse += SolveImpulse(err, up, physics.LinearVelocity, effectiveMass, 1); // only up force

            var qCurrent = Quaternion.CreateFromRotationMatrix(Entity.PositionComp.WorldMatrix);
            var qDesired = Quaternion.CreateFromRotationMatrix(Matrix.CreateWorld(Vector3.Zero, tangent, up));


            var qConj = Quaternion.Multiply(Quaternion.Conjugate(qCurrent), qDesired);
            var localAngularDesired = 2 * qConj.W * new Vector3(qConj.X, qConj.Y, qConj.Z);
            var desiredAngular = Vector3.Transform(localAngularDesired, qCurrent);
            var rotApply = desiredAngular;
            desiredAngular -= 0.25f * physics.AngularVelocity;

            var angularImpulse = Vector3.TransformNormal(desiredAngular, inertiaTensor) /
                                 MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;

            var com = physics.GetCenterOfMassWorld();

            var braking = false;
            if (Definition.MaxVelocity > 0)
            {
                // get nearest character
                MyCharacterMovementComponent component = null;
                var controllerDistance = double.MaxValue;
                FindControllerEntity(root.GetPosition(), root, ref component, ref controllerDistance);
                if (component != null)
                {
                    var control = component.MoveIndicator.Dot(Vector3.Forward);
                    var cvel = physics.LinearVelocity.Dot(tangent);
                    braking = Math.Sign(cvel) != Math.Sign(control) && Math.Abs(cvel) > 0.01 &&
                              Math.Abs(control) > 0.01;
                    SetAnimVar(BrakingVar, braking ? 1 : 0);
                    _powerFactor = MathHelper.Clamp(_powerFactor * (1 - _powerSmooth) + control * _powerSmooth, -1, 1);
                    if (Math.Abs(control) < .01f)
                        _powerFactor = 0;

                    var velocityLimit = Definition.MaxVelocity;
                    if (bestEdgeCaps.HasValue)
                        velocityLimit = Math.Min(velocityLimit, bestEdgeCaps.Value.MaxSpeed);

                    var forceFactorBase = MathHelper.Clamp(1 - Math.Abs(cvel) / velocityLimit, 0, 1);
                    var forceFactorControl = Math.Abs(_powerFactor);
                    var dir = Math.Sign(_powerFactor);
                    if (dir != Math.Sign(cvel))
                        forceFactorBase = 1;

                    var force = Definition.MaxForce * forceFactorControl * forceFactorBase * dir * tangent *
                                MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;
                    if (component.Entity == MyAPIGateway.Session.ControlledObject &&
                        !MyAPIGateway.Utilities.IsDedicated && _debugDraw)
                    {
                        var colorAngImpulse = new Vector4(0, 1, 0, 1);
                        MySimpleObjectDraw.DrawLine(com, com + force, DebugMtl, ref colorAngImpulse, .01f);
                    }

                    physics.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE, force, com,
                        Vector3.Zero);
                }
            }


            var frictiveNormalForce = Math.Max(0, impulse.Dot(up) / MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS);
            var frictiveForce =
                Math.Max(Definition.CoefficientOfFriction, braking ? Definition.BrakingCoefficientOfFriction : 0) *
                (bestEdgeCaps?.Friction ?? 1) * frictiveNormalForce;
            // clamp frictive impulse to at-max stopping.
            var tangentMomentumAfterUpdate =
                (physics.Mass * (physics.LinearVelocity + gravityHere * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS) + impulse).Dot(tangent);
            var frictiveImpulse = -Math.Sign(tangentMomentumAfterUpdate) *
                                  Math.Min(frictiveForce * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS,
                                      Math.Abs(tangentMomentumAfterUpdate)) * tangent;
            impulse += frictiveImpulse;

            if (!MyAPIGateway.Utilities.IsDedicated && _debugDraw)
            {
                var drawPivot = pivotWorld + 4 * up;
                var colorTarget = Vector4.One;
                MySimpleObjectDraw.DrawLine(pivotWorld, position, DebugMtl, ref colorTarget,
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


            physics.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE, impulse, com,
                angularImpulse);
        }

        private const float _powerSmooth = .0005f;
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
            float sv = e.Physics.Mass / 12;
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