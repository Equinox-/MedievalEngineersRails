using System;
using System.Collections;
using System.Collections.Concurrent;

namespace Equinox76561198048419394.RailSystem.Util
{
    public class PoolManager
    {
        private static readonly ConcurrentDictionary<Type, ConcurrentStack<object>> _cache = new ConcurrentDictionary<Type, ConcurrentStack<object>>();


        public static ReturnHandle<T> Get<T>(out T result) where T : class, new()
        {
            var tmp = _cache.GetOrAdd(typeof(T), (x) => new ConcurrentStack<object>());
            object obj;
            if (!tmp.TryPop(out obj))
                obj = new T();
            result = (T) obj;
            return new ReturnHandle<T>(result);
        }

        public static void Return<T>(T value) where T : class
        {
            var coll = value as IList;
            coll?.Clear();
            _cache[typeof(T)].Push(value);
        }

        public struct ReturnHandle<T> : IDisposable where T : class
        {
            public readonly T Value;

            public ReturnHandle(T t)
            {
                Value = t;
            }

            public void Dispose()
            {
                Return(Value);
            }
        }
    }
}