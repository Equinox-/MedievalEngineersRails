#if false
using System;
using System.Collections.Generic;
using System.Diagnostics.PerformanceData;
using Sandbox.Engine.Utils;
using Sandbox.Game.Components;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.Entities.Entity.Stats;
using Sandbox.Game.Entities.Entity.Stats.Extensions;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.EntityComponents.Renders;
using Sandbox.Game.World;
using Sandbox.Graphics;
using VRage.Components.Entity.Animations;
using VRage.Components.Entity.Camera;
using VRage.Components.Interfaces;
using VRage.Definitions.Components.Character;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.Models;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Components.Character;
using VRage.ObjectBuilders.Components.Entity;
using VRage.Systems;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Animations;
using VRageRender.Messages;

namespace Equinox76561198048419394.RailSystem.Util
{
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_RenderComponentRigged : MyObjectBuilder_RenderComponent
    {
    }

    [MyDependency(typeof(MySkeletonComponent))]
    [MyComponent(typeof(MyObjectBuilder_RenderComponentRigged))]
    public class MyRenderComponentRigged : MyRenderComponent
    {
        public override void OnAddedToScene()
        {
            m_skeletonSent = false;
            m_skeletonComp = Container.Get<MySkeletonComponent>();
            if (m_skeletonComp != null)
                m_skeletonComp.OnReloadBones += OnSkeletonReloadBones;

            EnableColorMaskHsv = true;
            NeedsDraw = true;
            CastShadows = true;
            NeedsResolveCastShadow = false;
            SkipIfTooSmall = false;
            base.OnAddedToScene();
            Entity.AddDebugRenderComponent(new MyDebugRenderComponentRigged(Entity));
        }

        public override void OnBeforeRemovedFromContainer()
        {
            if (m_skeletonComp != null)
                m_skeletonComp.OnReloadBones -= OnSkeletonReloadBones;
            m_skeletonComp = null;
            base.OnBeforeRemovedFromContainer();
        }

        private void OnSkeletonReloadBones(MySkeletonComponent mySkeletonComponent)
        {
            m_skeletonSent = false;
        }

        public override void AddRenderObjects()
        {
            if (Entity?.ModelComp == null)
                return;

            if (m_model == null)
                GetModelComponent();

            MyModel model = Entity.ModelComp.Model;
            if (model == null)
                return;

            if (IsRenderObjectAssigned(0))
                return;

            uint id = MyRenderProxy.CreateRenderCharacter(Container.Entity.GetFriendlyName() + " EntityId: " + Container.Entity.EntityId, model.AssetName,
                Container.Entity.PositionComp.WorldMatrix, m_diffuseColor, m_model.ColorMask, GetRenderFlags());
            SetRenderObjectID(0, id);
            SetVisibilityFeedback(true);
        }

        protected override void UpdateRenderObjectVisibility(bool visible)
        {
            base.UpdateRenderObjectVisibility(visible);
            if (!visible || m_skeletonSent) return;
            MarkPoseDirty();
            Draw();
        }

        private void UpdateRenderBoundingBox()
        {
            MySkeletonComponent skeletonComp = m_skeletonComp;
            if (skeletonComp?.CharacterBones == null)
                return;

            var boundingBox = BoundingBox.CreateInvalid();
            var worldMatrix = Entity.PositionComp.WorldMatrix;
            foreach (MyCharacterBone myCharacterBone in skeletonComp.CharacterBones)
            {
                Vector3 translation = myCharacterBone.Transform.AbsoluteMatrix.Translation;
                boundingBox.Include(ref translation);
            }

            ContainmentType containmentType;
            m_lastAabb.Contains(ref boundingBox, out containmentType);
            if (containmentType == ContainmentType.Contains) return;
            boundingBox.Inflate(0.5f);
            if (RenderObjectIDs[0] != uint.MaxValue)
                MyRenderProxy.UpdateRenderObject(RenderObjectIDs[0], worldMatrix, boundingBox, -1, null);
            m_lastAabb = boundingBox;
        }

        private void UpdateCharacterSkeleton()
        {
            MyCharacterBone[] characterBones = m_skeletonComp.CharacterBones;
            MySkeletonBoneDescription[] array = new MySkeletonBoneDescription[characterBones.Length];
            for (int i = 0; i < characterBones.Length; i++)
            {
                array[i].Parent = ((characterBones[i].Parent != null) ? characterBones[i].Parent.Index : -1);
                array[i].SkinTransform = characterBones[i].Transform.AbsoluteBindTransformInv;
            }

            MyRenderProxy.SetCharacterSkeleton(RenderObjectIDs[0], array, Entity.ModelComp.Model.Animations.Skeleton.ToArray());
            MarkPoseDirty();
        }

        /// <summary>
        /// Updates the pose of the character on the next call to <see cref="Draw"/>
        /// </summary>
        public void MarkPoseDirty()
        {
            m_poseDirty = true;
        }

        public override void Draw()
        {
            if (!m_skeletonSent)
            {
                UpdateCharacterSkeleton();
                m_skeletonSent = true;
            }

            UpdateRenderBoundingBox();
            if (m_poseDirty)
            {
                m_poseDirty = false;
                MyRenderProxy.SetCharacterTransforms(RenderObjectIDs[0], m_skeletonComp.BoneAbsoluteTransforms, null);
            }

            base.Draw();
        }

        private MySkeletonComponent m_skeletonComp;
        private bool m_skeletonSent, m_poseDirty;
        private BoundingBox m_lastAabb = BoundingBox.CreateInvalid();
    }

    [MyDefinitionType(typeof(MyObjectBuilder_RenderComponentRiggedDefinition))]
    public class MyRenderComponentRiggedDefinition : MyEntityComponentDefinition
    {
        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            var ob = (MyObjectBuilder_RenderComponentRiggedDefinition) builder;
            base.Init(ob);
        }
    }

    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_RenderComponentRiggedDefinition : MyObjectBuilder_EntityComponentDefinition
    {
    }

    public class MyDebugRenderComponentRigged : MyDebugRenderComponent
    {
        public MyDebugRenderComponentRigged(MyEntity entity) : base(entity)
        {
        }

        public override void DebugDraw()
        {
            m_simulatedBonesDebugDraw.Clear();
            m_simulatedBonesAbsoluteDebugDraw.Clear();
            MySkeletonComponent mySkeletonComponent = Entity.Components.Get<MySkeletonComponent>();
            if (mySkeletonComponent == null) return;
            if (!MyDebugDrawSettings.DEBUG_DRAW_CHARACTER_BONES) return;
            mySkeletonComponent.ComputeAbsoluteTransforms();
            for (int i = 0; i < mySkeletonComponent.CharacterBones.Length; i++)
            {
                MyCharacterBone myCharacterBone = mySkeletonComponent.CharacterBones[i];
                if (myCharacterBone.Parent == null) continue;
                MatrixD matrix = Matrix.CreateScale(0.1f) * myCharacterBone.Transform.AbsoluteMatrix * Entity.PositionComp.WorldMatrix;
                Vector3D translation = matrix.Translation;
                MyCharacterBone parent = myCharacterBone.Parent;
                Vector3D translation2 = (parent.Transform.AbsoluteMatrix * Entity.PositionComp.WorldMatrix).Translation;
                MyRenderProxy.DebugDrawLine3D(translation2, translation, Color.White, Color.White, false);
                Vector3D worldCoord = (translation2 + translation) * 0.5;
                MyRenderProxy.DebugDrawText3D(worldCoord, myCharacterBone.Name + " (" + i + ")", Color.Red, 0.5f, false);
                MyRenderProxy.DebugDrawAxis(matrix, 0.1f, false);
            }
        }

        private readonly List<Matrix> m_simulatedBonesDebugDraw = new List<Matrix>();
        private readonly List<Matrix> m_simulatedBonesAbsoluteDebugDraw = new List<Matrix>();
    }

    [MyDependency(typeof(MySkeletonComponent))]
    [MyComponent(typeof(MyObjectBuilder_RenderComponentCharacter))]
    public class MyRenderComponentCharacter : MyRenderComponentRigged
    {
        public override void Init(MyEntityComponentDefinition definition)
        {
            m_definition = (definition as MyRenderComponentCharacterDefinition);
            base.Init(definition);
        }

        public override void OnAddedToScene()
        {
            m_statComponent = Container.Get<MyEntityStatComponent>();
            m_damageComp = Container.Get<MyCharacterDamageComponent>();
            if (m_damageComp != null)
            {
                m_damageComp.DamageTaken += OnDamageTaken;
            }

            EnableColorMaskHsv = true;
            NeedsDraw = true;
            CastShadows = true;
            NeedsResolveCastShadow = false;
            SkipIfTooSmall = false;
            base.OnAddedToScene();
            Entity.AddDebugRenderComponent(new MyDebugRenderComponentCharacter(Entity));
            if (!MySession.Static.IsDedicated)
                MyUpdateComponent.Static.AddFixedUpdate(new MyFixedUpdate(UpdateHeadOnStart));
        }

        private void OnDamageTaken(MyEntity arg1, MyDamageInformation arg2)
        {
            if (m_definition != null && !m_definition.DamageEffects.TryGetValue(arg2.Type, out m_currentHitIndicatorSprite))
                m_currentHitIndicatorSprite = "Textures\\Gui\\ScreenEffect_Blood.png";
            Damage();
        }

        private void UpdateHeadOnStart()
        {
            if (!m_afterFirstUpdate)
            {
                m_afterFirstUpdate = true;
                return;
            }

            bool enabled = MyCameraComponent.ActiveCamera.Entity != Entity || !(MyCameraComponent.ActiveCamera is MyFirstPersonCameraComponent);
            EnableHead(enabled);
            MyUpdateComponent.Static.RemoveFixedUpdate(new MyFixedUpdate(UpdateHeadOnStart));
        }

        public override void OnBeforeRemovedFromContainer()
        {
            if (m_damageComp != null)
                m_damageComp.DamageTaken -= OnDamageTaken;
            m_damageComp = null;
            base.OnBeforeRemovedFromContainer();
        }

        public void Damage()
        {
            m_currentHitIndicatorCounter = 0.8f;
        }

        private void EnableHead(bool enabled)
        {
            if (Entity == null || !Entity.InScene || m_definition?.MaterialsDisabledIn1st == null || m_headRenderingEnabled == enabled)
                return;

            m_headRenderingEnabled = enabled;
            foreach (string materialName in m_definition.MaterialsDisabledIn1st)
                MyRenderProxy.UpdateModelProperties(RenderObjectIDs[0], 0, -1, materialName, new bool?(enabled), null, null);
        }

        public override void Draw()
        {
            bool enabled = (MyCameraComponent.ActiveCamera != null && MyCameraComponent.ActiveCamera.Entity != Entity) ||
                           !(MyCameraComponent.ActiveCamera is MyFirstPersonCameraComponent);
            EnableHead(enabled);
            MarkPoseDirty();
            base.Draw();
            HandleDamageEffect();
        }

        private void HandleDamageEffect()
        {
            bool isSelfDead = MySession.Static.ControlledEntity == Entity && m_statComponent != null && m_statComponent.IsDead();
            bool isSelfCorpse = MySector.ActiveCamera != null && MySector.ActiveCamera.Entity == Entity && Entity.Components.Contains<MyCorpseComponent>();
            bool isDead = isSelfDead || isSelfCorpse;
            if (isDead)
            {
                DrawDamageEffect(1f, "Textures\\Gui\\ScreenEffect_Blood.png");
                m_currentHitIndicatorCounter = 0f;
                return;
            }

            if (MySession.Static.ControlledEntity != Entity)
                return;
            if (m_statComponent == null)
                return;

            float currentRatio = m_statComponent.GetHealth().CurrentRatio;
            if (currentRatio <= 0.3f)
            {
                float alpha = MathHelper.Clamp(0.3f - currentRatio, 0f, 1f) / 0.3f + 0.3f;
                DrawDamageEffect(alpha, "Textures\\Gui\\ScreenEffect_Blood.png");
            }

            if (m_currentHitIndicatorCounter > 0f)
            {
                m_currentHitIndicatorCounter -= 0.0166666675f;
                if (m_currentHitIndicatorCounter < 0f)
                    m_currentHitIndicatorCounter = 0f;

                float alpha = m_currentHitIndicatorCounter / 0.8f;
                alpha = 1f - alpha;
                alpha *= alpha;
                alpha = 1f - alpha;
                DrawDamageEffect(alpha * alpha, m_currentHitIndicatorSprite);
            }
        }

        private void DrawDamageEffect(float alpha, string texture)
        {
            RectangleF rectangleF = new RectangleF(0f, 0f, (float) MyGuiManager.GetFullscreenRectangle().Width, (float) MyGuiManager.GetFullscreenRectangle().Height);
            Rectangle? rectangle = null;
            MyRenderProxy.DrawSprite(texture, ref rectangleF, false, ref rectangle, new Color(new Vector4(1f, 1f, 1f, alpha)), 0f, new Vector2(1f, 0f), ref Vector2.Zero,
                SpriteEffects.None, 0f);
        }

        private const string DEFAULT_HIT_EFFECT = "Textures\\Gui\\ScreenEffect_Blood.png";

        private const float HIT_INDICATOR_LENGTH = 0.8f;

        private MyEntityStatComponent m_statComponent;

        private MyCharacterDamageComponent m_damageComp;

        private float m_currentHitIndicatorCounter;

        private string m_currentHitIndicatorSprite;

        private bool m_headRenderingEnabled = true;

        private MyRenderComponentCharacterDefinition m_definition;

        private bool m_afterFirstUpdate;
    }

    internal class MyDebugRenderComponentCharacter : MyDebugRenderComponent
    {
        public MyDebugRenderComponentCharacter(MyEntity entity) : base(entity)
        {
        }

        public override void DebugDraw()
        {
            if (MyDebugDrawSettings.DEBUG_DRAW_SHOW_DAMAGE)
            {
                MyRenderProxy.DebugDrawText3D(Entity.WorldMatrix.Translation + Entity.WorldMatrix.Up, "velocity:" + m_lastCharacterVelocity, Color.Red, 1.5f, false,
                    MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER);
                m_lastCharacterVelocity = Math.Max(m_lastCharacterVelocity, Entity.Physics.LinearVelocity.Length());
            }
        }

        private float m_lastCharacterVelocity;
    }
}
#endif