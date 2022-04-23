using System;
using System.Collections.Generic;
using Equinox76561198048419394.RailSystem.Util;
using Sandbox.Engine.Physics;
using Sandbox.Game.EntityComponents.Grid;
using VRage.Components.Block;
using VRage.Components.Physics;
using VRage.Components.Session;
using VRage.Entities.Gravity;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.Scene;
using VRage.Session;

namespace Equinox76561198048419394.RailSystem.Physics
{
    public static class SleepUtility
    {
        private static bool CanSleepPhysics(MyPhysicsComponentBase check, MyPhysicsComponentBase relativeTo)
        {
            const double toleranceLinear = .025f;
            const double toleranceAngular = .1f;
            var checkLinVel = check.LinearVelocity.LengthSquared();
            var checkAngVel = check.AngularVelocity.LengthSquared();
            if (checkLinVel < toleranceLinear && checkAngVel < toleranceAngular)
                return true;
            if (relativeTo == null)
                return false;
            checkLinVel = Math.Abs(checkLinVel - relativeTo.LinearVelocity.LengthSquared());
            checkAngVel = Math.Abs(checkAngVel - relativeTo.AngularVelocity.LengthSquared());
            return checkLinVel < toleranceLinear && checkAngVel < toleranceAngular;
        }

        public static bool AttemptPhysicsSleep(MyEntity start)
        {
            var relativeTo = start.Get<MyPhysicsComponentBase>();
            using (PoolManager.Get(out HashSet<MyGroup> visitedGroups))
            using (PoolManager.Get(out HashSet<MyEntity> visitedEntities))
            using (PoolManager.Get(out List<MyEntity> pruningEntities))
            using (PoolManager.Get(out Queue<MyEntity> queue))
            {
                visitedEntities.Clear();
                visitedGroups.Clear();
                queue.Clear();

                queue.Enqueue(start);
                visitedEntities.Add(start);
                while (queue.Count > 0)
                {
                    var e = queue.Dequeue();
                    if (e.Components.TryGet(out MyBlockComponent block))
                    {
                        if (visitedEntities.Add(block.GridData.Entity))
                            queue.Enqueue(block.GridData.Entity);
                        continue;
                    }

                    if (!e.Components.TryGet<MyPhysicsComponentBase>(out var physicsComponent) || physicsComponent.IsStatic || !physicsComponent.Enabled)
                        continue;
                    if (e.Components.TryGet<MyCharacterPhysics>(out var characterPhysics) && characterPhysics.Enabled)
                        return false;
                    if (relativeTo != physicsComponent && !CanSleepPhysics(physicsComponent, relativeTo))
                        return false;

                    foreach (var group in MySession.Static.Scene.GetEntityGroups(e.Id))
                        if (visitedGroups.Add(@group))
                            foreach (var entity in @group.Entities)
                                if (visitedEntities.Add(entity))
                                    queue.Enqueue(entity);

                    var queryVol = e.PositionComp.WorldVolume;
                    queryVol.Radius += 10;
                    pruningEntities.Clear();
                    MyGamePruningStructure.GetTopMostEntitiesInSphere(in queryVol, pruningEntities, MyEntityQueryType.Dynamic);
                    foreach (var nearby in pruningEntities)
                        if (visitedEntities.Add(nearby) && nearby.PositionComp.WorldVolume.Intersects(queryVol))
                            queue.Enqueue(nearby);
                }

                foreach (var ent in visitedEntities)
                {
                    var gridPhysics = ent.Get<MyGridRigidBodyComponent>();
                    if (gridPhysics == null) continue;
                    gridPhysics.Gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(ent.PositionComp.GetPosition());
                    gridPhysics.Sleep();
                }

                return true;
            }
        }
    }
}