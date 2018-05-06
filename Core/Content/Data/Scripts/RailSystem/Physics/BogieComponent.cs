using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Equinox76561198048419394.RailSystem.Bendy;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Input.Devices.Keyboard;
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

        private void Simulate()
        {
            var pivotWorld = Entity.PositionComp.WorldAABB.Center;

            double best = double.MaxValue;
            Edge bestEdge = null;
            float bestTime = 0;
            using (var e = Graph.Edges.SortedByDistance(pivotWorld))
                while (e.MoveNext())
                {
                    if (e.Current.Value > best)
                        break;
                    var edge = Graph.Edges.GetUserData<Edge>(e.Current.Key);
                    if (edge.Curve == null)
                        continue;
                    float t0 = 0, t1 = 1;
                    Bezier.BSplineNearest(edge.Curve, pivotWorld, 16, ref t0, ref t1);
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
                        bestTime = t;
                    }
                }

            if (bestEdge == null)
                return;

            MyEntity root = Entity;

            while (root != null && root.Physics == null)
                root = root.Parent;

            if (root == null)
                return;

            var physics = root.Physics;
            var up = (Vector3) Vector3D.Lerp(bestEdge.From.Up, bestEdge.To.Up, bestTime);
            var tangent = (Vector3) bestEdge.Curve.SampleDerivative(bestTime);
            if (Entity.PositionComp.WorldMatrix.Up.Dot(tangent) < 0)
                tangent = -tangent;
            tangent.Normalize();
            var position = bestEdge.Curve.Sample(bestTime) + up * Definition.VerticalOffset;
            var normal = Vector3.Cross(tangent, up);
            normal.Normalize();

            // a) spring joint along normal to get dot(normal, (pivot*matrix - position)) == 0
            var impulse = Vector3.Zero;
            var err = (Vector3) (position - pivotWorld);

            if (err.LengthSquared() > RailConstants.DetachDistance * RailConstants.DetachDistance)
                return;

            var massTensor = Matrix.CreateScale(physics.Mass);
            var inertiaTensor = CreateInertiaTensor(root);
            impulse += Vector3.Dot(MyGravityProviderSystem.CalculateTotalGravityInPoint(pivotWorld), up) * up * physics.Mass * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;

            impulse += SolveImpulse(err, normal, physics.LinearVelocity, massTensor);

            // b) half spring joint along up to get dot(up, (pivot*matrix - position)) >= 0
            impulse += SolveImpulse(err, up, physics.LinearVelocity, massTensor, 1); // only up force


            var qCurrent = Quaternion.CreateFromRotationMatrix(Entity.PositionComp.WorldMatrix);
            var qDesired = Quaternion.CreateFromRotationMatrix(Matrix.CreateWorld(Vector3.Zero, up, tangent));


            var qConj = Quaternion.Multiply(Quaternion.Conjugate(qCurrent), qDesired);
            var localAngularDesired = 2 * qConj.W * new Vector3(qConj.X, qConj.Y, qConj.Z);
            var desiredAngular = Vector3.Transform(localAngularDesired, qCurrent);
            var rotApply = desiredAngular;
            desiredAngular -= 0.25f * physics.AngularVelocity;

            var angularImpulse = Vector3.TransformNormal(desiredAngular, inertiaTensor) / MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;

            var com = physics.GetCenterOfMassWorld();

            if (_debugDraw)
            {
                var colorTarget = Vector4.One;
                MySimpleObjectDraw.DrawLine(pivotWorld, position, MyStringId.GetOrCompute("Square"), ref colorTarget, .01f);
            }

            if (_debugDraw)
            {
                var colorImpulse = new Vector4(1, 0, 0, 1);
                MySimpleObjectDraw.DrawLine(com, com + impulse, MyStringId.GetOrCompute("Square"), ref colorImpulse, .01f);

                var colorAngImpulse = new Vector4(0, 0, 1, 1);
                MySimpleObjectDraw.DrawLine(com, com + rotApply, MyStringId.GetOrCompute("Square"), ref colorAngImpulse, .01f);
            }

            physics.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE, impulse, com, angularImpulse);

            if (Definition.MaxVelocity > 0)
            {
                // get nearest character
                var sphere = root.PositionComp.WorldVolume;
                MyCharacterMovementComponent component = null;
                var controllerDistance = double.MaxValue;
                foreach (var e in MyEntities.GetTopMostEntitiesInSphere(ref sphere))
                {
                    var ctx = e.Components.Get<MyCharacterMovementComponent>();
                    if (ctx == null) continue;
                    var dd = Vector3D.DistanceSquared(sphere.Center, e.PositionComp.WorldVolume.Center);
                    if (component == null || dd < controllerDistance)
                    {
                        component = ctx;
                        controllerDistance = dd;
                    }
                }

                if (component != null)
                {
                    var control = component.MoveIndicator.Dot(Vector3.Forward);

                    {
                        bool backwards = component.MoveIndicator.Z < -0.5;
                        bool forward = component.MoveIndicator.Z > 0.5;
                        bool strafeLeft = component.MoveIndicator.X < -0.5;
                        bool strafeRight = component.MoveIndicator.X > 0.5;
                        float max = (!component.IsSprinting
                                        ? (!component.IsRunning
                                            ? (!component.IsWalking
                                                ? (!component.IsCrouching
                                                    ? 3.8f
                                                    : (!backwards || strafeLeft || strafeRight
                                                        ? 0.9f
                                                        : 1.3f))
                                                : 1.8f)
                                            : (!backwards || strafeLeft || strafeRight
                                                ? (!forward ? 3.3f : 3.1f)
                                                : 3.8f))
                                        : 7) * component.MovementSpeedMultiplier;
                        control *= max;
                    }

                    var cvel = physics.LinearVelocity.Dot(tangent);
                    _powerFactor = MathHelper.Clamp(_powerFactor * (1 - _powerSmooth) + control * _powerSmooth, -1, 1);
                    if (Math.Abs(control) < .01f)
                        _powerFactor = 0;

                    var forceFactorBase = MathHelper.Clamp(1 - Math.Abs(cvel) / Definition.MaxVelocity, 0, 1);
                    var forceFactorControl = Math.Abs(_powerFactor);
                    var dir = Math.Sign(_powerFactor);
                    if (dir != Math.Sign(cvel))
                        forceFactorBase = 1;

                    var force = Definition.MaxForce * forceFactorControl * forceFactorBase * dir * tangent * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;
                    var colorAngImpulse = new Vector4(0, 1, 0, 1);
                    if (component.Entity == MyAPIGateway.Session.ControlledObject && !MyAPIGateway.Utilities.IsDedicated)
                        MySimpleObjectDraw.DrawLine(com, com + force, MyStringId.GetOrCompute("Square"), ref colorAngImpulse, .01f);
                    physics.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE, force, com, Vector3.Zero);
                }
            }
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
            Matrix mass, int signFlags = 3)
        {
            var errorOnAxis = dir.Dot(error);
            var velOnAxis = dir.Dot(vel);
            // velOnAxis = -errorOnAxis / 1 step
            var desiredVel = errorOnAxis / (MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS * 2);

            if ((signFlags & 1) == 0 && errorOnAxis > 0)
                return Vector3.Zero;
            if ((signFlags & 2) == 0 && errorOnAxis < 0)
                return Vector3.Zero;
            return Vector3.TransformNormal(dir * (desiredVel - velOnAxis), mass);
        }
    }


    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_BogieComponent : MyObjectBuilder_EntityComponent
    {
    }
}