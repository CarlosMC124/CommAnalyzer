using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CommAnalyzer.Helpers
{
    internal class FixedSizeQueue<T>
    {
        public readonly int _maxSize;
        public Queue<T> _queue = new Queue<T>();
        private readonly object _queueLockObj = new object();

        internal FixedSizeQueue(int maxSize)
        {
            _maxSize = maxSize;
        }

        internal void Enqueue(T item)
        {
            lock (_queueLockObj)
            {
                if (_queue.Count == _maxSize)
                {
                    _queue.Dequeue();
                }

                _queue.Enqueue(item);
            }
        }

        internal bool Contains(T item)
        {
            lock (_queueLockObj)
            {
                return _queue.Contains(item);
            }
        }
    }
}
