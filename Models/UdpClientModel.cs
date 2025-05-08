using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CotrollerDemo.Models
{
    public class UdpClientModel
    {
        public IPAddress ServerIp; // 本机IP

        public byte[] HexValue = [0xFA, 0xFB, 0xFC, 0xFD, 0xDD, 0xCC, 0xBB, 0xAA]; // 发送包头

        public string[] ReceiveValue = ["00", "00", "C0", "FF", "AA", "BB", "CC", "DD"]; // 接收包头

        public int Version = 5; // 版本号

        public int PackLength = 10; // 包长度

        private IPEndPoint _receivePoint = new(IPAddress.Parse("255.255.255.255"), 9090); // 接收客户端的IP和端口

        public UdpClient UdpServer; // UDP服务端

        public int DeviceConnectState; // 设备连接状态

        public byte[] DeviceIpByte = new byte[4]; // IP地址字节数组
        public byte[] DeviceSerialNumByte = new byte[16]; // 序列号字节数组
        public string[] DeviceSerialNum = []; // 序列号

        // 添加接收超时设置
        private const int ReceiveTimeoutMs = 3000; // 3秒接收超时

        // 添加最大重试次数
        private const int MaxRetryCount = 3; // 最大重试次数

        // 添加重试间隔
        private const int RetryDelayMs = 1000; // 重试间隔时间(毫秒)

        // 添加锁对象确保线程安全
        private readonly object _udpLock = new object();

        public UdpClientModel()
        {
            ServerIp = GlobalValues.GetIpAdders();
            const int serverPort = 8080;
            UdpServer = new UdpClient(serverPort);
            //UdpServer.Client.ReceiveTimeout = ReceiveTimeoutMs; // 设置接收超时
        }

        /// <summary>
        /// 开始监听UDP接口
        /// </summary>
        /// <returns></returns>
        public void StartUdpListen()
        {
            byte[] typeValues = [1, 1, 1, 0, 0, 0, 0]; // 类型值

            byte[] bufferBytes =
            [
                .. HexValue,
                         .. BitConverter.GetBytes(Version),
                         .. typeValues,
                         .. BitConverter.GetBytes(PackLength),
                         .. ServerIp.GetAddressBytes(),
                         .. GetMacAddress()
            ];

            Task.Run(() =>
            {
                try
                {
                    TrySendAndReceive(bufferBytes, new IPEndPoint(IPAddress.Parse("255.255.255.255"), 9090));
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"UDP监听出错: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 尝试发送并接收数据，支持重试
        /// </summary>
        private bool TrySendAndReceive(byte[] bufferBytes, IPEndPoint endpoint, int maxRetries = 1)
        {
            bool success = false;
            int retryCount = 0;

            while (!success && retryCount <= maxRetries)
            {
                if (retryCount > 0)
                {
                    // 如果是重试，添加延迟
                    Thread.Sleep(RetryDelayMs);
                }

                // 使用锁确保同一时间只有一个线程访问UdpServer
                lock (_udpLock)
                {
                    int sendResult = UdpServer.Send(bufferBytes, bufferBytes.Length, endpoint);

                    if (sendResult > 0)
                    {
                        try
                        {
                            // 设置接收超时
                            var receiveTask = Task.Run(() => UdpServer.Receive(ref _receivePoint));
                            if (receiveTask.Wait(ReceiveTimeoutMs))
                            {
                                var result = receiveTask.Result;
                                if (result is { Length: > 0 })
                                {
                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        ProcessData(result);
                                    });
                                    success = true;
                                }
                            }
                            else
                            {
                                UdpServer?.Close();
                                UdpServer?.Dispose();
                                UdpServer = new UdpClient(8080);
                                TrySendAndReceive(bufferBytes, endpoint);
                                retryCount++;
                            }
                        }
                        catch (SocketException ex)
                        {
                            // 记录错误但继续重试
                            Debug.WriteLine($"接收数据时出错(重试 {retryCount}/{maxRetries}): {ex.Message}");
                        }
                    }
                }

            }

            // 仅在所有重试都失败时显示错误
            if (!success && maxRetries > 0)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"在 {maxRetries + 1} 次尝试后仍无法完成操作，请检查设备连接");
                });
            }

            return success;
        }

        public void ProcessData(byte[] data)
        {
            if (data.Length > 29)
            {
                // 获取接收到的IP
                Array.Copy(data, data.Length - 23, DeviceIpByte, 0, 4);

                IPAddress linkIp = new(DeviceIpByte);

                // 获取接收到的序列号
                Array.Copy(data, data.Length - 19, DeviceSerialNumByte, 0, 16);
                DeviceSerialNum = [.. DeviceSerialNumByte.Select(b => b.ToString("X2"))];

                DeviceConnectState = data[31];

                var dev = new DeviceInfoModel()
                {
                    IpAddress = _receivePoint.Address,
                    SerialNum = string.Join(":", DeviceSerialNum),
                    Status = DeviceConnectState is 1 ? "已连接" : "未连接",
                    LinkIp = linkIp
                };

                if (!GlobalValues.Devices.Select(t => Equals(t.IpAddress, dev.IpAddress)).Contains(true))
                {
                    GlobalValues.Devices.Add(dev);
                }
                else
                {
                    var tempDev = GlobalValues.Devices.First(d => Equals(d.IpAddress, dev.IpAddress));
                    tempDev.Status = dev.Status;
                }
            }
            else
            {
                var device = GlobalValues.Devices.First(d => Equals(d.IpAddress, _receivePoint.Address));
                if (data.Last() == 0)
                {
                    device.Status = device.Status == "未连接" ? "已连接" : "未连接";
                }
            }
        }

        /// <summary>
        /// 连接/断开设备
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="isConnect">是否连接</param>
        public void IsConnectDevice(IPAddress ip, bool isConnect)
        {
            Task.Run(() =>
            {
                try
                {
                    byte[] typeValues; // 类型值

                    if (isConnect)
                    {
                        typeValues = [1, 1, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0];
                    }
                    else
                    {
                        typeValues = [1, 1, 3, 0, 0, 0, 0];
                    }

                    byte[] bufferBytes =
                    [
                      .. HexValue,
                      .. BitConverter.GetBytes(Version),
                      .. typeValues,
                      .. BitConverter.GetBytes(PackLength),
                      .. ServerIp.GetAddressBytes(),
                      .. GetMacAddress()
                    ];

                    // 尝试发送并接收，支持自动重试
                    // 连接时使用最大重试次数，断开时只尝试一次
                    int retries = isConnect ? MaxRetryCount : 1;
                    bool success = TrySendAndReceive(bufferBytes, new IPEndPoint(ip, 9090), retries);

                    // 如果是连接操作且之前的重试都失败，显示连接状态
                    if (isConnect && !success)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show($"设备连接失败，请检查网络和设备状态", "连接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                        });
                    }
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"设备连接操作出错: {ex.Message}");
                    });
                }
            });
        }

        /// <summary>
        /// 获取本机Mac地址
        /// </summary>
        /// <returns></returns>
        public static byte[] GetMacAddress()
        {
            byte[] macBytes = [];

            // 获取所有网络接口
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var networkInterface in networkInterfaces)
            {
                // 检查网络接口类型是否为以太网
                if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                {
                    // 获取 MAC 地址
                    macBytes = networkInterface.GetPhysicalAddress().GetAddressBytes();
                }
            }

            return macBytes;
        }

        public void StopUdpListen()
        {
            try
            {
                UdpServer?.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error stopping UDP listener: " + ex.Message);
            }
        }
    }
}