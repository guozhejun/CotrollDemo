using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CotrollerDemo.Models
{
    public class TcpClientModel
    {
        public TcpClient Client;
        public NetworkStream Stream;
        public TcpListener Tcp;

        public byte[] PackLengths = [0x1D, 0, 0, 0]; // 包长度

        public byte[] HexValue = [0xFA, 0xFB, 0xFC, 0xFD, 0xDD, 0xCC, 0xBB, 0xAA]; // 发送包头

        public byte[] ReceiveValue = [0x2C, 0x04, 0xC0, 0xFF, 0xAA, 0xBB, 0xCC, 0xDD]; // 接收包头

        public byte[] TypeValues = [0x14, 1, 0x0B, 0, 0, 0, 0, 0, 0, 0, 0, 0]; // 类型值

        public int Version = 5; // 版本号

        public int PackLength = 29; // 包长度

        public float[] FloatArray = new float[256]; // 浮点数数组

        public int ChannelId; // 通道ID

        public int Segments; // 分段数

        public ChannelWriter<ReceiveData> ChannelWriter { get; set; }
        public ChannelReader<ReceiveData> ChannelReader { get; set; }

        public List<List<float>> SineWaveList { get; set; } = []; // 正弦波数据

        private readonly CancellationTokenSource _cts;
        private readonly object _dataLock = new object(); // 添加一个锁对象来同步数据处理

        public TcpClientModel()
        {
            _cts = new CancellationTokenSource();
            var dataChannel = Channel.CreateUnbounded<ReceiveData>(new UnboundedChannelOptions()
            {
                AllowSynchronousContinuations = true,
                SingleReader = false,
                SingleWriter = true,
            });
            //_dataChannel = Channel.CreateUnbounded<List<ReceiveData>>();
            ChannelWriter = dataChannel.Writer;
            ChannelReader = dataChannel.Reader;

            if (SineWaveList.Count <= 0)
            {
                for (int i = 0; i < 8; i++)
                {
                    SineWaveList.Add([]);
                }
            }
        }

        /// <summary>
        /// 开始监听Tcp服务端
        /// </summary>
        public void StartTcpListen()
        {
            Task.Run(async () =>
            {
                try
                {
                    Tcp = new TcpListener(GlobalValues.GetIpAdders(), 9089);
                    Tcp.Start();

                    Client = await Tcp.AcceptTcpClientAsync().ConfigureAwait(false);

                    Client.ReceiveBufferSize = 1072;
                    Client.ReceiveTimeout = 3000;

                    Stream = Client.GetStream();

                    ReceiveDataClient();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            });
        }

        /// <summary>
        /// 接收客户端发送的数据
        /// </summary>
        /// <returns></returns>
        public void ReceiveDataClient()
        {
            Task.Run(async () =>
            {
                byte[] buffers = new byte[1072];

                while (!_cts.IsCancellationRequested)
                {
                    int bytesRead;
                    if ((bytesRead = await Stream.ReadAsync(buffers, _cts.Token).ConfigureAwait(false)) != 0 && GlobalValues.IsRunning)
                    {
                        lock (_dataLock)
                        {
                            try
                            {
                                // 将数据放入队列
                                byte[] data = new byte[bytesRead];
                                Buffer.BlockCopy(buffers, 0, data, 0, bytesRead);

                                byte[] header = data[..8];
                                if (header.SequenceEqual(ReceiveValue))
                                {
                                    // 处理数据
                                    if (bytesRead > 48) // 确保数据长度足够
                                    {
                                        ChannelId = buffers[40];
                                        Segments = buffers[34];
                                        FloatArray = ConvertByteToFloat([.. buffers.Skip(48)]);

                                        if (ChannelId is >= 0 and < 8 && FloatArray.Length > 0)
                                        {
                                            ReceiveData receiveData = new()
                                            {
                                                ChannelId = ChannelId,
                                                Segments = Segments,
                                                Data = FloatArray
                                            };

                                            // 发送数据到通道
                                            ChannelWriter.WriteAsync(receiveData).ConfigureAwait(false).GetAwaiter()
                                               .GetResult();

                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error processing TCP data: {ex.Message}");
                            }
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 将字节数组转换为浮点数数组
        /// </summary>
        /// <param name="byteArray"></param>
        /// <returns></returns>
        private static float[] ConvertByteToFloat(byte[] byteArray)
        {
            // 每 4 个字节转换为一个 float
            int floatCount = byteArray.Length / 4;
            float[] floatArray = new float[floatCount];

            for (int i = 0; i < floatCount; i++)
            {
                // 提取 4 个字节
                floatArray[i] = BitConverter.ToSingle(byteArray, i * 4);
            }

            return floatArray;
        }

        /// <summary>
        /// 发送数据到客户端
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        public async Task SendDataClient(int param)
        {
            try
            {
                if (Stream != null)
                {
                    byte[] data =
                     [
                        .. PackLengths,
                        .. HexValue,
                        .. BitConverter.GetBytes(Version),
                        .. TypeValues,
                        .. BitConverter.GetBytes(PackLength),
                        (byte)param
                     ];

                    await Stream.WriteAsync(data, _cts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ErrorMessage：" + ex.Message);
            }
        }

        public void StopTcpListen()
        {
            try
            {
                _cts.Cancel();
                Stream?.Close();
                Client?.Close();
                Tcp?.Stop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error stopping TCP listener: " + ex.Message);
            }
        }
    }
}