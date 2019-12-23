using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RabbitRPC.Internal
{
    internal static class Extensions
    {
        private class DictionaryRemover<TKey, TValue> : IDisposable
        {
            private readonly TKey Key;
            private readonly IDictionary<TKey, TValue> Dictionary;

            public DictionaryRemover(TKey key, IDictionary<TKey, TValue> dictionary)
            {
                this.Key = key;
                this.Dictionary = dictionary;
            }

            public void Dispose()
            {
                if (Dictionary.ContainsKey(Key))
                    Dictionary.Remove(Key);
            }
        }

        public static IDisposable AddThenRemove<TKey, TValue>(this IDictionary<TKey, TValue> dic, TKey key, TValue value)
        {
            dic.Add(key, value);
            return new DictionaryRemover<TKey, TValue>(key, dic);
        }

        public static Type GetFirstGenericArg(this Type type)
        {
            if (!type.IsGenericType)
                return null;

            return type.GetGenericArguments()[0];
        }
    }
}
