using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Cascade.Core
{
    /// <summary>
    /// An immutable list with value equality (like a tuple). Events, levels and goals use it
    /// so that records compare by content. Implicitly convertible from arrays and lists (copied).
    /// </summary>
    public sealed class ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
    {
        /// <summary>The empty list.</summary>
        public static readonly ValueList<T> Empty = new ValueList<T>(new T[0]);

        readonly T[] _items;

        public ValueList(IEnumerable<T> items) : this(new List<T>(items).ToArray()) { }

        ValueList(T[] ownedItems) { _items = ownedItems; }

        /// <summary>Wraps an array without copying; the caller must not modify it afterwards.</summary>
        internal static ValueList<T> Wrap(T[] items) => new ValueList<T>(items);

        public int Count => _items.Length;
        public T this[int index] => _items[index];

        /// <summary>A copy of the contents as an array.</summary>
        public T[] ToArray() => (T[])_items.Clone();

        public bool Equals(ValueList<T>? other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is null || other._items.Length != _items.Length) return false;
            var cmp = EqualityComparer<T>.Default;
            for (int i = 0; i < _items.Length; i++)
                if (!cmp.Equals(_items[i], other._items[i])) return false;
            return true;
        }

        public override bool Equals(object? obj) => obj is ValueList<T> o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                var cmp = EqualityComparer<T>.Default;
                foreach (var item in _items) h = h * 31 + (item is null ? 0 : cmp.GetHashCode(item));
                return h;
            }
        }

        public static bool operator ==(ValueList<T>? a, ValueList<T>? b) => a is null ? b is null : a.Equals(b);
        public static bool operator !=(ValueList<T>? a, ValueList<T>? b) => !(a == b);

        public static implicit operator ValueList<T>(T[] items) => new ValueList<T>((T[])items.Clone());
        public static implicit operator ValueList<T>(List<T> items) => new ValueList<T>(items.ToArray());

        public override string ToString()
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < _items.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(_items[i]);
            }
            return sb.Append(']').ToString();
        }

        public Enumerator GetEnumerator() => new Enumerator(_items);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(_items);
        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(_items);

        public struct Enumerator : IEnumerator<T>
        {
            readonly T[] _a;
            int _i;
            internal Enumerator(T[] a) { _a = a; _i = -1; }
            public T Current => _a[_i];
            object? IEnumerator.Current => _a[_i];
            public bool MoveNext() => ++_i < _a.Length;
            public void Reset() { _i = -1; }
            public void Dispose() { }
        }
    }

    /// <summary>Factory helpers for <see cref="ValueList{T}"/>.</summary>
    public static class ValueList
    {
        /// <summary>Builds a list from the arguments: <c>ValueList.Of(1, 2, 3)</c>.</summary>
        public static ValueList<T> Of<T>(params T[] items) => items;

        /// <summary>Builds a list from any sequence.</summary>
        public static ValueList<T> From<T>(IEnumerable<T> items) => new ValueList<T>(items);
    }
}
