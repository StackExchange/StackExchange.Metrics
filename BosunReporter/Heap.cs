using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace BosunReporter
{
    internal enum HeapMode
    {
        Min,
        Max
    }

    internal class Heap<T> where T : IComparable
    {
        private readonly List<T> _data;
        private readonly Func<T, T, int> _comparer;

        public readonly HeapMode HeapMode; 

        public int Count
        {
            get { return _data.Count; }
        }

        public Heap(HeapMode mode)
        {
            _data = new List<T>();

            HeapMode = mode;

            // the code in Pop and Push are coded as if the heap were always a MaxHeap. A MinHeap is achieved by creating a comparer which is opposite.
            if (mode == HeapMode.Max)
            {
                _comparer = (a, b) => a.CompareTo(b);
            }
            else
            {
                _comparer = (a, b) => b.CompareTo(a);
            }
        }

        public T Pop()
        {
            if (_data.Count == 0)
                throw new IndexOutOfRangeException("There are no elements available to extract from the Heap.");

            var topVal = _data[0];

            var newCount = _data.Count - 1;

            if (newCount > 0)
            {
                Swap(0, newCount);
                var index = 0;
                var item = _data[index];
                while (true)
                {
                    int lci = LeftChildIndex(index);
                    if (lci >= newCount)
                        break;

                    var lc = _data[lci];

                    int rci = RightChildIndex(index);
                    bool hasRightChild = rci < newCount;

                    if (!hasRightChild || GreaterThan(lc, _data[rci]))
                    {
                        if (LessThan(item, lc))
                        {
                            Swap(index, lci);
                            index = lci;
                            continue;
                        }
                    }
                    else if (LessThan(item, _data[rci]))
                    {
                        Swap(index, rci);
                        index = rci;
                        continue;
                    }

                    break;
                }
            }

            _data.RemoveAt(newCount);

            return topVal;
        }

        public void Push(T item)
        {
            var index = _data.Count;
            _data.Add(item);

            while (index > 0)
            {
                var parentIndex = ParentIndex(index);
                if (GreaterThan(item, _data[parentIndex]))
                {
                    Swap(index, parentIndex);
                    index = parentIndex;
                }
                else
                {
                    break;
                }
            }
        }

        public void Clear()
        {
            _data.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GreaterThan(T a, T b)
        {
            return _comparer(a, b) > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool LessThan(T a, T b)
        {
            return _comparer(a, b) < 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ParentIndex(int index)
        {
            return (index - 1) / 2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int LeftChildIndex(int index)
        {
            return (index + 1) * 2 - 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int RightChildIndex(int index)
        {
            return (index + 1) * 2;
        }

        private void Swap(int a, int b)
        {
            var tmp = _data[a];
            _data[a] = _data[b];
            _data[b] = tmp;
        }
    }
}
