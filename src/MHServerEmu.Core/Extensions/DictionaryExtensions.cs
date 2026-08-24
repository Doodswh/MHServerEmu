using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MHServerEmu.Core.Extensions
{
    public static class DictionaryExtensions
    {
        /// <summary>
        /// Retrieves a <see langword="ref"/> to a value corresponding to a <typeparamref name="TKey"/> key in this <see cref="Dictionary{TKey, TValue}"/>.
        /// </summary>
        public static ref TValue GetValueRef<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, out bool found)
        {
            ref TValue value = ref CollectionsMarshal.GetValueRefOrNullRef(dictionary, key);
            found = Unsafe.IsNullRef(ref value) == false;
            return ref value;
        }

        /// <summary>
        /// Returns a <see langword="ref"/> to a value corresponding to a <typeparamref name="TKey"/> key in this <see cref="Dictionary{TKey, TValue}"/>.
        /// Adds a <see langword="default"/> value if no existing value corresponds to the specified key.
        /// </summary>
        /// <remarks>
        /// This has behavior similar to std::map indexers in C++.
        /// </remarks>
        public static ref TValue GetValueRefOrAddDefault<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key)
        {
            return ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, key, out _);
        }

        /// <summary>
        /// Returns a <see langword="ref"/> to a value corresponding to a <typeparamref name="TKey"/> key in this <see cref="Dictionary{TKey, TValue}"/>.
        /// Adds a <see langword="default"/> value if no existing value corresponds to the specified key.
        /// </summary>
        /// <remarks>
        /// This has behavior similar to std::map indexers in C++.
        /// </remarks>
        public static ref TValue GetValueRefOrAddDefault<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, out bool added)
        {
            ref TValue value = ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, key, out bool exists);
            added = !exists;
            return ref value;
        }
    }
}
