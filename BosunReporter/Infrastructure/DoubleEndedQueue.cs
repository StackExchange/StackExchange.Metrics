using System;

namespace BosunReporter.Infrastructure
{
    /// <summary>
    /// This class is definitely not thread safe. Make sure to use locks in a multi-threaded environment.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    class DoubleEndedQueue<T>
    {
        T[] _array;
        int _head; // index of the oldest item in the array
        int _tail; // index where the next item will be written

        public int Count { get; private set; }

        public DoubleEndedQueue(int capacity = 8)
        {
            if (capacity <= 0)
                throw new Exception("DoubleEndedQueue initial capacity must be at least 1.");

            _array = new T[capacity];
        }

        public void Push(T item)
        {
            if (Count == _array.Length)
                Grow();

            _array[_tail] = item;

            Count++;
            _tail++;
            if (_tail >= _array.Length)
                _tail = 0;
        }

        public bool TryPopOldest(out T item)
        {
            if (Count == 0)
            {
                item = default(T);
                return false;
            }

            item = _array[_head];
            _array[_head] = default(T);

            Count--;
            _head++;
            if (_head >= _array.Length)
                _head = 0;

            return true;
        }

        public bool TryPopNewest(out T item)
        {
            if (Count == 0)
            {
                item = default(T);
                return false;
            }

            _tail--;
            if (_tail < 0)
                _tail = _array.Length - 1;

            item = _array[_tail];
            _array[_tail] = default(T);

            Count--;

            return true;
        }

        void Grow()
        {
            var oldArray = _array;
            var newArray = new T[_array.Length * 2];

            if (_head == 0)
            {
                // easy optimization in the event head and tail are perfectly aligned with the array
                Array.Copy(oldArray, newArray, Count);
            }
            else
            {
                var frontLength = oldArray.Length - _head;
                Array.Copy(oldArray, _head, newArray, 0, frontLength);
                Array.Copy(oldArray, 0, newArray, frontLength, Count - frontLength);
            }

            _array = newArray;

            _head = 0;
            _tail = Count;
        }
    }
}