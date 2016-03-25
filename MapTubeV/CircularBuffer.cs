using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace MapTubeV
{
    /// <summary>
    /// Generic circular buffer template. Buffer is filled from a delegate on the get operation.
    /// Thread Safe.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class CircularBuffer<T> where T : new()
    {
        private object LockBuffer = new object(); //thread lock for operations on buffer

        private class BufferEntry
        {
            public string Key;
            public T Data;
        }
        protected int _BufferSize = 100;
        private BufferEntry[] Buffer; //circular buffer
        private int BufferPos = 0;

        public CircularBuffer()
        {
            //this.BufferSize = Size;
            Buffer = new BufferEntry[_BufferSize];
        }

        /// <summary>
        /// Get or set the Buffer Size. A set will clear the current buffer contents
        /// </summary>
        public int BufferSize
        {
            get { return _BufferSize; }
            set
            {
                _BufferSize = value;
                Buffer = new BufferEntry[_BufferSize];
            }
        }

        /// <summary>
        /// Flush the circular buffer of all data
        /// </summary>
        public void FlushBuffer()
        {
            lock (LockBuffer)
            {
                for (int i = 0; i < Buffer.Length; i++)
                    Buffer[i] = null;
            }
        }

        /// <summary>
        /// Delegate to fill the circular buffer if a get is performed and no data is found.
        /// </summary>
        /// <param name="Key"></param>
        /// <returns></returns>
        public delegate T Fill(string Key);

        public T Get(string Key, Fill FillDelegate)
        {
            //TODO: If we have to load a new descriptor, it might be worth checking the available memory first and
            //release all the data if memory is running low. This would prevent the system from locking up if 100x
            //entries exceed the available memory

            lock (LockBuffer)
            {
                int i = BufferPos;
                do
                {
                    i = (i + _BufferSize - 1) % _BufferSize;
                    if (Buffer[i] == null) break; //if you find a null one, stop as there won't be any more non-null
                    if (Buffer[i].Key == Key)
                    {
                        return Buffer[i].Data;
                    }
                } while (i != BufferPos);

                //not found, so we're going to have to create one
                BufferEntry entry = new BufferEntry();
                entry.Key = Key;
                entry.Data = FillDelegate(Key);
                //if (Buffer[BufferPos] != null) Buffer[BufferPos].Dispose();
                Buffer[BufferPos] = entry;
                BufferPos = (BufferPos + 1) % _BufferSize;
                return entry.Data;
            }
        }



    }
}