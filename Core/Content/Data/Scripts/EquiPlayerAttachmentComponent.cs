using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Medieval.Entities.UseObject;
using Sandbox.Engine.Physics;
using Sandbox.Game.Components;
using Sandbox.Game.Entities;
using Sandbox.Game.Replication;
using Sandbox.ModAPI;
using VRage;
using VRage.Components;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.Input;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Logging;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394
{
    [MyComponent(typeof(MyObjectBuilder_EquiPlayerAttachmentComponent))]
    [MyDependency(typeof(MyUseObjectsComponent), Critical = true)]
    [MyDefinitionRequired]
    [ReplicatedComponent]
    public class EquiPlayerAttachmentComponent : MyMultiComponent, IMyGenericUseObjectInterface, IMyEventProxy
    {
        public EquiPlayerAttachmentComponentDefinition Definition { get; private set; }

        private readonly MyInputContext _inSeatControls = new MyInputContext("Attachment controls");

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiPlayerAttachmentComponentDefinition) def;

            _inSeatControls.UnregisterAllActions();
            _inSeatControls.RegisterAction(Definition.ExitControl, () =>
            {
                if (_attachedCharacter != MyAPIGateway.Session.ControlledObject)
                    return;
                RaiseDetach(true, null);
            });
        }

        private readonly List<MyUseObjectGeneric> _genericUseObjects = new List<MyUseObjectGeneric>();

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            AddScheduledCallback(RegisterLazy);
        }

        private void WaitingForEntity(MyEntity e)
        {
            if (Entity == null || !Entity.InScene)
            {
                MyEntities.OnEntityAdd -= WaitingForEntity;
                return;
            }

            if (e.EntityId != _entityIdForSave) return;
            if (_attachedCharacter == null)
                AttachInternal(e.EntityId, 0f);
        }

        private void RegisterLazy(long dt)
        {
            if (Entity == null || !Entity.InScene)
                return;
            var component = Entity.Components.Get<MyUseObjectsComponentBase>();
//            // TODO another hack because of LoadDetectorsFromModel being naughty
//            component?.OnRemovedFromScene();
//            component?.OnAddedToScene();
            _genericUseObjects.Clear();
            List<MyUseObjectGeneric> tmp = null;
            if (component != null)
            {
                tmp = new List<MyUseObjectGeneric>();
                component.GetInteractiveObjects(tmp);
                foreach (var obj in tmp)
                    if (Definition.AcceptDummy(obj))
                        _genericUseObjects.Add(obj);
            }

            if (_genericUseObjects.Count == 0)
            {
                MyLog.Default.Warning(
                    $"Failed to find use object for {nameof(EquiPlayerAttachmentComponent)} {Definition.Id}");
                if (tmp == null) return;
                foreach (var t in tmp)
                    MyLog.Default.Info($"Detector " + t);
                return;
            }

            foreach (var k in _genericUseObjects)
                k.Interface = this;

            if (_entityIdForSave == 0L) return;
            MyEntity existing;
            if (MyEntities.TryGetEntityById(_entityIdForSave, out existing))
                AttachInternal(existing.EntityId, 0f);
            else
                MyEntities.OnEntityAdd += WaitingForEntity;
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            RemoveScheduledUpdate(RegisterLazy);
            foreach (var k in _genericUseObjects)
                k.Interface = null;
            _genericUseObjects.Clear();

            MyEntities.OnEntityAdd -= WaitingForEntity;
            if (_attachedCharacter != null)
                DetachInternal(false, null); // if this gets removed then kick the entity, but don't clear the save.
        }

        private MatrixD _relPosAdded;
        private int _animationIndex = -1;
        private MyEntity _attachedCharacter;

        // TODO kill asap
        public MyEntity AttachedCharacter => _attachedCharacter;

        public void Use(string dummyName, UseActionEnum actionEnum, MyEntity user)
        {
            if (actionEnum != UseActionEnum.Manipulate)
                return;
            if (user != MyAPIGateway.Session.ControlledObject)
                return;
            RaiseAttach(user.EntityId);
        }

        #region Add/Remove Logic

        private void RaiseAttach(long id)
        {
            var rand = (float) _random.NextDouble();
            if (MyAPIGateway.Multiplayer == null)
                AttachInternal(id, rand);
            else
                MyAPIGateway.Multiplayer.RaiseEvent(this, s => Attach, id, rand);
        }

        [Event]
        [Broadcast]
        [Server]
        private void Attach(long id, float randomSeed)
        {
            if (!MyEventContext.Current.IsLocallyInvoked && MyAPIGateway.Session.IsServer())
            {
                MyEntity ent;
                if (!MyEntities.TryGetEntityById(id, out ent) || ent == null)
                {
                    MyEventContext.ValidationFailed();
                    return;
                }

                var player = MyAPIGateway.Players.GetPlayerControllingEntity(ent);
                if (player == null || MyEventContext.Current.Sender.Value != player.SteamUserId)
                {
                    MyLog.Default.Warning(
                        $"Invalid player requested attach.  {player?.SteamUserId.ToString() ?? "nil"} required, {MyEventContext.Current.Sender.Value} requested");
                    MyEventContext.ValidationFailed();
                    return;
                }
            }

            if (!AttachInternal(id, randomSeed))
                MyEventContext.ValidationFailed();
        }

        private const float MaxInteractionDistance = 50;

        private bool AttachInternal(long id, float randomSeed)
        {
            MyEntity user;
            if (!MyEntities.TryGetEntityById(id, out user) || user == null ||
                (MyAPIGateway.Session.IsServer() &&
                 Vector3D.DistanceSquared(user.WorldMatrix.Translation, Entity.WorldMatrix.Translation) >=
                 MaxInteractionDistance * MaxInteractionDistance))
            {
                MyLog.Default.Warning($"Player requesting attach was too far away or does not exist.");
                return false;
            }

            MyEntities.OnEntityAdd -= WaitingForEntity;
            var wasAutoLoaded = _entityIdForSave == user.EntityId;

            if (user == _attachedCharacter)
                return true;

            if (_attachedCharacter != null && !DetachInternal(true, null))
                return false;
            _attachedCharacter = user;
            _entityIdForSave = _attachedCharacter.EntityId;

            _relPosAdded = _attachedCharacter.WorldMatrix * MatrixD.Invert(AttachMatrix);
            _relPosAdded = MatrixD.Normalize(_relPosAdded);

            if (_attachedCharacter.Physics != null)
            {
                _attachedCharacter.Physics.Enabled = false;
                _attachedCharacter.Physics.AngularVelocity = Vector3.Zero;
                _attachedCharacter.Physics.LinearVelocity = Vector3.Zero;
            }

            PilotUpdate();
            AddFixedUpdate(PilotUpdate);

            if (_attachedCharacter == MyAPIGateway.Session.ControlledObject)
                _inSeatControls.Push();

            var animController = _attachedCharacter.Components.Get<MyAnimationControllerComponent>();
            if (animController != null)
            {
                EquiPlayerAttachmentComponentDefinition.AnimationDesc? anim = null;
                if (wasAutoLoaded)
                    anim = Definition.ByIndex(_animationIndex);
                if (!anim.HasValue)
                    anim = Definition.SelectAnimation(animController.SourceId, randomSeed, out _animationIndex);
                if (anim.HasValue)
                    animController.TriggerAction(anim.Value.Start);
            }
            else
                _animationIndex = -1;

            return true;
        }

        private static readonly Random _random = new Random();

        private MatrixD AttachMatrix => Definition.Anchor.GetMatrix() * Entity.WorldMatrix;

        private void RaiseDetach(bool clearSave, Vector3D? loc)
        {
            if (MyAPIGateway.Multiplayer == null)
                DetachInternal(clearSave, loc);
            else
                MyAPIGateway.Multiplayer.RaiseEvent(this, s => Detach, clearSave, loc);
        }

        [Event]
        [Broadcast]
        [Server]
        private void Detach(bool clearSave, Vector3D? outLoc)
        {
            if (!MyEventContext.Current.IsLocallyInvoked && MyAPIGateway.Session.IsServer())
            {
                if (_attachedCharacter == null)
                {
                    MyEventContext.ValidationFailed();
                    return;
                }

                if (outLoc.HasValue && Vector3D.DistanceSquared(outLoc.Value, _attachedCharacter.GetPosition()) >
                    MaxInteractionDistance * MaxInteractionDistance)
                {
                    MyEventContext.ValidationFailed();
                    return;
                }

                var player = MyAPIGateway.Players.GetPlayerControllingEntity(_attachedCharacter);
                if (player == null || MyEventContext.Current.Sender.Value != player.SteamUserId)
                {
                    MyLog.Default.Warning(
                        $"Invalid player requested detach.  {player?.SteamUserId.ToString() ?? "nil"} required, {MyEventContext.Current.Sender.Value} requested");
                    MyEventContext.ValidationFailed();
                    return;
                }
            }

            if (!DetachInternal(clearSave, outLoc))
                MyEventContext.ValidationFailed();
        }

        private bool DetachInternal(bool clearSave, Vector3D? loc)
        {
            if (_attachedCharacter == null)
                return false;

            if (!_relPosAdded.IsValid())
                _relPosAdded = MatrixD.Identity;

            _inSeatControls.Pop();

            var outPos = _relPosAdded * AttachMatrix;
            if (clearSave && _attachedCharacter.PositionComp != null)
            {
                var translate = MyAPIGateway.Entities.FindFreePlace(
                    outPos.Translation + _attachedCharacter.PositionComp.LocalVolume.Center,
                    _attachedCharacter.PositionComp.LocalVolume.Radius * .6f, 200, 20, 2f);
                if (translate.HasValue)
                    outPos.Translation = translate.Value - _attachedCharacter.PositionComp.LocalVolume.Center;
                else
                    outPos = AttachMatrix;

                if (loc.HasValue)
                    outPos.Translation = loc.Value;

                _attachedCharacter.PositionComp.SetWorldMatrix(outPos, _attachedCharacter.Parent, true);

                if (_attachedCharacter.Physics != null)
                {
                    _attachedCharacter.Physics.Enabled = true;
                    _attachedCharacter.Physics.LinearVelocity =
                        GetVelocityAtPoint(_attachedCharacter.WorldMatrix.Translation);
                }
            }

            var anim = Definition.ByIndex(_animationIndex);
            if (anim.HasValue)
                _attachedCharacter.Components.Get<MyAnimationControllerComponent>()?.TriggerAction(anim.Value.Stop);
            RemoveFixedUpdate(PilotUpdate);
            _attachedCharacter = null;
            if (!clearSave)
            {
                MyEntities.OnEntityAdd -= WaitingForEntity;
                MyEntities.OnEntityAdd += WaitingForEntity;
                return true;
            }

            _entityIdForSave = 0;
            _animationIndex = -1;
            return true;
        }

        private Vector3 GetVelocityAtPoint(Vector3D pos)
        {
            var root = Entity;
            while (root != null)
            {
                if (root.Physics != null)
                    return root.Physics.GetVelocityAtPoint(pos);
                root = root.Parent;
            }

            return Vector3.Zero;
        }

        #endregion

        private void PilotUpdate()
        {
            // ReSharper disable once UseNullPropagationWhenPossible
            if (_attachedCharacter == null)
                return;
            if (!_attachedCharacter.InScene)
            {
                DetachInternal(false, null);
                return;
            }

            if (_attachedCharacter.PositionComp == null) return;
            if (MyAPIGateway.Session.IsServer() && Vector3D.DistanceSquared(
                    _attachedCharacter.PositionComp.WorldMatrix.Translation,
                    AttachMatrix.Translation) > MaxInteractionDistance * MaxInteractionDistance)
                RaiseDetach(true, _attachedCharacter.PositionComp.WorldMatrix.Translation);
            else
                _attachedCharacter.PositionComp.WorldMatrix = AttachMatrix;
        }

        public MyActionDescription GetActionInfo(string dummyName, UseActionEnum actionEnum)
        {
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (actionEnum)
            {
                case UseActionEnum.Manipulate:
                    return _attachedCharacter != null ? Definition.OccupiedActionDesc : Definition.EmptyActionDesc;
                default:
                    return new MyActionDescription {Text = MyStringId.GetOrCompute("Bad action")};
            }
        }

        public UseActionEnum SupportedActions => PrimaryAction | SecondaryAction;

        public UseActionEnum PrimaryAction =>
            _attachedCharacter == null ? UseActionEnum.Manipulate : UseActionEnum.None;

        public UseActionEnum SecondaryAction => UseActionEnum.None;
        public bool ContinuousUsage => false;

        #region Saving

        private long _entityIdForSave;

        public override bool IsSerialized => _entityIdForSave != 0;

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiPlayerAttachmentComponent) base.Serialize();
            ob.Entity = _entityIdForSave;
            ob.Relative = new MyPositionAndOrientation(_relPosAdded);
            ob.AnimationId = _animationIndex;
            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent obb)
        {
            base.Deserialize(obb);
            var ob = (MyObjectBuilder_EquiPlayerAttachmentComponent) obb;
            _entityIdForSave = ob.Entity;
            _relPosAdded = ob.Relative.GetMatrix();
            _animationIndex = ob.AnimationId;
        }

        #endregion
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiPlayerAttachmentComponent : MyObjectBuilder_MultiComponent
    {
        public long Entity;
        public MyPositionAndOrientation Relative;
        public int AnimationId;

        public bool ShouldSerializeRelative()
        {
            return Entity != 0;
        }
    }
}