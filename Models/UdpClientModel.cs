using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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

        public UdpClientModel()
        {
            ServerIp = GlobalValues.GetIpAdders();
            int serverPort = 8080;
            UdpServer = new(serverPort);
            //ReceiveData();
        }

        /// <summary>
        /// 开始监听UDP接口
        /// </summary>
        /// <returns></returns>
        public void StartUdpListen()
        {
            //GlobalValues.Devices = [];
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

            Task.Run(async () =>
            {
                int sendResult = await UdpServer.SendAsync(bufferBytes, bufferBytes.Length,
                    new IPEndPoint(IPAddress.Parse("255.255.255.255"), 9090));

                if (sendResult > 0)
                {
                    var result = UdpServer.Receive(ref _receivePoint);

                    if (result.Length > 0)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ProcessData(result);
                        });
                    }
                }
            });
        }

        //public void ReceiveData()
        //{
        //    try
        //    {
        //        Task.Run(async () =>
        //        {
        //            while (true)
        //            {
        //                var result = UdpServer.Receive(ref _receivePoint);

        //                if (result.Length > 0)
        //                {
        //                    Application.Current.Dispatcher.Invoke(() =>
        //                    {
        //                        ProcessData(result);
        //                    });
        //                }
        //                await Task.Delay(10);
        //            }
        //        });
        //    }
        //    catch (Exception e)
        //    {
        //        MessageBox.Show(e.ToString());
        //    }
        //}

        //private byte[] temporaryArray = new byte[8];

        public void ProcessData(byte[] data)
        {
            //Array.Copy(data, temporaryArray, 8);

            //string[] hexArray = [.. temporaryArray.Select(b => b.ToString("X2"))];

            //ReceiveValue.SequenceEqual(hexArray) &&

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

            int sendResult = UdpServer.Send(bufferBytes, bufferBytes.Length, new IPEndPoint(ip, 9090));

            if (sendResult > 0)
            {
                var result = UdpServer.Receive(ref _receivePoint);

                if (result.Length > 0)
                {
                    ProcessData(result);
                }
            }
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