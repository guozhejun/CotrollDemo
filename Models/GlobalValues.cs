using System.Collections.ObjectModel;
using System.Linq;
using System.Net;

namespace CotrollerDemo.Models
{
    public static class GlobalValues
    {
        /// <summary>
        /// UDP客户端
        /// </summary>
        public static UdpClientModel UdpClient { get; set; } = new();

        /// <summary>
        /// TCP客户端
        /// </summary>
        public static TcpClientModel TcpClient { get; set; } = new();

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public static bool IsRunning { get; set; } = false;


        public static ObservableCollection<DeviceInfoModel> Devices { get; set; } = [];

        /// <summary>
        /// 获取本机IP地址
        /// </summary>
        /// <returns></returns>
        public static IPAddress GetIpAdders()
        {
            string hostName = Dns.GetHostName();

            // 获取主机名对应的所有 IP 地址
            var addresses = Dns.GetHostEntry(hostName).AddressList;

            // 过滤出 IPv4 地址
            var ipAddress = addresses.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

            return ipAddress ?? IPAddress.Loopback;
        }
    }
}