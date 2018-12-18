using System;
using System.Collections.Generic;

namespace Equinox76561198048419394.RailSystem.Util
{
    public class UserDataTable
    {
        private Dictionary<Type, object> _table;

        public T Get<T>() where T : class
        {
            return (T) _table?.GetValueOrDefault(typeof(T));
        }

        public T GetOrAdd<T>(Func<UserDataTable, T> factory) where T : class
        {
            object res;
            if (_table != null && _table.TryGetValue(typeof(T), out res))
                return (T) res;
            var created = factory(this);
            Add<T>(created);
            return created;
        }

        public void Remove<T>()
        {
            _table?.Remove(typeof(T));
        }

        public void Add<T>(T val) where T : class
        {
            if (_table == null)
                _table = new Dictionary<Type, object>();
            _table.Add(typeof(T), val);
        }
    }
}