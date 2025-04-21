using Arction.Wpf.Charting;
using Arction.Wpf.Charting.Annotations;
using Arction.Wpf.Charting.Axes;
using Arction.Wpf.Charting.SeriesXY;
using Arction.Wpf.Charting.Titles;
using Arction.Wpf.Charting.Views.ViewXY;
using CotrollerDemo.Models;
using DevExpress.Mvvm.Native;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Docking;
using DryIoc.ImTools;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using Application = System.Windows.Application;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ResizableTextBox = CotrollerDemo.Views.ResizableTextBox;
using TextEdit = DevExpress.Xpf.Editors.TextEdit;

namespace CotrollerDemo.ViewModels
{
    public class ControllerViewModel : BindableBase
    {
        #region Property

        private ObservableCollection<string> _fileNames = [];

        /// <summary>
        /// 文件名集合
        /// </summary>
        public ObservableCollection<string> FileNames
        {
            get => _fileNames;
            set => SetProperty(ref _fileNames, value);
        }

        private ObservableCollection<DeviceInfoModel> _devices;

        /// <summary>
        /// 设备列表
        /// </summary>
        public ObservableCollection<DeviceInfoModel> Devices
        {
            get => _devices;
            set => SetProperty(ref _devices, value);
        }

        //public LightningChart Chart = new();

        public List<LightningChart> Charts { get; set; } = [];

        private int _chartCount = 1;

        /// <summary>
        /// 曲线数量
        /// </summary>
        private readonly int _seriesCount = 8;

        /// <summary>
        /// 存放已生成的颜色
        /// </summary>
        private static readonly HashSet<Color> GeneratedColors = [];

        // 存放路径s
        public string FolderPath = @"D:\Datas";

        private bool _isRunning;

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                GlobalValues.IsRunning = value;
                IsDrop = !value;
                SetProperty(ref _isRunning, value);
            }
        }

        private bool _isConnect;

        public bool IsConnect
        {
            get => _isConnect;
            set => SetProperty(ref _isConnect, value);
        }


        private bool _isDrop;

        /// <summary>
        /// 是否可拖拽
        /// </summary>
        public bool IsDrop
        {
            get => _isDrop;
            set => SetProperty(ref _isDrop, value);
        }

        /// <summary>
        /// 正弦波数据
        /// </summary>
        public List<List<float>> SineWaves { get; set; } = [];

        /// <summary>
        /// 曲线点数数量
        /// </summary>
        public int[] PointNumbers = new int[8];

        private readonly ResizableTextBox _text = new();

        private readonly ConcurrentDictionary<int, List<float>> _dataBuffer = new();

        private readonly ConcurrentQueue<float[][]> _fileData = new();

        public SampleDataSeries Sample { get; set; } = new();
        public LineSeriesCursor Cursor { get; set; }

        public CancellationTokenSource Source = new();

        public Point MousePos = new();

        #endregion Property

        #region Command

        /// <summary>
        /// 开始试验
        /// </summary>
        public AsyncDelegateCommand StartTestCommand { get; set; }

        /// <summary>
        /// 停止试验
        /// </summary>
        public AsyncDelegateCommand StopTestCommand { get; set; }

        /// <summary>
        /// 查询设备
        /// </summary>
        public DelegateCommand DeviceQueryCommand { get; set; }

        /// <summary>
        /// 打开文件夹
        /// </summary>
        public DelegateCommand OpenFolderCommand { get; set; }

        /// <summary>
        /// 清空文件夹
        /// </summary>
        public DelegateCommand ClearFolderCommand { get; set; }

        /// <summary>
        /// 连接设备
        /// </summary>
        public DelegateCommand<object> ConnectCommand { get; set; }

        /// <summary>
        /// 断开连接
        /// </summary>
        public DelegateCommand<object> DisconnectCommand { get; set; }

        /// <summary>
        /// 删除文件
        /// </summary>
        public DelegateCommand<object> DeleteFileCommand { get; set; }

        /// <summary>
        /// 右键菜单
        /// </summary>
        public DelegateCommand<object> ShowMenuCommand { get; set; }

        /// <summary>
        /// 右键删除曲线
        /// </summary>
        public DelegateCommand<SampleDataSeries> DeleteSampleCommand { get; set; }

        /// <summary>
        /// 添加图表
        /// </summary>
        public DelegateCommand<object> AddChartCommand { get; set; }

        #endregion Command

        #region Main

        public ControllerViewModel()
        {

            Devices = [];
            UpdateDeviceList();
            GlobalValues.TcpClient.StartTcpListen();
            // 使用异步方法并立即启动任务，但不等待结果
            GetFolderFiles();
            var chart = CreateChart();
            Charts.Add(chart);
            //SaveData();

            IsRunning = false;

            StartTestCommand = new AsyncDelegateCommand(StartTest, CanStartChart).ObservesProperty(
                () => IsRunning
            );
            StopTestCommand = new AsyncDelegateCommand(StopTest, CanStopChart).ObservesProperty(
                () => IsRunning
            );
            OpenFolderCommand = new DelegateCommand(OpenFolder);
            DeviceQueryCommand = new DelegateCommand(UpdateDeviceList);
            ClearFolderCommand = new DelegateCommand(ClearFolder);
            ConnectCommand = new DelegateCommand<object>(ConnectDevice, CanConnectDevice).ObservesProperty(
                () => IsConnect
            );
            DisconnectCommand = new DelegateCommand<object>(DisconnectDevice, CanDisconnectDevice).ObservesProperty(
                () => IsConnect
            );
            DeleteFileCommand = new DelegateCommand<object>(DeleteFile);
            ShowMenuCommand = new DelegateCommand<object>(ShowMenu);
            DeleteSampleCommand = new DelegateCommand<SampleDataSeries>(DeleteSample);
            AddChartCommand = new DelegateCommand<object>(AddChart);
        }

        private bool CanConnectDevice(object arg)
        {
            return IsConnect;
        }
        private bool CanDisconnectDevice(object arg)
        {
            return !IsConnect;
        }

        /// <summary>
        /// 创建图表
        /// </summary>
        private LightningChart CreateChart()
        {
            var chart = new LightningChart();

            chart.PreviewMouseRightButtonDown += (s, e) => e.Handled = true;

            chart.BeginUpdate();

            chart.AllowDrop = true;
            chart.ViewXY.ZoomPanOptions.WheelZooming = WheelZooming.Off;
            chart.Title.Visible = false;
            chart.Title.Text = $"chart{_chartCount}";
            _chartCount++;
            var lineBaseColor = GenerateUniqueColor();

            var view = chart.ViewXY;

            view.DropOldEventMarkers = true;
            view.DropOldSeriesData = true;
            chart.Background = Brushes.Black;
            chart.ViewXY.GraphBackground.Color = Colors.Black;

            DisposeAllAndClear(view.PointLineSeries);
            DisposeAllAndClear(view.YAxes);

            // 设置X轴
            view.XAxes[0].LabelsVisible = true;
            view.XAxes[0].ScrollMode = XAxisScrollMode.Scrolling;
            view.XAxes[0].AllowUserInteraction = true;
            view.XAxes[0].AllowScrolling = false;
            view.XAxes[0].SetRange(0, 2048);
            view.XAxes[0].ValueType = AxisValueType.Number;
            view.XAxes[0].AutoFormatLabels = false;
            view.XAxes[0].LabelsNumberFormat = "N0";
            view.XAxes[0].MajorGrid.Pattern = LinePattern.Solid;
            view.XAxes[0].Title = null;
            view.XAxes[0].MajorGrid.Visible = false;
            view.XAxes[0].MinorGrid.Visible = false;

            // 设置Y轴
            var yAxis = new AxisY(view);
            yAxis.Title.Text = null;
            yAxis.Title.Visible = false;
            yAxis.Title.Angle = 0;
            yAxis.Title.Color = lineBaseColor;
            yAxis.Units.Text = null;
            yAxis.Units.Visible = false;
            yAxis.MajorGrid.Visible = false;
            yAxis.MinorGrid.Visible = false;
            yAxis.MajorGrid.Pattern = LinePattern.Solid;
            yAxis.AutoDivSeparationPercent = 0;
            yAxis.Visible = true;
            yAxis.SetRange(-5, 10); // 调整Y轴范围为正常波形范围
            yAxis.MajorGrid.Color = Colors.LightGray;
            view.YAxes.Add(yAxis);

            // 设置图例
            view.LegendBoxes[0].Layout = LegendBoxLayout.Vertical;
            view.LegendBoxes[0].Fill.Color = Colors.Transparent;
            view.LegendBoxes[0].Shadow.Color = Colors.Transparent;
            view.LegendBoxes[0].Position = LegendBoxPositionXY.TopRight;
            view.LegendBoxes[0].SeriesTitleMouseMoveOverOn +=
                ControllerViewModel_SeriesTitleMouseMoveOverOn;

            // 设置Y轴布局
            view.AxisLayout.AxisGridStrips = XYAxisGridStrips.X;
            view.AxisLayout.YAxesLayout = YAxesLayout.Stacked;
            view.AxisLayout.SegmentsGap = 2;
            view.AxisLayout.YAxisAutoPlacement = YAxisAutoPlacement.LeftThenRight;
            view.AxisLayout.YAxisTitleAutoPlacement = true;
            view.AxisLayout.AutoAdjustMargins = false;

            // 创建8条曲线，每条曲线颜色不同
            for (int i = 0; i < _seriesCount; i++)
            {
                var series = new SampleDataSeries(view, view.XAxes[0], view.YAxes[0])
                {
                    Title = new SeriesTitle() { Text = $"CH {i + 1}" },
                    //AllowUserInteraction = true,
                    LineStyle =
                    {
                        Color = ChartTools.CalcGradient(GenerateUniqueColor(), Colors.White, 50),
                    },
                    SampleFormat = SampleFormat.SingleFloat,
                };

                series.MouseOverOn += (sender, args) =>
                {
                    Sample = series;
                };

                SineWaves.Add([]);

                view.SampleDataSeries.Add(series);
            }

            chart.ViewXY.ZoomToFit();
            chart.AfterRendering += Chart_AfterRendering;
            chart.SizeChanged += new SizeChangedEventHandler(Chart_SizeChanged);
            chart.EndUpdate();
            CreateLineSeriesCursor(chart);

            return chart;
        }

        /// <summary>
        /// 移动到图例栏中的曲线标题时获取当前的曲线
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControllerViewModel_SeriesTitleMouseMoveOverOn(object sender, SeriesTitleDeviceMovedEventArgs e)
        {
            lock (Sample)
            {
                Sample = e.Series as SampleDataSeries;
            }
        }

        /// <summary>
        /// 是否可以停止绘制图表
        /// </summary>
        /// <returns></returns>
        private bool CanStopChart()
        {
            return IsRunning;
        }

        /// <summary>
        /// 是否可以开始绘制图表
        /// </summary>
        /// <returns></returns>
        private bool CanStartChart()
        {
            return !IsRunning;
        }

        /// <summary>
        /// 连接设备
        /// </summary>
        /// <param name="obj"></param>
        private void ConnectDevice(object obj)
        {
            if (obj is DeviceInfoModel selectItem)
            {
                var linkIp = Devices.First(d =>
                    Equals(d.IpAddress, selectItem.IpAddress)
                );

                GlobalValues.UdpClient.IsConnectDevice(linkIp.IpAddress, true);
                Devices = GlobalValues.Devices;
                IsConnect = false;
            }
        }

        /// <summary>
        /// 断开设备
        /// </summary>
        /// <param name="obj"></param>
        private void DisconnectDevice(object obj)
        {
            if (obj is DeviceInfoModel selectItem)
            {
                var linkIp = Devices.First(d =>
                    Equals(d.IpAddress, selectItem.IpAddress)
                );

                GlobalValues.UdpClient.IsConnectDevice(linkIp.IpAddress, false);
                Devices = GlobalValues.Devices;
                IsConnect = true;
            }
        }

        /// <summary>
        /// 更新设备列表
        /// </summary>
        private void UpdateDeviceList()
        {
            GlobalValues.UdpClient.StartUdpListen();
            Devices = GlobalValues.Devices;
        }

        public int CursorUpdateCounter;

        /// <summary>
        /// 更新曲线数据
        /// </summary>
        private void UpdateSeriesData()
        {
            try
            {
                if (!IsRunning || Source.IsCancellationRequested)
                    return;

                lock (_dataBuffer)
                {
                    // 使用TryRead而不是WaitToReadAsync，避免阻塞
                    while (GlobalValues.TcpClient.ChannelReader.TryRead(out var data))
                    {
                        if (data == null)
                            continue;

                        var channelId = data.ChannelId;
                        _dataBuffer.AddOrUpdate(channelId, _ => [.. data.Data], (_, list) =>
                        {
                            list.AddRange(data.Data);
                            return list;
                        });

                        Interlocked.Add(ref PointNumbers[channelId], data.Data.Length);
                    }
                }

                // 检查是否所有通道都达到了2048点
                bool allChannelsReady = PointNumbers.All(count => count >= 2048);
                if (!allChannelsReady)
                    return;

                // 准备所有通道的数据
                var channelData = new float[_seriesCount][];
                lock (_dataBuffer)
                {
                    for (int i = 0; i < _seriesCount; i++)
                    {
                        if (_dataBuffer.TryGetValue(i, out var buffer) && buffer.Count >= 2048)
                        {
                            channelData[i] = [.. buffer.Take(2048)];
                            // 移除已处理的数据
                            buffer.RemoveRange(0, 2048);
                            Interlocked.Add(ref PointNumbers[i], -2048);
                        }
                    }
                }

                // 检查是否所有通道都有有效数据，如果没有则跳过此次更新
                if (channelData.Any(data => data == null))
                    return;

                // 在UI线程中更新图表
                try
                {
                    // 一次性更新所有图表
                    foreach (var chart in Charts)
                    {
                        chart.BeginUpdate();
                        // 只更新数据，不更新光标
                        for (int i = 0; i < _seriesCount; i++)
                        {
                            if (channelData[i] != null)
                            {
                                var series = chart.ViewXY.SampleDataSeries[i];
                                series.SamplesSingle = channelData[i];
                            }
                        }
                        chart.EndUpdate();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error updating chart data: {ex.Message}");
                }

                // 将处理好的数据加入到队列
                _fileData.Enqueue(channelData);

                // 单独处理光标更新，使用较低的频率 每10次数据更新才更新一次光标显示
                if (Interlocked.Increment(ref CursorUpdateCounter) % 10 == 0)
                {
                    try
                    {
                        foreach (var chart in Charts)
                        {
                            UpdateCursorResult(chart);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error updating cursor: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 任务被取消，正常退出
                Debug.WriteLine("UpdateSeriesData task was cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in UpdateSeriesData: {ex.Message}");
            }
        }

        /// <summary>
        /// 每帧渲染时调用的事件处理程序
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CompositionTarget_Rendering(object sender, EventArgs e)
        {
            // 直接调用更新方法，每帧执行一次
            UpdateSeriesData();
        }

        /// <summary>
        /// 更新光标结果
        /// </summary>
        public void UpdateCursorResult(LightningChart chart)
        {
            try
            {
                chart.BeginUpdate();
                //获取注释
                var cursorValues = chart.ViewXY.Annotations;

                var targetYCoord = (float)chart.ViewXY.GetMarginsRect().Bottom - 20;
                chart.ViewXY.YAxes[0].CoordToValue(targetYCoord, out var y);

                // 更新每条曲线的注释
                var cursors = chart.ViewXY.LineSeriesCursors;

                var series = chart.ViewXY.SampleDataSeries;

                for (int i = 0; i < cursors.Count; i++)
                {
                    int cursorIndex = i * (series.Count + 1);

                    int seriesNumber = 1;

                    // 收集所有有效的Y值和对应的注释索引
                    var validAnnotations =
                        new List<(int Index, double YValue, string Text, string Title)>();

                    // 第一步：收集所有有效的注释
                    foreach (var t in series)
                    {
                        var title = t.Title.Text.Split(':')[0];
                        if (SolveValueAccurate(t, cursors[i].ValueAtXAxis, out double seriesYValue)
                            && !string.IsNullOrEmpty(title)
                            && title.Length > 0
                        )
                        {
                            // 保存注释信息
                            validAnnotations.Add
                                (
                                    (seriesNumber + cursorIndex, seriesYValue, $"{title}: {Math.Round(seriesYValue, 2)}", title)
                                );

                            // 更新曲线标题
                            t.Title.Text = $"{title}: {Math.Round(seriesYValue, 2)}";
                        }
                        else
                        {
                            // 如果无法解析Y值，确保注释是隐藏的
                            if (seriesNumber + cursorIndex < cursorValues.Count)
                            {
                                if (cursorValues[seriesNumber + cursorIndex].Text.Split([':'], 2)[0].Length > 4)
                                {
                                    cursorValues[seriesNumber + cursorIndex] = CreateAnnotation(
                                        chart
                                    );
                                }
                            }
                        }

                        seriesNumber++;
                    }

                    // 第二步：按Y值排序注释
                    validAnnotations.Sort((a, b) => a.YValue.CompareTo(b.YValue));

                    // 第三步：分配注释位置，避免重叠
                    const double minYSpacing = 0.6; // 注释之间的最小Y轴间距

                    // 应用排序后的位置
                    for (int j = 0; j < validAnnotations.Count; j++)
                    {
                        var annotation = cursorValues[validAnnotations[j].Index];
                        var originalY = validAnnotations[j].YValue;

                        // 调整Y位置以避免重叠
                        double adjustedY = originalY;

                        // 检查与前一个注释的间距
                        if (j > 0)
                        {
                            var prevY = validAnnotations[j - 1].YValue;
                            var prevYMax = cursorValues[validAnnotations[j - 1].Index]
                                .AxisValuesBoundaries
                                .YMax;

                            // 如果太接近前一个注释，调整位置
                            if (originalY - prevY < minYSpacing)
                            {
                                adjustedY = prevYMax + 0.5; // 在前一个注释下方放置
                            }
                        }

                        // 设置注释位置
                        annotation.AxisValuesBoundaries.XMax =
                              cursors[i].ValueAtXAxis
                            + (validAnnotations[j].Title.Length + validAnnotations[j]
                                    .YValue.ToString(CultureInfo.InvariantCulture)
                                    .Length) * 3 * Charts.Count;
                        annotation.AxisValuesBoundaries.XMin = cursors[i].ValueAtXAxis + 10;
                        annotation.AxisValuesBoundaries.YMax = adjustedY + 0.25;
                        annotation.AxisValuesBoundaries.YMin = adjustedY - 0.25;

                        // 设置注释内容
                        annotation.Text = validAnnotations[j].Text;
                        annotation.Visible = true;
                    }

                    // 设置X轴注释位置和内容
                    cursorValues[cursorIndex].AxisValuesBoundaries.XMax =
                        cursors[i].ValueAtXAxis + 45 * Charts.Count;
                    cursorValues[cursorIndex].AxisValuesBoundaries.XMin =
                        cursors[i].ValueAtXAxis + 15;
                    cursorValues[cursorIndex].AxisValuesBoundaries.YMax = y + 0.5;
                    cursorValues[cursorIndex].AxisValuesBoundaries.YMin = y;
                    cursorValues[cursorIndex].Visible = true;
                    cursorValues[cursorIndex].Text =
                        $"X: {Math.Round(cursors[i].ValueAtXAxis, 2)}";
                }
                chart.EndUpdate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"更新光标结果时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        private void CleanupResources()
        {
            // 清理数据缓冲区
            lock (_dataBuffer)
            {
                foreach (var key in _dataBuffer.Keys.ToList())
                {
                    if (_dataBuffer.TryRemove(key, out var buffer))
                    {
                        buffer.Clear();
                    }
                }
            }

            // 重置点数计数
            for (int i = 0; i < PointNumbers.Length; i++)
            {
                PointNumbers[i] = 0;
            }

            // 重置光标更新计数器
            CursorUpdateCounter = 0;
            // 清理文件数据队列
            while (_fileData.TryDequeue(out var data))
            {
                SaveData(data);
            }
        }

        /// <summary>
        /// </summary>
        /// 保存折线数据
        private void SaveData(float[][] data)
        {

            try
            {
                if (data.Length > 0)
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");

                    Parallel.For(0, _seriesCount, (channelId) =>
                    {

                        if (data[channelId] != null)
                        {
                            string fullPath = Path.Combine(
                                FolderPath,
                                $"{timestamp}_CH{channelId + 1}.txt"
                            );

                            // 使用FileShare.ReadWrite模式允许其他进程读取
                            using var fs = new FileStream(
                                fullPath,
                                FileMode.Create,
                                FileAccess.Write,
                                FileShare.ReadWrite,  // 修改这里
                                4096,
                                FileOptions.Asynchronous
                            );

                            // 使用using确保资源释放
                            using var sw = new StreamWriter(fs);

                            var lines = data[channelId]
                                .Select((t, j) => $"{j}-{Convert.ToDouble(t)}")
                                .ToList();

                            sw.Write(string.Join(Environment.NewLine, lines));
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SaveData task was cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"保存数据时出错: {ex.Message}");
            }
        }

        #region 右键菜单

        /// <summary>
        /// 显示右键菜单
        /// </summary>
        private void ShowMenu(object obj)
        {
            if (obj is not Canvas canvas)
                return;

            // 获取鼠标当前位置
            MousePos = System.Windows.Input.Mouse.GetPosition(canvas);

            if (canvas.Children[0] is LightningChart chart)
            {
                chart.BeginUpdate();
                string title = Sample.Title.Text.Split(":")[0];

                ContextMenu menu = new();

                if (Sample != null)
                {
                    if (title != "Series title" && title.Length > 4)
                    {
                        MenuItem deleteItem = new()
                        {
                            Header = "删除曲线",
                            Command = DeleteSampleCommand,
                            CommandParameter = Sample,
                        };
                        menu.Items.Add(deleteItem);
                    }

                    menu.Items.Add(
                        new MenuItem()
                        {
                            Header = "修改标题",
                            Command = new DelegateCommand<LightningChart>(UpdateTitle),
                            CommandParameter = chart
                        }
                    );
                }

                if (chart.ViewXY.SampleDataSeries.Count > _seriesCount)
                {
                    menu.Items.Add(
                        new MenuItem()
                        {
                            Header = "删除添加曲线",
                            Command = new DelegateCommand<LightningChart>(DeleteAllAddSeries),
                            CommandParameter = chart
                        }
                    );
                }

                menu.Items.Add(
                    new MenuItem()
                    {
                        Header = "切换图例",
                        Command = new DelegateCommand(() =>
                        {
                            chart.ViewXY.LegendBoxes[0].Visible = !chart.ViewXY.
                                LegendBoxes[0].
                                Visible;
                        }),
                    }
                );

                menu.Items.Add(
                    new MenuItem()
                    {
                        Header = "重置缩放",
                        Command = new DelegateCommand(() =>
                        {
                            chart.ViewXY.ZoomToFit();
                        }),
                    }
                );

                menu.Items.Add(
                    new MenuItem()
                    {
                        Header = "添加注释",
                        Command = new DelegateCommand(() => AddComment(canvas)),
                    }
                );

                menu.Items.Add(
                    new MenuItem()
                    {
                        Header = "添加光标",
                        Command = new DelegateCommand(() =>
                        {
                            // 直接调用方法，不使用异步调用
                            CreateLineSeriesCursor(chart);
                        }),
                    }
                );

                if (Cursor != null)
                {
                    menu.Items.Add(
                        new MenuItem()
                        {
                            Header = "删除光标",
                            Command = new DelegateCommand<LightningChart>(DeleteCursor),
                            CommandParameter = chart
                        }
                    );
                }

                chart.ContextMenu = menu;
                // 初始化图表事件处理
                chart.MouseRightButtonDown += (s, e) => e.Handled = true;
                chart.EndUpdate();
            }
        }

        /// <summary>
        /// 删除曲线
        /// </summary>
        /// <param name="sample"></param>
        private void DeleteSample(SampleDataSeries sample)
        {
            var chart = sample.OwnerView.OwnerChart;

            var title = sample.Title.Text.Split(':')[0];

            chart.ViewXY.SampleDataSeries.Remove(sample);
            if (title.Contains('_'))
            {
                if (chart.ViewXY.Annotations.Any(t => t.Text.Contains(title)))
                {
                    chart.ViewXY.Annotations.RemoveAll(t => t.Text.Contains(title));
                }
            }
            UpdateCursorResult(chart);
            Sample = new SampleDataSeries();
        }

        /// <summary>
        /// 添加光标
        /// </summary>
        /// <param name="chart"></param>
        public void CreateLineSeriesCursor(LightningChart chart)
        {
            chart.BeginUpdate();
            LineSeriesCursor cursor = new(chart.ViewXY, chart.ViewXY.XAxes[0])
            {
                Visible = true,
                SnapToPoints = true,
                ValueAtXAxis = 100,
            };
            cursor.LineStyle.Color = Color.FromArgb(150, 255, 0, 0);
            cursor.TrackPoint.Color1 = Colors.White;
            cursor.PositionChanged += Cursor_PositionChanged;
            cursor.MouseOverOn += Cursor_MouseOverOn;
            chart.ViewXY.LineSeriesCursors.Add(cursor);

            // 创建注释
            for (int i = 0; i < chart.ViewXY.SampleDataSeries.Count + 1; i++)
            {
                chart.ViewXY.Annotations.Add(CreateAnnotation(chart));
            }

            chart.EndUpdate();
            // 直接更新光标结果，不使用异步调用
            UpdateCursorResult(chart);
        }

        private void Cursor_MouseOverOn(object sender, System.Windows.Input.MouseEventArgs e)
        {
            Cursor = sender as LineSeriesCursor;
        }

        /// <summary>
        /// 创建注释
        /// </summary>
        /// <param name="chart"></param>
        public AnnotationXY CreateAnnotation(LightningChart chart)
        {
            //添加注释以显示游标值
            AnnotationXY annot = new(chart.ViewXY, chart.ViewXY.XAxes[0], chart.ViewXY.YAxes[0])
            {
                Style = AnnotationStyle.Rectangle,
                LocationCoordinateSystem = CoordinateSystem.RelativeCoordinatesToTarget,
                LocationRelativeOffset = new PointDoubleXY(60, 0),
                Sizing = AnnotationXYSizing.AxisValuesBoundaries,
                AxisValuesBoundaries = new BoundsDoubleXY(0, 0, -4, -3.5),
            };
            annot.TextStyle.Color = Colors.White;
            annot.TextStyle.Font = new WpfFont("Segoe UI", 12);

            // 将背景色修改为透明
            annot.Fill.Color = Colors.Transparent;
            annot.Fill.GradientFill = GradientFill.Solid;
            annot.Fill.GradientColor = Colors.Transparent;
            annot.ArrowLineStyle.Color = Colors.Transparent;
            annot.ArrowLineStyle.Width = 0;

            annot.BorderLineStyle.Color = Colors.Transparent;
            annot.BorderLineStyle.Width = 0;
            annot.BorderVisible = false;

            annot.AllowUserInteraction = false;
            annot.Visible = false;
            return annot;
        }

        /// <summary>
        /// 添加注释框
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        private void AddComment(Canvas canvas)
        {
            var text = new ResizableTextBox() { Width = 200, Height = 100 };

            Canvas.SetLeft(text, MousePos.X);
            Canvas.SetTop(text, MousePos.Y);

            canvas.Children.Add(text);
        }

        /// <summary>
        /// 修改曲线标题
        /// </summary>
        /// <param name="chart"></param>
        public void UpdateTitle(LightningChart chart)
        {
            var textEdit = new TextEdit()
            {
                Width = 200,
                Height = 30,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            var dialog = new DXDialog
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = "输入内容：" },
                        textEdit,
                    },
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                Buttons = DialogButtons.OkCancel,
                Width = 300,
                Height = 150,
                Title = "修改标题",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                WindowStyle = WindowStyle.ToolWindow,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowState = WindowState.Normal,
            };

            if (dialog.ShowDialog() == true)
            {
                if (
                    textEdit.EditValue != null
                    && (string)textEdit.EditValue != string.Empty
                )
                {
                    Sample.Title.Text = textEdit.Text;
                    UpdateCursorResult(chart);
                }
            }
        }

        /// <summary>
        /// 删除图表中所有添加的曲线
        /// </summary>
        /// <param name="chart"></param>
        public void DeleteAllAddSeries(LightningChart chart)
        {
            chart.BeginUpdate();

            chart.ViewXY.Annotations.RemoveAll(anno =>
                anno.Text != null
                && anno.Text.Split([':'], 2)[0].Length > 5
                && anno.Text != "Annotation"
            );

            chart.ViewXY.SampleDataSeries.RemoveAll(t =>
                t.Title.Text.Split([':'], 2)[0].Length > 5
            );

            chart.EndUpdate();

            UpdateCursorResult(chart);
        }

        /// <summary>
        /// 删除选中的光标
        /// </summary>
        /// <param name="chart"></param>
        public void DeleteCursor(LightningChart chart)
        {
            chart.BeginUpdate();
            int cursorIndex = chart.ViewXY.LineSeriesCursors.IndexOf(Cursor);
            int annoIndex =
                chart.ViewXY.LineSeriesCursors.Count <= 1 || cursorIndex == 0
                    ? 0
                    : chart.ViewXY.SampleDataSeries.Count * cursorIndex;
            chart.ViewXY.Annotations.RemoveRange(
                annoIndex,
                chart.ViewXY.SampleDataSeries.Count + 1
            );
            chart.ViewXY.LineSeriesCursors.Remove(Cursor);
            Cursor = null;

            chart.EndUpdate();
        }

        #endregion 右键菜单

        /// <summary>
        /// 获取文件夹下的所有文件
        /// </summary>
        private void GetFolderFiles()
        {
            try
            {
                // 确保文件夹存在
                if (!Directory.Exists(FolderPath))
                {
                    Directory.CreateDirectory(FolderPath);
                }

                // 使用EnumerateFiles代替GetFiles，这样文件可以在被发现时立即处理 而不需要等待整个列表构建完成
                Task.Run(async () =>
                 {
                     List<string> fileNames = [.. Directory.EnumerateFiles(FolderPath, "*.txt", SearchOption.TopDirectoryOnly).Select(Path.GetFileName)];

                     await Application.Current.Dispatcher.InvokeAsync(() =>
                       {
                           if (fileNames.Count > 0)
                           {
                               var list = fileNames.Where(f => !FileNames.Contains(f)).ToList();
                               FileNames.AddRange(list);
                           }
                           else
                           {
                               FileNames = [];
                           }
                       });
                 });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取文件列表时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 开始试验
        /// </summary>
        private async Task StartTest()
        {
            // 先清理资源，确保干净的起点
            CleanupResources();

            await GlobalValues.TcpClient.SendDataClient(1);

            IsRunning = true; // 更新运行状态

            // 启动数据保存任务
            //SaveData();

            // 添加渲染事件处理
            CompositionTarget.Rendering += CompositionTarget_Rendering;

            foreach (var chart in Charts)
            {
                int count = chart.ViewXY.PointLineSeries.Count;

                if (count > 8)
                {
                    for (int i = count; i > 8; i--)
                    {
                        chart.ViewXY.PointLineSeries.Remove(chart.ViewXY.PointLineSeries[i - 1]);
                        chart.ViewXY.YAxes.Remove(chart.ViewXY.YAxes[i - 1]);
                    }
                }
            }
        }

        /// <summary>
        /// 停止试验
        /// </summary>
        private async Task StopTest()
        {
            await GlobalValues.TcpClient.SendDataClient(0);

            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            IsRunning = false; // 更新运行状态

            // 清理资源
            CleanupResources();

            // 获取文件列表
            GetFolderFiles();
        }

        /// <summary>
        /// 更新光标位置
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Cursor_PositionChanged(object sender, PositionChangedEventArgs e)
        {
            //取消正在进行的呈现，因为下面的代码更新了图表。
            e.CancelRendering = true;

            var chart = e.Cursor.OwnerView.OwnerChart;

            Task.Run(async () =>
              await Application.Current.Dispatcher.InvokeAsync(() =>
              {
                  UpdateCursorResult(chart);
              }, DispatcherPriority.Background)
            );
        }

        /// <summary>
        /// 根据X值解决Y值
        /// </summary>
        /// <param name="series"></param>
        /// <param name="xValue"></param>
        /// <param name="yValue"></param>
        /// <returns></returns>
        private static bool SolveValueAccurate(SampleDataSeries series, double xValue, out double yValue)
        {
            yValue = 0;

            var result = series.SolveYValueAtXValue(xValue);
            string text = series.Title.Text;
            if (result.SolveStatus == LineSeriesSolveStatus.OK)
            {
                yValue = (result.YMax + result.YMin) / 2.0;
                return true;
            }
            else
            {
                return false;
            }
        }

        private void Chart_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var chart = sender as LightningChart;
            UpdateCursorResult(chart);
        }

        private void Chart_AfterRendering(object sender, AfterRenderingEventArgs e)
        {
            if (sender is not LightningChart chart)
                return;
            chart.AfterRendering -= Chart_AfterRendering;
            UpdateCursorResult(chart);
        }

        /// <summary>
        /// 释放所有并清除数组
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        public static void DisposeAllAndClear<T>(List<T> list)
            where T : IDisposable
        {
            if (list == null)
            {
                return;
            }

            while (list.Count > 0)
            {
                int lastInd = list.Count - 1;
                // ReSharper disable once SuggestVarOrType_SimpleTypes
                T item = list[lastInd]; // take item ref from list.
                list.RemoveAt(lastInd); // remove item first
                if (item != null)
                {
                    (item as IDisposable).Dispose(); // then dispose it.
                }
            }
        }

        /// <summary>
        /// 随机生成颜色
        /// </summary>
        /// <returns></returns>
        public Color GenerateUniqueColor()
        {
            Color color;
            Random random = new();
            do
            {
                byte a = (byte)random.Next(256);
                byte b = (byte)random.Next(256);
                byte c = (byte)random.Next(256);
                color = Color.FromRgb(a, b, c);
            } while (GeneratedColors.Contains(color));

            GeneratedColors.Add(color);
            return color;
        }

        /// <summary>
        /// 打开文件夹
        /// </summary>
        private void OpenFolder()
        {
            try
            {
                Process.Start("explorer.exe", FolderPath);
            }
            catch (Exception ex)
            {
                // 处理异常
                DXMessageBox.Show("无法打开文件夹: " + ex.Message);
            }
        }

        /// <summary>
        /// 清空文件夹
        /// </summary>
        private async void ClearFolder()
        {
            try
            {
                if ((DialogResult)DXMessageBox.Show("是否清空文件夹?", "提示", MessageBoxButton.YesNo) == DialogResult.Yes)
                {
                    // 删除文件夹中所有的文件
                    FileOperation.Delete(FolderPath);

                    // 刷新文件列表
                    GetFolderFiles();
                }
            }
            catch (Exception ex)
            {
                DXMessageBox.Show("清空文件夹时出错: " + ex.Message);
            }
        }

        /// <summary>
        /// 删除文件
        /// </summary>
        /// <param name="obj"></param>
        private void DeleteFile(object obj)
        {
            try
            {
                if (
                    (DialogResult)
                        DXMessageBox.Show("是否删除此文件?", "提示", MessageBoxButton.YesNo)
                    == DialogResult.Yes
                )
                {
                    if (obj is string fileName)
                    {
                        string file = Path.Combine(FolderPath, fileName);
                        try
                        {
                            // // 确保文件没有只读属性
                            if (File.Exists(file))
                            {
                                File.Delete(file);
                                FileNames.Remove(fileName);
                            }

                            //GetFolderFiles();
                            DXMessageBox.Show("文件已删除！");
                        }
                        catch (Exception ex)
                        {
                            DXMessageBox.Show($"无法删除文件: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 处理异常
                DXMessageBox.Show("操作过程中出错: " + ex.Message);
            }
        }

        private LayoutGroup _layoutGroup = new();

        /// <summary>
        /// 添加图表
        /// </summary>
        /// <param name="obj"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void AddChart(object obj)
        {
            _layoutGroup = obj as LayoutGroup;

            if (_layoutGroup != null)
            {
                var layPanel = new LayoutPanel();
                var canvas = new Canvas();
                var chart = CreateChart();

                // 初始化新图表的数据
                if (IsRunning)
                {
                    chart.BeginUpdate();
                    // 从_dataBuffer同步当前数据
                    for (int i = 0; i < _seriesCount; i++)
                    {
                        var series = chart.ViewXY.SampleDataSeries[i];
                        if (_dataBuffer.TryGetValue(i, out var buffer) && buffer.Count >= 1024)
                        {
                            var data = buffer.Take(1024).ToArray();
                            series.SamplesSingle = data;
                        }
                    }
                    chart.EndUpdate();
                }
                // 添加到Charts集合要在数据初始化之后
                Charts.Add(chart);

                UpdateCursorResult(chart);

                // 添加关闭事件处理
                layPanel.CloseCommand = new DelegateCommand(() =>
                {
                    // 从Charts集合中移除图表
                    if (Charts != null && Charts.Contains(chart))
                    {
                        // 释放图表资源
                        chart.Dispose();

                        Charts.Remove(chart);
                    }
                    layPanel.Closed = true;
                });

                layPanel.ContextMenuCustomizations.AddRange(_layoutGroup.Items[0].ContextMenuCustomizations);
                layPanel.AllowDrop = true;
                layPanel.Drop += (o, e) =>
                {
                    if (e.Data.GetDataPresent(System.Windows.DataFormats.StringFormat))
                    {
                        if (e.Data.GetData(System.Windows.DataFormats.StringFormat) is string fileData)
                        {
                            string filePath = Path.Combine("D:\\Datas", fileData);

                            if (File.Exists(filePath))
                            {
                                // 读取文件的所有行并存储到数组中
                                var lines = File.ReadAllLines(filePath);
                                var data = new string[lines.Length][];
                                var yData = new float[lines.Length];

                                for (int i = 0; i < lines.Length; i++)
                                {
                                    data[i] = lines[i]
                                        .Split(['-'], 2, StringSplitOptions.RemoveEmptyEntries);
                                    yData[i] = Convert.ToSingle(data[i][1]);
                                }

                                chart.BeginUpdate();

                                SampleDataSeries series = new(
                                    chart.ViewXY,
                                    chart.ViewXY.XAxes[0],
                                    chart.ViewXY.YAxes[0]
                                )
                                {
                                    Title = new SeriesTitle() { Text = fileData },
                                    LineStyle =
                                    {
                                        Color = ChartTools.CalcGradient(GenerateUniqueColor(),Colors.White,50)
                                    },
                                    SampleFormat = SampleFormat.SingleFloat,
                                };

                                series.MouseOverOn += (sender, args) =>
                                {
                                    Sample = series;
                                };

                                series.AddSamples(yData, false);
                                chart.ViewXY.SampleDataSeries.Add(series);

                                for (int i = 0; i < chart.ViewXY.LineSeriesCursors.Count; i++)
                                {
                                    var ann = CreateAnnotation(chart);
                                    chart.ViewXY.Annotations.Add(ann);
                                }

                                chart.EndUpdate();

                                UpdateCursorResult(chart);
                            }
                        }
                    }
                };

                canvas.PreviewMouseDown += (o, e) =>
                {
                    var hitTest = VisualTreeHelper.HitTest(canvas, e.GetPosition(canvas));

                    if (hitTest == null || hitTest.VisualHit == canvas)
                    {
                        _text.ClearFocus();
                    }
                };
                canvas.SizeChanged += (o, e) =>
                {
                    chart.Width = canvas.ActualWidth;
                    chart.Height = canvas.ActualHeight;
                };
                canvas.PreviewMouseRightButtonDown += (o, e) =>
                {
                    ShowMenu(canvas);
                };

                canvas.Children.Add(chart);
                layPanel.Content = canvas;
                _layoutGroup.Items.Add(layPanel);
            }
        }



        #endregion Main
    }
}