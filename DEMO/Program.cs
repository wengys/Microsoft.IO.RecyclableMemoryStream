using Microsoft.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DEMO
{
    class Program
    {
        //创建新的RecyclableMemoryStreamManager实例，以管理内存池
        static RecyclableMemoryStreamManager recyclableMemoryManager = new Microsoft.IO.RecyclableMemoryStreamManager();
        static void Main(string[] args)
        {
            //创建初始用的数据种子，大小与内存池中的内存块相同
            var seed = Enumerable.Repeat<byte>(1, recyclableMemoryManager.BlockSize).ToArray();
            //设定在流中填充两个内存块的数据
            var initLoops = 2;
            //计算数据总长度
            var streamLength = recyclableMemoryManager.BlockSize * initLoops;
            //获取第一个流
            var firstStream = recyclableMemoryManager.GetStream();
            //向流中写入数据，数据量占两个内存块
            for (int i = 0; i < initLoops; i++)
            {
                firstStream.Write(seed, 0, seed.Length); 
            }
            //关闭流，并将内存块还回内存池中
            firstStream.Dispose();

            //获取第二个流
            var secondStream = recyclableMemoryManager.GetStream();
            //手工设置流的容量，以从内存池中获得足够的内存块
            secondStream.Capacity = streamLength;
            //设置流长度，否则Read方法无法工作
            secondStream.SetLength(streamLength); 
            var dataFromPooledBlocks = new byte[streamLength];
            //读取流中所有数据，由于当前流内部使用的内存块是由内存池中分配出来的，故而可以读取到第一个流写入的数据
            secondStream.Read(dataFromPooledBlocks, 0, dataFromPooledBlocks.Length);
            //检查数据是否和第一个流写入的数据一致
            System.Diagnostics.Debug.Assert(dataFromPooledBlocks.All(elem => elem == 1)); 
            secondStream.Dispose();
        }
    }
}
