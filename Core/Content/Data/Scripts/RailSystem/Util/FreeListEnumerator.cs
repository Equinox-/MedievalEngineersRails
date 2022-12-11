using System.Collections;
using System.Collections.Generic;
using VRage.Library.Collections;

namespace Equinox76561198048419394.RailSystem.Util
{
    public static class FreeListEnumerator
    {
        public static FreeListEnumerator<T> GetEnumerator<T>(this MyFreeList<T> list) => new FreeListEnumerator<T>(list);
        public static FreeListIdEnumerator<T> GetIdEnumerator<T>(this MyFreeList<T> list) => new FreeListIdEnumerator<T>(list);

        public static FreeListIdReader<T> Ids<T>(this MyFreeList<T> list) => new FreeListIdReader<T>(list);
    }

    public readonly struct FreeListIdReader<T>
    {
        private readonly MyFreeList<T> _backing;
        public FreeListIdReader(MyFreeList<T> backing) => _backing = backing;
        public FreeListIdEnumerator<T> GetEnumerator() => _backing.GetIdEnumerator();
    }

    public readonly struct FreeListReader<T>
    {
        private readonly MyFreeList<T> _backing;

        private FreeListReader(MyFreeList<T> backing) => _backing = backing;

        public static implicit operator FreeListReader<T>(MyFreeList<T> backing) => new FreeListReader<T>(backing);

        public ref readonly T this[int index] => ref _backing.GetRef(index);

        public int Count => _backing.Count;

        public FreeListIdReader<T> Ids => _backing.Ids();

        public FreeListReadOnlyEnumerator<T> GetEnumerator() => new FreeListReadOnlyEnumerator<T>(_backing.GetEnumerator());

        public FreeListIdEnumerator<T> GetIdEnumerator() => _backing.GetIdEnumerator();
    }

    public struct FreeListIdEnumerator<T> : IEnumerator<int>
    {
        private FreeListEnumerator<T> _backing;

        public FreeListIdEnumerator(MyFreeList<T> backing)
        {
            _backing = backing.GetEnumerator();
        }

        public bool MoveNext() => _backing.MoveNext();

        public void Reset() => _backing.Reset();
        public int Current => _backing.Index;

        public void Dispose() => _backing.Dispose();

        object IEnumerator.Current => Current;
    }

    public struct FreeListEnumerator<T> : IEnumerator<T>
    {
        private readonly MyFreeList<T> _backing;

        public FreeListEnumerator(MyFreeList<T> backing)
        {
            _backing = backing;
            Index = -1;
        }

        public void Dispose()
        {
        }

        public bool MoveNext()
        {
            while (Index < _backing.UsedLength)
            {
                Index++;
                if (_backing.IsValidKey(Index))
                    return true;
            }

            return false;
        }

        public void Reset() => Index = -1;

        public int Index { get; private set; }

        public ref T Current => ref _backing.GetRef(Index);

        T IEnumerator<T>.Current => Current;

        object IEnumerator.Current => Current;
    }

    public struct FreeListReadOnlyEnumerator<T> : IEnumerator<T>
    {
        private FreeListEnumerator<T> _backing;

        public FreeListReadOnlyEnumerator(FreeListEnumerator<T> backing)
        {
            _backing = backing;
        }

        public void Dispose()
        {
        }

        public bool MoveNext() => _backing.MoveNext();

        public void Reset() => _backing.Reset();

        public int Index => _backing.Index;

        public ref readonly T Current => ref _backing.Current;

        T IEnumerator<T>.Current => Current;

        object IEnumerator.Current => Current;
    }
}