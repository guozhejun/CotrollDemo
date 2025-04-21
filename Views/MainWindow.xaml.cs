using CotrollerDemo.Models;
using System;
using System.Windows;

namespace CotrollerDemo.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Closed(object sender, System.EventArgs e)
        {
            GlobalValues.UdpClient.StopUdpListen();
            GlobalValues.TcpClient.StopTcpListen();

            Environment.Exit(0);
        }
    }
}