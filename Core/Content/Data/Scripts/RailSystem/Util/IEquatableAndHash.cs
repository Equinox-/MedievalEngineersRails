using System;
using System.Collections.Generic;

namespace Equinox76561198048419394.RailSystem.Util
{
    public interface IEquatableAndHash<T> : IEquatable<T>
    {
        int GetHashCode();
    }

    public sealed class EquatableAndHashComparer<T> : IEqualityComparer<T> where T : IEquatableAndHash<T>
    {
        public static readonly EquatableAndHashComparer<T> Instance = new EquatableAndHashComparer<T>();

        private EquatableAndHashComparer()
        {
        }

        public bool Equals(T x, T y)
        {
            if (x == null) return y == null;
            return y != null && x.Equals(y);
        }

        public int GetHashCode(T obj)
        {
            return obj.GetHashCode();
        }
    }
}