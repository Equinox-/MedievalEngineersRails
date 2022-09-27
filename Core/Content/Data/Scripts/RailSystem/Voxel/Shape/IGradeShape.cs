using System;
using System.Collections.Generic;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Voxel.Shape
{
    public interface IGradeShape
    {
        float GetDensity(in Vector3D worldCoord);
        bool IsInside(Vector3D targetPosition);

        void DrawShape(MyStringId material);
        void DrawQuery(Vector3D pos);

        void CollectCacheKeys(Action<BoundingBoxD, object> consumer);
    }
}