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
    using System.IO;
    using System.Collections.Generic;
    

    /// <summary>
    /// MemoryStream implementation that deals with pooling and managing memory streams which use potentially large
    /// buffers.
    /// </summary>
    [Obsolete("演示用，请访问 https://github.com/Microsoft/Microsoft.IO.RecyclableMemoryStream 以获取正式版本")]
    public sealed class RecyclableMemoryStream : MemoryStream
    {
        private const long MaxStreamLength = Int32.MaxValue;

        /// <summary>
        /// All of these blocks must be the same size
        /// </summary>
        private readonly List<byte[]> blocks = new List<byte[]>(1);

       
        
        private readonly RecyclableMemoryStreamManager memoryManager;

        /// <summary>
        /// Gets the memory manager being used by this stream.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Object has been disposed</exception>
        internal RecyclableMemoryStreamManager MemoryManager
        {
            get
            {
                this.CheckDisposed();
                return this.memoryManager;
            }
        }

        private bool disposed;

        /// <summary>
        /// This buffer exists so that WriteByte can forward all of its calls to Write
        /// without creating a new byte[] buffer on every call.
        /// </summary>
        private readonly byte[] byteBuffer = new byte[1];

        #region Constructors

        /// <summary>
        /// Allocate a new RecyclableMemoryStream object
        /// </summary>
        /// <param name="memoryManager">The memory manager</param>
        /// <param name="tag">A string identifying this stream for logging and debugging purposes</param>
        /// <param name="requestedSize">The initial requested size to prevent future allocations</param>
        /// <param name="initialLargeBuffer">An initial buffer to use. This buffer will be owned by the stream and returned to the memory manager upon Dispose.</param>
        public RecyclableMemoryStream(RecyclableMemoryStreamManager memoryManager)
        {
            this.memoryManager = memoryManager;
            this.EnsureCapacity(0);
            this.disposed = false;
        }
        #endregion

        #region Dispose and Finalize
        ~RecyclableMemoryStream()
        {
            this.Dispose(false);
        }

        /// <summary>
        /// Returns the memory used by this stream back to the pool.
        /// </summary>
        /// <param name="disposing">Whether we're disposing (true), or being called by the finalizer (false)</param>
        /// <remarks>This method is not thread safe and it may not be called more than once.</remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1816:CallGCSuppressFinalizeCorrectly", Justification = "We have different disposal semantics, so SuppressFinalize is in a different spot.")]
        protected override void Dispose(bool disposing)
        {
            if (this.disposed)
            {
                return;
            }
            
            if (disposing)
            {
                // Once this flag is set, we can't access any properties -- use fields directly
                this.disposed = true;
            
                GC.SuppressFinalize(this);
            }
            else
            {
                // We're being finalized.

                if (AppDomain.CurrentDomain.IsFinalizingForUnload())
                {
                    // If we're being finalized because of a shutdown, don't go any further.
                    // We have no idea what's already been cleaned up. Triggering events may cause
                    // a crash.
                    base.Dispose(disposing);
                    return;
                }
                
            }

           
            this.memoryManager.ReturnBlocks(this.blocks);
            
            base.Dispose(disposing);
        }

        /// <summary>
        /// Equivalent to Dispose
        /// </summary>
        public override void Close()
        {
            this.Dispose(true);
        }
        #endregion

        #region MemoryStream overrides
        /// <summary>
        /// Gets or sets the capacity
        /// </summary>
        /// <remarks>Capacity is always in multiples of the memory manager's block size, unless
        /// the large buffer is in use.  Capacity never decreases during a stream's lifetime. 
        /// Explicitly setting the capacity to a lower value than the current value will have no effect. 
        /// This is because the buffers are all pooled by chunks and there's little reason to 
        /// allow stream truncation.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">Object has been disposed</exception>
        public override int Capacity
        {
            get
            {
                this.CheckDisposed();

                if (this.blocks.Count > 0)
                {
                    return this.blocks.Count * this.memoryManager.BlockSize;
                }
                return 0;
            }
            set
            {
                this.CheckDisposed();
                this.EnsureCapacity(value);
            }
        }

        private int length;

        /// <summary>
        /// Gets the number of bytes written to this stream.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Object has been disposed</exception>
        public override long Length
        {
            get
            {
                this.CheckDisposed();
                return this.length;
            }
        }

        private int position;

        /// <summary>
        /// Gets the current position in the stream
        /// </summary>
        /// <exception cref="ObjectDisposedException">Object has been disposed</exception>
        public override long Position
        {
            get 
            { 
                this.CheckDisposed();
                return this.position; 
            }
            set
            {
                this.CheckDisposed();
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException("value", "value must be non-negative");
                }

                if (value > MaxStreamLength)
                {
                    throw new ArgumentOutOfRangeException("value", "value cannot be more than " + MaxStreamLength);
                }

                this.position = (int)value;
            }
        }

        /// <summary>
        /// Whether the stream can currently read
        /// </summary>
        public override bool CanRead
        {
            get { return !this.disposed; }
        }

        /// <summary>
        /// Whether the stream can currently seek
        /// </summary>
        public override bool CanSeek
        {
            get { return !this.disposed; }
        }

        /// <summary>
        /// Always false
        /// </summary>
        public override bool CanTimeout
        {
            get { return false; }
        }

        /// <summary>
        /// Whether the stream can currently write
        /// </summary>
        public override bool CanWrite
        {
            get { return !this.disposed; }
        }

        /// <summary>
        /// Returns a single buffer containing the contents of the stream.
        /// The buffer may be longer than the stream length.
        /// </summary>
        /// <returns>A byte[] buffer</returns>
        /// <remarks>IMPORTANT: Doing a Write() after calling GetBuffer() invalidates the buffer. The old buffer is held onto
        /// until Dispose is called, but the next time GetBuffer() is called, a new buffer from the pool will be required.</remarks>
        /// <exception cref="ObjectDisposedException">Object has been disposed</exception>
        public override byte[] GetBuffer()
        {
            this.CheckDisposed();

            throw new NotSupportedException("为简化代码结构，移除之。有兴趣的请参见官方代码。");
        }

        /// <summary>
        /// Returns a new array with a copy of the buffer's contents. You should almost certainly be using GetBuffer combined with the Length to 
        /// access the bytes in this stream. Calling ToArray will destroy the benefits of pooled buffers, but it is included
        /// for the sake of completeness.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Object has been disposed</exception>
        [Obsolete("This method has degraded performance vs. GetBuffer and should be avoided.")]
        public override byte[] ToArray()
        {
            this.CheckDisposed();
            var newBuffer = new byte[this.Length];

            this.InternalRead(newBuffer, 0, this.length, 0);

            return newBuffer;
        }

        /// <summary>
        /// Reads from the current position into the provided buffer
        /// </summary>
        /// <param name="buffer">Destination buffer</param>
        /// <param name="offset">Offset into buffer at which to start placing the read bytes.</param>
        /// <param name="count">Number of bytes to read.</param>
        /// <returns>The number of bytes read</returns>
        /// <exception cref="ArgumentNullException">buffer is null</exception>
        /// <exception cref="ArgumentOutOfRangeException">offset or count is less than 0</exception>
        /// <exception cref="ArgumentException">offset subtracted from the buffer length is less than count</exception>
        /// <exception cref="ObjectDisposedException">Object has been disposed</exception>
        public override int Read(byte[] buffer, int offset, int count)
        {
            this.CheckDisposed();
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException("offset", "offset cannot be negative");
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException("count", "count cannot be negative");
            }

            if (offset + count > buffer.Length)
            {
                throw new ArgumentException("buffer length must be at least offset + count");
            }

            int amountRead = this.InternalRead(buffer, offset, count, this.position);
            this.position += amountRead;
            return amountRead;
        }

        /// <summary>
        /// Writes the buffer to the stream
        /// </summary>
        /// <param name="buffer">Source buffer</param>
        /// <param name="offset">Start position</param>
        /// <param name="count">Number of bytes to write</param>
        /// <exception cref="ArgumentNullException">buffer is null</exception>
        /// <exception cref="ArgumentOutOfRangeException">offset or count is negative</exception>
        /// <exception cref="ArgumentException">buffer.Length - offset is not less than count</exception>
        /// <exception cref="ObjectDisposedException">Object has been disposed</exception>
        public override void Write(byte[] buffer, int offset, int count)
        {
            this.CheckDisposed();
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }
            
            if (offset < 0)
            {
                throw  new ArgumentOutOfRangeException("offset", offset, "Offset must be in the range of 0 - buffer.Length-1");
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException("count", count, "count must be non-negative");
            }

            if (count + offset > buffer.Length)
            {
                throw new ArgumentException("count must be greater than buffer.Length - offset");
            }

            int blockSize = this.memoryManager.BlockSize;
            long end = (long)this.position + count;
            // Check for overflow
            if (end > MaxStreamLength)
            {
                throw new IOException("Maximum capacity exceeded");
            }

            long requiredBuffers = (end + blockSize - 1) / blockSize;
            
            if (requiredBuffers * blockSize > MaxStreamLength)
            {
                throw new IOException("Maximum capacity exceeded");
            }

            this.EnsureCapacity((int)end);
           
            int bytesRemaining = count;
            int bytesWritten = 0;
            var blockAndOffset = this.GetBlockAndRelativeOffset(this.position);

            while (bytesRemaining > 0)
            {
                byte[] currentBlock = this.blocks[blockAndOffset.Block];
                int remainingInBlock = blockSize - blockAndOffset.Offset;
                int amountToWriteInBlock = Math.Min(remainingInBlock, bytesRemaining);

                Buffer.BlockCopy(buffer, offset + bytesWritten, currentBlock, blockAndOffset.Offset, amountToWriteInBlock);

                bytesRemaining -= amountToWriteInBlock;
                bytesWritten += amountToWriteInBlock;

                ++blockAndOffset.Block;
                blockAndOffset.Offset = 0;
            }
           
            this.position = (int)end;
            this.length = Math.Max(this.position, this.length);
        }

        /// <summary>
        /// Returns a useful string for debugging. This should not normally be called in actual production code.
        /// </summary>
        public override string ToString()
        {
            return string.Format("Length = {0:N0} bytes",this.Length);
        }

        /// <summary>
        /// Writes a single byte to the current position in the stream.
        /// </summary>
        /// <param name="value">byte value to write</param>
        /// <exception cref="ObjectDisposedException">Object has been disposed</exception>
        public override void WriteByte(byte value)
        {
            this.CheckDisposed();
            this.byteBuffer[0] = value;
            this.Write(this.byteBuffer, 0, 1);
        }

        /// <summary>
        /// Reads a single byte from the current position in the stream.
        /// </summary>
        /// <returns>The byte at the current position, or -1 if the position is at the end of the stream.</returns>
        /// <exception cref="ObjectDisposedException">Object has been disposed</exception>
        public override int ReadByte()
        {
            this.CheckDisposed();
            if (this.position == this.length)
            {
                return -1;
            }
            byte value = 0;
        
            var blockAndOffset = this.GetBlockAndRelativeOffset(this.position);
            value = this.blocks[blockAndOffset.Block][blockAndOffset.Offset];
           
            this.position++;
            return value;
        }

        /// <summary>
        /// Sets the length of the stream
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">value is negative or larger than MaxStreamLength</exception>
        /// <exception cref="ObjectDisposedException">Object has been disposed</exception>
        public override void SetLength(long value)
        {
            this.CheckDisposed();
            if (value < 0 || value > MaxStreamLength)
            {
                throw new ArgumentOutOfRangeException("value", "value must be non-negative and at most " + MaxStreamLength);
            }
            
            this.EnsureCapacity((int)value);

            this.length = (int)value;
            if (this.position > value)
            {
                this.position = (int) value;
            }
        }

        /// <summary>
        /// Sets the position to the offset from the seek location
        /// </summary>
        /// <param name="offset">How many bytes to move</param>
        /// <param name="loc">From where</param>
        /// <returns>The new position</returns>
        /// <exception cref="ObjectDisposedException">Object has been disposed</exception>
        /// <exception cref="ArgumentOutOfRangeException">offset is larger than MaxStreamLength</exception>
        /// <exception cref="ArgumentException">Invalid seek origin</exception>
        /// <exception cref="IOException">Attempt to set negative position</exception>
        public override long Seek(long offset, SeekOrigin loc)
        {
            this.CheckDisposed();
            if (offset > MaxStreamLength)
            {
                throw new ArgumentOutOfRangeException("offset", "offset cannot be larger than " + MaxStreamLength);
            }

            int newPosition;
            switch (loc)
            {
                case SeekOrigin.Begin:
                    newPosition = (int)offset;
                break;
                case SeekOrigin.Current:
                    newPosition = (int) offset + this.position;
                break;
                case SeekOrigin.End:
                    newPosition = (int) offset + this.length;
                break;
                default:
                    throw new ArgumentException("Invalid seek origin", "loc");
            }
            if (newPosition < 0)
            {
                throw new IOException("Seek before beginning");
            }
            this.position = newPosition;
            return this.position;
        }

        /// <summary>
        /// Synchronously writes this stream's bytes to the parameter stream.
        /// </summary>
        /// <param name="stream">Destination stream</param>
        /// <remarks>Important: This does a synchronous write, which may not be desired in some situations</remarks>
        public override void WriteTo(Stream stream)
        {
            this.CheckDisposed();
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }
            
           
            int currentBlock = 0;
            int bytesRemaining = this.length;

            while (bytesRemaining > 0)
            {
                int amountToCopy = Math.Min(this.blocks[currentBlock].Length, bytesRemaining);
                stream.Write(this.blocks[currentBlock], 0, amountToCopy);

                bytesRemaining -= amountToCopy;

                ++currentBlock;
            }
        }
        #endregion
        
        #region Helper Methods
        private void CheckDisposed()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(string.Format("The stream is disposed."));
            }
        }

        private int InternalRead(byte[] buffer, int offset, int count, int fromPosition)
        {
            if (this.length - fromPosition <= 0)
            {
                return 0;
            }
            
            var blockAndOffset = this.GetBlockAndRelativeOffset(fromPosition);
            int bytesWritten = 0;
            int bytesRemaining = Math.Min(count, this.length - fromPosition);

            while (bytesRemaining > 0)
            {
                int amountToCopy = Math.Min(this.blocks[blockAndOffset.Block].Length - blockAndOffset.Offset, bytesRemaining);
                Buffer.BlockCopy(this.blocks[blockAndOffset.Block], blockAndOffset.Offset, buffer, bytesWritten + offset, amountToCopy);

                bytesWritten += amountToCopy;
                bytesRemaining -= amountToCopy;

                ++blockAndOffset.Block;
                blockAndOffset.Offset = 0;
            }
            return bytesWritten;
        }

        private struct BlockAndOffset
        {
            public int Block;
            public int Offset;

            public BlockAndOffset(int block, int offset)
            {
                this.Block = block;
                this.Offset = offset;
            }
        }

        private BlockAndOffset GetBlockAndRelativeOffset(int offset)
        {
            var blockSize = this.memoryManager.BlockSize;
            return new BlockAndOffset(offset / blockSize, offset % blockSize);
        }

        private void EnsureCapacity(int newCapacity)
        {
            while (this.Capacity < newCapacity)
            {
                blocks.Add((this.memoryManager.GetBlock()));
            }
        }

        #endregion
    }
}
