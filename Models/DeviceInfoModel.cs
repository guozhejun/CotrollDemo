using System.Net;
using Prism.Mvvm;

namespace CotrollerDemo.Models
{
    public class DeviceInfoModel: BindableBase
    {
        /// <summary>
        /// Ip地址
        /// </summary>

        private IPAddress _iPAddress;

        public IPAddress IpAddress
        {
            get => _iPAddress;
            set => SetProperty(ref _iPAddress, value);
        }


        /// <summary>
        /// 设备序列号
        /// </summary>
        public string _serialNum;

        public string SerialNum
        {
            get => _serialNum;
            set => SetProperty(ref _serialNum, value);
        }

        /// <summary>
        /// 连接状态
        /// </summary>
        public string _status;

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        /// <summary>
        /// 连接设备IP
        /// </summary>
        public IPAddress _linkIp;

        public IPAddress LinkIp
        {
            get => _linkIp;
            set => SetProperty(ref _linkIp, value);
        }
    }
}