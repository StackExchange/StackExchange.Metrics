using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace StackExchange.Metrics.Infrastructure
{
    /// <summary>
    /// "Sort of" a list that allows items to be added, and to be enumerated. It uses
    /// an <see cref="ArrayPool{T}"/> to manage its underlying arrays.
    /// </summary>
    /// <remarks>
    /// Trimmed down from https://github.com/jtmueller/Collections.Pooled/blob/master/Collections.Pooled/PooledList.cs
    /// </remarks>
    internal class PooledList<T> : IEnumerable<T>, IDisposable
    {
        // internal constant copied from Array.MaxArrayLength
        private const int MaxArrayLength = 0x7FEFFFFF;
        private const int DefaultCapacity = 4;

        private ArrayPool<T> _pool;
        private T[] _items;
        private int _size;
        private int _capacity;

        /// <summary>
        /// Constructs a PooledList. The list is initially empty and has a capacity
        /// of zero. Upon adding the first element to the list the capacity is
        /// increased to DefaultCapacity, and then increased in multiples of two
        /// as required.
        /// </summary>
        public PooledList() : this(DefaultCapacity, ArrayPool<T>.Shared)
        {
        }

        /// <summary>
        /// Constructs a List with a given initial capacity. The list is
        /// initially empty, but will have room for the given number of elements
        /// before any reallocations are required.
        /// </summary>
        public PooledList(int capacity) : this(capacity, ArrayPool<T>.Shared) { }

        /// <summary>
        /// Constructs a List with a given initial capacity. The list is
        /// initially empty, but will have room for the given number of elements
        /// before any reallocations are required.
        /// </summary>
        private PooledList(int capacity, ArrayPool<T> pool)
        {
            _pool = pool ?? ArrayPool<T>.Shared;

            if (capacity == 0)
            {
                _items = Array.Empty<T>();
            }
            else
            {
                _items = _pool.Rent(capacity);
            }
        }

        /// <summary>
        /// Read-only property describing how many elements are in the List.
        /// </summary>
        public int Count => _size;

        /// <summary>
        /// Gets or sets the element at the given index.
        /// </summary>
        public T this[int index]
        {
            get => _items[index];
        }

        /// <summary>
        /// Adds the given object to the end of this list. The size of the list is
        /// increased by one. If required, the capacity of the list is doubled
        /// before adding the new element.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T item)
        {
            int size = _size;
            if ((uint)size < (uint)_items.Length)
            {
                _size = size + 1;
                _items[size] = item;
            }
            else
            {
                AddWithResize(item);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange(IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                Add(item);
            }
        }

        // Non-inline from List.Add to improve its code quality as uncommon path
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AddWithResize(T item)
        {
            int size = _size;
            EnsureCapacity(size + 1);
            _size = size + 1;
            _items[size] = item;
        }

        /// <summary>
        /// Ensures that the capacity of this list is at least the given minimum
        /// value. If the current capacity of the list is less than min, the
        /// capacity is increased to twice the current capacity or to min,
        /// whichever is larger.
        /// </summary>
        private void EnsureCapacity(int min)
        {
            if (_items.Length < min)
            {
                int newCapacity = _items.Length == 0 ? DefaultCapacity : _items.Length * 2;
                // Allow the list to grow to maximum possible capacity (~2G elements) before encountering overflow.
                // Note that this check works even when _items.Length overflowed thanks to the (uint) cast
                if ((uint)newCapacity > MaxArrayLength) newCapacity = MaxArrayLength;
                if (newCapacity < min) newCapacity = min;
                _capacity = newCapacity;
                if (newCapacity != _items.Length)
                {
                    if (newCapacity > 0)
                    {
                        var newItems = _pool.Rent(newCapacity);
                        if (_size > 0)
                        {
                            Array.Copy(_items, newItems, _size);
                        }
                        ReturnArray();
                        _items = newItems;
                    }
                    else
                    {
                        ReturnArray();
                        _size = 0;
                    }
                }
            }
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);

        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

        private void ReturnArray()
        {
            if (_items.Length == 0)
                return;

            try
            {
                // Clear the elements so that the gc can reclaim the references.
                _pool.Return(_items, clearArray: true);
            }
            catch (ArgumentException)
            {
                // oh well, the array pool didn't like our array
            }

            _items = Array.Empty<T>();
        }

        /// <summary>
        /// Returns the internal buffers to the ArrayPool.
        /// </summary>
        public void Dispose()
        {
            ReturnArray();
            _size = 0;
        }

        public struct Enumerator : IEnumerator<T>, IEnumerator
        {
            private readonly PooledList<T> _list;
            private int _index;
            private T _current;

            internal Enumerator(PooledList<T> list)
            {
                _list = list;
                _index = 0;
                _current = default;
            }

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if ((uint)_index < (uint)_list._size)
                {
                    _current = _list._items[_index++];
                    return true;
                }

                return false;
            }

            public T Current => _current;

            object IEnumerator.Current => Current;

            void IEnumerator.Reset()
            {
                _index = 0;
                _current = default;
            }
        }
    }
}
