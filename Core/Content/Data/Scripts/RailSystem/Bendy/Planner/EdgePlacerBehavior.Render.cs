using System;
using Equinox76561198048419394.RailSystem.Util.Curve;
using VRage.Components.Entity.Camera;
using VRage.Entities.Gravity;
using VRage.Game;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Bendy.Planner
{
    public partial class EdgePlacerBehavior
    {
        private struct Renderer
        {
            public readonly MyCameraComponent Camera;

            public const float DetailDistance = 100;

            public BoundingFrustumD EverythingFrustum;
            public BoundingFrustumD DetailFrustum;

            public Renderer(MyCameraComponent camera)
            {
                Camera = camera;
                EverythingFrustum = new BoundingFrustumD(Camera.GetViewProjMatrix());

                var detailProj = Camera.GetProjectionSetup();
                detailProj.FarPlane = 100;
                DetailFrustum = new BoundingFrustumD(Camera.GetViewMatrix() * detailProj.ProjectionMatrix);
            }

            public void DrawNode(Node node) => DrawNode(node.Position, node.Up);

            public void DrawNode(in EdgePlacerSystem.AnnotatedNode node) => DrawNode(node.Position, node.Up);

            public void DrawNode(Vector3D pos, Vector3 up, Vector4? color = null)
            {
                if (DetailFrustum.Contains(pos) == ContainmentType.Disjoint)
                    return;
                var rawColor = color ?? NodeColor;
                var top = pos + NodeMarkerSize * up;
                MySimpleObjectDraw.DrawLine(pos, top, SquareMaterial, ref rawColor, NodeWidth);
            }

            public void DrawCurve(ICurve curve, Color color, float width = 0.05f, float verticalOffset = 0.325f, float tStart = 0, float tEnd = 1)
            {
                var center = curve.Sample(0.5f);
                var bounds = new BoundingBoxD(center, center);
                bounds.Include(curve.Sample(0));
                bounds.Include(curve.Sample(1));

                if (!EverythingFrustum.Intersects(bounds))
                    return;
                
                var up = MyGravityProviderSystem.CalculateNaturalGravityInPoint(center);
                up *= -verticalOffset / up.Length();
                curve.Draw(color, tStart, tEnd, edgeWidth: width, upZero: up, upOne: up);
            }
        }
    }
}