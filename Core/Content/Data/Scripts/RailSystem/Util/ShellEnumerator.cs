using System.Collections;
using System.Collections.Generic;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util
{
    public struct ShellEnumerator : IEnumerator<Vector3I>
    {
        public readonly Vector3I Min, Max;

        public Vector3I Current { get; private set; }

        public ShellEnumerator(Vector3I min, Vector3I max)
        {
            Min = min;
            Max = max;
            Current = new Vector3I(min.X - 1, min.Y, min.Z);
        }

        public bool MoveNext()
        {
            var c = Current;
            try
            {
                var mid = c.Y != Min.Y && c.Y != Max.Y && c.Z != Min.Z && c.Z != Max.Z;
                if (mid)
                    c.X += Max.X - Min.X;
                else
                    c.X++;

                if (c.X <= Max.X)
                    return true;
                c.X = Min.X;
                c.Y++;
                if (c.Y <= Max.Y)
                    return true;
                c.Y = Min.Y;
                c.Z++;
                return c.Z <= Max.Z;
            }
            finally
            {
                Current = c;
            }
        }

        public void Reset()
        {
            Current = Min;
        }

        object IEnumerator.Current => Current;

        public void Dispose()
        {
        }
    }
}