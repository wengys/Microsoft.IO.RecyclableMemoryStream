// ---------------------------------------------------------------------
// Copyright (c) 2015 Microsoft
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
// ---------------------------------------------------------------------

namespace Microsoft.IO
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Linq;
    using System.Threading;

    /// <summary>
    /// Manages pools of RecyclableMemoryStream objects.
    /// </summary>
    /// <remarks>
    [Obsolete("演示用，请访问 https://github.com/Microsoft/Microsoft.IO.RecyclableMemoryStream 以获取正式版本")]
    public partial class RecyclableMemoryStreamManager
    {
        public const int DefaultBlockSize = 128 * 1024;

        private readonly int blockSize;
        
        private readonly ConcurrentStack<byte[]> smallPool;

        /// <summary>
        /// Initializes the memory manager with the default block/buffer specifications.
        /// </summary>
        public RecyclableMemoryStreamManager()
            : this(DefaultBlockSize) { }

        /// <summary>
        /// Initializes the memory manager with the given block requiredSize.
        /// </summary>
        /// <param name="blockSize">Size of each block that is pooled. Must be > 0.</param>
        /// <exception cref="ArgumentOutOfRangeException">blockSize is not a positive number, or largeBufferMultiple is not a positive number, or maximumBufferSize is less than blockSize.</exception>
        public RecyclableMemoryStreamManager(int blockSize)
        {
            if (blockSize <= 0)
            {
                throw new ArgumentOutOfRangeException("blockSize", blockSize, "blockSize must be a positive number");
            }

            this.blockSize = blockSize;
            
            this.smallPool = new ConcurrentStack<byte[]>();
        }

        /// <summary>
        /// The size of each block. It must be set at creation and cannot be changed.
        /// </summary>
        public int BlockSize
        {
            get { return this.blockSize; }
        }

        /// <summary>
        /// Removes and returns a single block from the pool.
        /// </summary>
        /// <returns>A byte[] array</returns>
        internal byte[] GetBlock()
        {
            byte[] block;
            if (!this.smallPool.TryPop(out block))
            {
                // We'll add this back to the pool when the stream is disposed
                // (unless our free pool is too large)
                block = new byte[this.BlockSize];
            }
            return block;
        }

        /// <summary>
        /// Returns the blocks to the pool
        /// </summary>
        /// <param name="blocks">Collection of blocks to return to the pool</param>
        /// <exception cref="ArgumentNullException">blocks is null</exception>
        /// <exception cref="ArgumentException">blocks contains buffers that are the wrong size (or null) for this memory manager</exception>
        internal void ReturnBlocks(ICollection<byte[]> blocks)
        {
            if (blocks == null)
            {
                throw new ArgumentNullException("blocks");
            }

            var bytesToReturn = blocks.Count * this.BlockSize;

            foreach (var block in blocks)
            {
                if (block == null || block.Length != this.BlockSize)
                {
                    throw new ArgumentException("blocks contains buffers that are not BlockSize in length");
                }
            }

            foreach (var block in blocks)
            {
                this.smallPool.Push(block);
            }
        }
       
        /// <summary>
        /// Retrieve a new MemoryStream object with no tag and a default initial capacity.
        /// </summary>
        /// <returns>A MemoryStream.</returns>
        public MemoryStream GetStream()
        {
            return new RecyclableMemoryStream(this);
        }
    }
}