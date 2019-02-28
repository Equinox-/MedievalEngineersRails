using System;
using System.Xml.Serialization;
using VRage.Components;
using VRage.Components.Entity.Animations;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util
{
    [MyComponent(typeof(MyObjectBuilder_FastRiggedCopyHack))]
    [MyDependency(typeof(MySkeletonComponent), Critical = true)]
    public class FastRiggedCopyHack : MyEntityComponent
    {
        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _mySkeleton = Entity.Get<MySkeletonComponent>();
            _mySkeleton.OnReloadBones += InvalidateBoneMapping;
            Entity.Hierarchy.ParentChanged += ParentChanged;
            ParentChanged(Entity.Hierarchy, null, Entity.Hierarchy.Parent);
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (_mySkeleton != null)
                _mySkeleton.OnReloadBones -= InvalidateBoneMapping;
            Entity.Hierarchy.ParentChanged -= ParentChanged;
            ParentChanged(Entity.Hierarchy, Entity.Hierarchy.Parent, null);
            _mySkeleton = null;
        }

        private MySkeletonComponent _mySkeleton;
        private MyEntity _parentCache;
        private MySkeletonComponent _parentSkeleton;

        private void ParentChanged(MyHierarchyComponent target, MyHierarchyComponent oldParent, MyHierarchyComponent newParent)
        {
            var obj = newParent?.Entity;
            if (obj == _parentCache)
                return;
            if (_parentCache != null)
            {
                _parentCache.Components.ComponentAdded -= CheckSkeleton;
                _parentCache.Components.ComponentRemoved -= CheckSkeleton;
            }

            _parentCache = obj;
            if (_parentCache != null)
            {
                _parentCache.Components.ComponentAdded += CheckSkeleton;
                _parentCache.Components.ComponentRemoved += CheckSkeleton;
            }

            CheckSkeleton();
        }

        private void CheckSkeleton(MyEntityComponent e)
        {
            CheckSkeleton();
        }

        private void CheckSkeleton()
        {
            var newSkeleton = _parentCache?.Get<MySkeletonComponent>();
            if (_parentSkeleton == newSkeleton)
                return;
            if (_parentSkeleton != null)
            {
                _parentSkeleton.OnReloadBones -= InvalidateBoneMapping;
                _parentSkeleton.OnPoseChanged -= UpdatePose;
                _parentSkeleton = null;
            }

            _parentSkeleton = newSkeleton;
            if (_parentSkeleton == null)
                return;
            _parentSkeleton.OnReloadBones += InvalidateBoneMapping;
            _parentSkeleton.OnPoseChanged += UpdatePose;
            InvalidateBoneMapping(_parentSkeleton);
        }

        private int[] _boneMapping;

        private void InvalidateBoneMapping(MySkeletonComponent skeleton)
        {
            if (_parentSkeleton?.CharacterBones == null)
                return;
            Array.Resize(ref _boneMapping, _mySkeleton.CharacterBones.Length);
            for (var i = 0; i < _mySkeleton.CharacterBones.Length; i++)
            {
                var bone = _mySkeleton.CharacterBones[i];
                var match = -1;
                for (var j = 0; j < _parentSkeleton.CharacterBones.Length; j++)
                {
                    var parentBone = _parentSkeleton.CharacterBones[j];
                    if (!bone.Name.Equals(parentBone.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    match = j;
                    break;
                }

                _boneMapping[i] = match;
            }

            UpdatePose(skeleton);
        }

        [FixedUpdate]
        private void Update()
        {
            UpdatePose(null);
        }

        private void UpdatePose(MySkeletonComponent skeleton)
        {
            if (_parentSkeleton == null || _boneMapping == null)
                return;
            for (var i = 0; i < _boneMapping.Length; i++)
            {
                var matched = _boneMapping[i];
                var dest = _mySkeleton.CharacterBones[i];
                if (matched != -1)
                {
                    var source = _parentSkeleton.CharacterBones[matched];
                    var srcT = source.Transform.AbsoluteTransform;
                    var dstParent = dest.Parent?.Transform.AbsoluteTransform ?? MyTransform.Identity;
                    dstParent.Rotation.Conjugate();
                    var tmpPos = srcT.Position - dstParent.Position;
                    var finalPos = Vector3.Transform(tmpPos, dstParent.Rotation);
                    var finalRot = dest.InheritRotation ? Quaternion.Multiply(dstParent.Rotation, srcT.Rotation) : srcT.Rotation;
                    dest.SetTransform(ref finalPos, ref finalRot);
                }

                dest.ComputeAbsoluteTransform(null, false);
            }

            _mySkeleton.MarkPoseChanged();
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_FastRiggedCopyHack : MyObjectBuilder_EntityComponent
    {
    }
}