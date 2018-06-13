using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using VRageRender.Animations;

namespace Equinox76561198048419394
{
    public static class Extensions
    {
        public static void SetTransform(this MyCharacterBone bone, Matrix matrix)
        {
            var q = Quaternion.CreateFromRotationMatrix(matrix);
            var t = matrix.Translation;
            bone.SetTransform(ref t, ref q);
        }

        public static bool IsServer(this IMySession session)
        {
            return MyAPIGateway.Multiplayer == null || MyAPIGateway.Multiplayer.IsServer;
        }
    }
}