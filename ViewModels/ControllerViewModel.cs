using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using Arction.Wpf.Charting;
using Arction.Wpf.Charting.Annotations;
using Arction.Wpf.Charting.Axes;
using Arction.Wpf.Charting.SeriesXY;
using Arction.Wpf.Charting.Titles;
using Arction.Wpf.Charting.Views.ViewXY;
using CotrollerDemo.Models;
using DevExpress.Xpf.Bars;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Docking;
using Prism.Commands;
using Prism.Mvvm;
using Application = System.Windows.Application;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ResizableTextBox = CotrollerDemo.Views.ResizableTextBox;
using TextEdit = DevExpress.Xpf.Editors.TextEdit;

namespace CotrollerDemo.ViewModels
{
    public class ControllerViewModel : BindableBase, IDisposable
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

        private bool _canConnect;

        public bool CanConnect
        {
            get => _canConnect;
            set => SetProperty(ref _canConnect, value);
        }

        private bool _canDisConnect;

        public bool CanDisConnect
        {
            get => _canDisConnect;
            set => SetProperty(ref _canDisConnect, value);
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

        public readonly ConcurrentDictionary<int, List<float>> DataBuffer = new();

        public readonly ConcurrentQueue<float[][]> FileData = new();

        public SampleDataSeries Sample { get; set; } = new();
        public LineSeriesCursor Cursor { get; set; }

        public CancellationTokenSource Source = new();

        public Point MousePos;

        public ObservableCollection<IControllerAction> Res;

        public readonly int Points = 10240;

        private readonly object _updateLock = new();
        private readonly Dictionary<int, Queue<float[]>> _channelBuffers = new();
        //private const int BufferThreshold = 2048; // 缓冲区阈值

        private FileSystemWatcher _fileWatcher;

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
        /// 添加图表
        /// </summary>
        public DelegateCommand<object> AddChartCommand { get; set; }

        #endregion Command

        #region Main

        public ControllerViewModel()
        {
            // 初始化时不要立即加载所有文件，可以延迟加载或限制数量
            FileNames = new ObservableCollection<string>();

            Devices = [];
            UpdateDeviceList();
            GlobalValues.TcpClient.StartTcpListen();

            // 初始化文件监视器
            InitializeFileWatcher();

            // 可选择性加载文件，或者使用"加载更多"按钮
            Task.Run(() => GetFolderFiles());
            var chart = CreateChart();
            Charts.Add(chart);

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
            ConnectCommand = new DelegateCommand<object>(ConnectDevice);
            DisconnectCommand = new DelegateCommand<object>(DisconnectDevice);
            DeleteFileCommand = new DelegateCommand<object>(DeleteFile);
            ShowMenuCommand = new DelegateCommand<object>(ShowMenu);
            AddChartCommand = new DelegateCommand<object>(AddChart);
        }

        /// <summary>
        /// 创建图表
        /// </summary>
        private LightningChart CreateChart()
        {
            var chart = new LightningChart();

            chart.PreviewMouseRightButtonDown += (_, e) => e.Handled = true;

            chart.BeginUpdate();

            chart.AllowDrop = true;
            chart.ViewXY.ZoomPanOptions.WheelZooming = WheelZooming.Off;
            chart.Title.Visible = false;
            chart.Title.Text = $"chart{_chartCount}";
            _chartCount++;
            var lineBaseColor = GenerateUniqueColor();

            var view = chart.ViewXY;

            view.Margins = new Thickness(40, 30, 10, 30);
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
            view.XAxes[0].SetRange(0, Points);
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

                series.MouseOverOn += (_, _) =>
                {
                    Sample = series;
                };

                SineWaves.Add([]);

                view.SampleDataSeries.Add(series);
            }

            chart.ViewXY.ZoomToFit();
            chart.AfterRendering += Chart_AfterRendering;
            chart.SizeChanged += Chart_SizeChanged;
            chart.EndUpdate();
            CreateLineSeriesCursor(chart);

            return chart;
        }

        /// <summary>
        /// 移动到图例栏中的曲线标题时获取当前的曲线
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControllerViewModel_SeriesTitleMouseMoveOverOn(
            object sender,
            SeriesTitleDeviceMovedEventArgs e
        )
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
                var linkIp = Devices.First(d => Equals(d.IpAddress, selectItem.IpAddress));

                GlobalValues.UdpClient.IsConnectDevice(linkIp.IpAddress, true);
                Devices = GlobalValues.Devices;
                CanConnect = false;
                CanDisConnect = true;
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
                var linkIp = Devices.First(d => Equals(d.IpAddress, selectItem.IpAddress));

                GlobalValues.UdpClient.IsConnectDevice(linkIp.IpAddress, false);
                Devices = GlobalValues.Devices;
                CanConnect = true;
                CanDisConnect = false;
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

                // 初始化通道缓冲区
                lock (_updateLock)
                {
                    for (int i = 0; i < _seriesCount; i++)
                    {
                        if (!_channelBuffers.ContainsKey(i))
                        {
                            _channelBuffers[i] = new Queue<float[]>();
                        }
                    }
                }

                // 使用TryRead读取数据
                while (GlobalValues.TcpClient.ChannelReader.TryRead(out var data))
                {
                    if (data == null || data.Data == null || data.Data.Length == 0)
                        continue;

                    lock (_updateLock)
                    {
                        if (data.ChannelId >= 0 && data.ChannelId < _seriesCount)
                        {
                            // 将数据分段存入对应通道的缓冲区
                            _channelBuffers[data.ChannelId].Enqueue(data.Data);
                            Interlocked.Add(ref PointNumbers[data.ChannelId], data.Data.Length);

                            // 检查是否所有通道都有足够的数据
                            if (CheckAllChannelsReady())
                            {
                                ProcessAndUpdateCharts();
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"无效的通道ID: {data.ChannelId}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateSeriesData error: {ex.Message}");
            }
        }

        private bool CheckAllChannelsReady()
        {
            return _channelBuffers.All(buffer => buffer.Value.Sum(arr => arr.Length) >= Points);
        }

        private void ProcessAndUpdateCharts()
        {
            try
            {
                var channelData = new float[_seriesCount][];

                // 为每个通道准备数据
                for (int i = 0; i < _seriesCount; i++)
                {
                    var buffer = new List<float>();
                    var queue = _channelBuffers[i];

                    // 从队列中取出数据直到达到所需点数
                    while (queue.Count > 0 && buffer.Count < Points)
                    {
                        var data = queue.Peek();
                        if (buffer.Count + data.Length <= Points)
                        {
                            buffer.AddRange(queue.Dequeue());
                        }
                        else
                        {
                            // 只取需要的部分
                            var remaining = Points - buffer.Count;
                            buffer.AddRange(data.Take(remaining));
                            var newArray = data.Skip(remaining).ToArray();
                            queue.Dequeue();
                            if (newArray.Length > 0)
                            {
                                queue.Enqueue(newArray);
                            }
                        }
                    }

                    channelData[i] = buffer.ToArray();
                    Interlocked.Add(ref PointNumbers[i], -Points);
                }

                // 更新图表
                Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var chart in Charts)
                    {
                        try
                        {
                            chart.BeginUpdate();
                            for (int i = 0; i < _seriesCount; i++)
                            {
                                var series = chart.ViewXY.SampleDataSeries[i];
                                if (series != null && channelData[i] != null)
                                {
                                    series.SamplesSingle = channelData[i];
                                }
                            }
                            chart.EndUpdate();

                            // 更新光标
                            if (Interlocked.Increment(ref CursorUpdateCounter) % 10 == 0)
                            {
                                UpdateCursorResult(chart);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Chart update error: {ex.Message}");
                        }
                    }
                });

                // 将数据加入文件保存队列
                FileData.Enqueue(channelData);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProcessAndUpdateCharts error: {ex.Message}");
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
        /// 计算注释宽度
        /// </summary>
        /// <param name="text">注释文本</param>
        /// <param name="fontSize">字体大小</param>
        /// <returns>适合文本的宽度（基于经验的计算）</returns>
        private static double CalculateAnnotationWidth(string text, double fontSize = 14)
        {
            // 基于字体大小和文本长度计算宽度 每个字符平均宽度假设为字体大小的0.7倍 添加额外的边距以确保文本完全显示
            double width = text.Length * fontSize * 0.7 + fontSize * 2;
            return Math.Min(width, 220);
        }

        public Dictionary<object, double> CursorlDict { get; set; } = [];
        public Dictionary<object, double> TempCursorlDict { get; set; } = [];

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

                // 获取图表可见区域的范围
                var visibleRect = chart.ViewXY.GetMarginsRect();
                chart.ViewXY.XAxes[0].ValueToCoord(chart.ViewXY.XAxes[0].Maximum);

                var targetYCoord = (float)visibleRect.Bottom - 20;
                chart.ViewXY.YAxes[0].CoordToValue(targetYCoord, out var y);

                // 更新每条曲线的注释
                var cursors = chart.ViewXY.LineSeriesCursors;

                var series = chart.ViewXY.SampleDataSeries;

                // 获取chart控件宽度
                double chartWidth = chart.ActualWidth > 0 ? chart.ActualWidth : 800; // 兜底

                var xAxis = chart.ViewXY.XAxes[0];
                double xAxisMin = xAxis.Minimum;
                double xAxisMax = xAxis.Maximum;
                double xAxisVisible = xAxisMax - xAxisMin;
                double xPerPixel = chartWidth > 0 ? xAxisVisible / chartWidth : 0;

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
                        if (
                            SolveValueAccurate(t, cursors[i].ValueAtXAxis, out double seriesYValue)
                            && !string.IsNullOrEmpty(title)
                            && title.Length > 0
                        )
                        {
                            // 保存注释信息
                            validAnnotations.Add(
                                (
                                    seriesNumber + cursorIndex,
                                    seriesYValue,
                                    $"{title}: {Math.Round(seriesYValue, 2)}",
                                    title
                                )
                            );

                            var cursor = CursorlDict.First(tuple => tuple.Key == cursors[i].Tag);
                            if (!cursors[i].ValueAtXAxis.Equals(cursor.Value) || TempCursorlDict.Values == CursorlDict.Values)
                            {
                                // 更新曲线标题
                                t.Title.Text = $"{title}: {Math.Round(seriesYValue, 2)}";
                            }
                        }
                        else
                        {
                            // 如果无法解析Y值，确保注释是隐藏的
                            if (seriesNumber + cursorIndex < cursorValues.Count)
                            {
                                if (
                                    cursorValues[seriesNumber + cursorIndex]
                                        .Text.Split([':'], 2)[0]
                                        .Length > 4
                                )
                                {
                                    cursorValues[seriesNumber + cursorIndex] = CreateAnnotation(
                                        chart
                                    );
                                }
                            }
                        }

                        seriesNumber++;
                    }

                    CursorlDict[cursors[i].Tag] = cursors[i].ValueAtXAxis;
                    // 第二步：按Y值排序注释
                    validAnnotations.Sort((a, b) => a.YValue.CompareTo(b.YValue));

                    // 第三步：分配注释位置，避免重叠
                    const double minYSpacing = 0.6; // 注释之间的最小Y轴间距

                    // 检查光标是否靠近右边界
                    double cursorX = cursors[i].ValueAtXAxis;

                    // 判断光标是否在图表右侧区域（例如：最后20%区域）
                    bool isNearRightEdge = (cursorX > (xAxisMax - xAxisVisible * 0.1));

                    // 注释与光标的距离为固定10像素
                    double annotationOffsetPx = 10;

                    // 应用排序后的位置
                    for (int j = 0; j < validAnnotations.Count; j++)
                    {
                        var annotation = cursorValues[validAnnotations[j].Index];
                        var originalY = validAnnotations[j].YValue;
                        string annotationText =
                            $"{validAnnotations[j].Title}: {Math.Round(validAnnotations[j].YValue, 2)}";

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

                        // 根据文本计算所需的宽度
                        double requiredWidthPx = CalculateAnnotationWidth(annotationText);

                        // 根据光标位置调整注释位置（左侧或右侧）

                        double requiredWidth = requiredWidthPx * xPerPixel;
                        double annotationOffset = annotationOffsetPx * xPerPixel;
                        if (isNearRightEdge)
                        {
                            annotation.AxisValuesBoundaries.XMax = cursorX - 2 * xPerPixel;
                            annotation.AxisValuesBoundaries.XMin =
                                annotation.AxisValuesBoundaries.XMax - requiredWidth;
                        }
                        else
                        {
                            annotation.AxisValuesBoundaries.XMin = cursorX + annotationOffset;
                            annotation.AxisValuesBoundaries.XMax =
                                annotation.AxisValuesBoundaries.XMin + requiredWidth;
                        }

                        annotation.AxisValuesBoundaries.YMax = adjustedY + 0.25;
                        annotation.AxisValuesBoundaries.YMin = adjustedY - 0.25;

                        // 设置注释内容
                        annotation.Text = annotationText;
                        annotation.Visible = true;
                    }

                    // 设置X轴注释位置和内容
                    string xAnnotationText = $"X: {Math.Round(cursors[i].ValueAtXAxis, 2)}";
                    cursorValues[cursorIndex].Text = xAnnotationText;

                    // 计算所需的宽度
                    double xRequiredWidthPx = CalculateAnnotationWidth(xAnnotationText);

                    // 同样根据光标位置调整X轴注释

                    double xRequiredWidth = xRequiredWidthPx * xPerPixel;
                    double perPixelLocal = annotationOffsetPx * xPerPixel;
                    if (isNearRightEdge)
                    {
                        cursorValues[cursorIndex].AxisValuesBoundaries.XMax =
                            cursorX - 2 * xPerPixel;
                        cursorValues[cursorIndex].AxisValuesBoundaries.XMin =
                            cursorValues[cursorIndex].AxisValuesBoundaries.XMax - xRequiredWidth;
                    }
                    else
                    {
                        cursorValues[cursorIndex].AxisValuesBoundaries.XMin =
                            cursorX + perPixelLocal;
                        cursorValues[cursorIndex].AxisValuesBoundaries.XMax =
                            cursorValues[cursorIndex].AxisValuesBoundaries.XMin + xRequiredWidth;
                    }

                    cursorValues[cursorIndex].AxisValuesBoundaries.YMax = y + 0.5;
                    cursorValues[cursorIndex].AxisValuesBoundaries.YMin = y;
                    cursorValues[cursorIndex].Visible = true;
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
            //lock (_dataBuffer)
            //{
            //    foreach (var key in _dataBuffer.Keys.ToList())
            //    {
            //        if (_dataBuffer.TryRemove(key, out var buffer))
            //        {
            //            buffer.Clear();
            //        }
            //    }
            //}

            //// 重置点数计数
            //for (int i = 0; i < PointNumbers.Length; i++)
            //{
            //    PointNumbers[i] = 0;
            //}

            // 重置光标更新计数器
            CursorUpdateCounter = 0;
            // 清理文件数据队列
            while (FileData.TryDequeue(out _)) { }
        }

        /// <summary>
        /// 保存折线数据
        /// </summary>
        private void SaveData()
        {
            Task.Run(async () =>
            {
                try
                {
                    while (IsRunning)
                    {
                        if (FileData.TryDequeue(out var data))
                        {
                            if (data == null || data.Length != _seriesCount)
                            {
                                continue;
                            }

                            string timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");

                            // 使用一个文件保存所有通道数据
                            string fullPath = Path.Combine(FolderPath, $"{timestamp}.txt");

                            await using var fs = new FileStream(
                                fullPath,
                                FileMode.Create,
                                FileAccess.Write,
                                FileShare.ReadWrite,
                                8192,
                                FileOptions.Asynchronous
                            );

                            await using var sw = new StreamWriter(fs);

                            // 逐通道写入，减少内存使用
                            for (int channelId = 0; channelId < _seriesCount; channelId++)
                            {
                                if (data[channelId] == null || data[channelId].Length == 0)
                                    continue;

                                await sw.WriteLineAsync($"CH{channelId + 1}:");

                                // 分批写入数据，避免一次性构建大字符串
                                const int batchSize = 1000;
                                for (int i = 0; i < data[channelId].Length; i += batchSize)
                                {
                                    var batch = new StringBuilder();
                                    int end = Math.Min(i + batchSize, data[channelId].Length);

                                    for (int j = i; j < end; j++)
                                    {
                                        batch.AppendLine($"{j}-{Math.Round(data[channelId][j], 5)}");
                                    }

                                    await sw.WriteAsync(batch.ToString());
                                }

                                await sw.WriteLineAsync(); // 通道间空行
                            }
                        }
                        else
                        {
                            await Task.Delay(50);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"保存数据时出错: {ex.Message}");
                }
            });
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

                ContextMenu menu = new();

                var deleteItem = new MenuItem() { Header = "删除曲线" };

                if (chart.ViewXY.SampleDataSeries.Count > 8)
                {
                    // 为每条曲线创建子菜单项
                    for (int i = 0; i < chart.ViewXY.SampleDataSeries.Count - 8; i++)
                    {
                        int seriesIndex = i + 8;
                        var series = chart.ViewXY.SampleDataSeries[seriesIndex];
                        string seriesTitle = series.Title?.Text;

                        var seriesItem = new MenuItem()
                        {
                            Header = seriesTitle,
                            Command = new DelegateCommand(() =>
                            {
                                DeleteSample(chart, seriesIndex);
                            }),
                        };

                        deleteItem.Items.Add(seriesItem);
                    }
                }
                else
                {
                    deleteItem.Items.Add(
                        new MenuItem() { Header = "没有可用的曲线", IsEnabled = false }
                    );
                }

                var updateTitleItem = new MenuItem() { Header = "修改标题" };

                // 为每条曲线创建子菜单项
                foreach (
                    var seriesItem in from series in chart.ViewXY.SampleDataSeries
                                      let series1 = series
                                      select new MenuItem()
                                      {
                                          Header = series.Title?.Text,
                                          Command = new DelegateCommand(() =>
                                          {
                                              UpdateTitle(series1);
                                          }),
                                      }
                )
                {
                    updateTitleItem.Items.Add(seriesItem);
                }

                menu.Items.Add(deleteItem);
                menu.Items.Add(updateTitleItem);

                if (chart.ViewXY.SampleDataSeries.Count > _seriesCount)
                {
                    menu.Items.Add(
                        new MenuItem()
                        {
                            Header = "删除添加曲线",
                            Command = new DelegateCommand<LightningChart>(DeleteAllAddSeries),
                            CommandParameter = chart,
                        }
                    );
                }

                menu.Items.Add(
                    new MenuItem()
                    {
                        Header = "切换图例",
                        Command = new DelegateCommand(() =>
                        {
                            chart.ViewXY.LegendBoxes[0].Visible = !chart
                                .ViewXY
                                .LegendBoxes[0]
                                .Visible;
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

                var deleteMenuItem = new MenuItem() { Header = "删除光标" };

                for (int i = 0; i < chart.ViewXY.LineSeriesCursors.Count; i++)
                {
                    int cursorIndex = i;
                    var cursor = chart.ViewXY.LineSeriesCursors[i];

                    // 为每个光标创建子菜单项
                    var cursorItem = new MenuItem()
                    {
                        Header =
                            $"{cursor.Tag} (X: {Math.Round(cursor.ValueAtXAxis, 2)})",
                        Command = new DelegateCommand(() =>
                        {
                            DeleteSeriesCursor(chart, cursorIndex);
                        }),
                    };

                    deleteMenuItem.Items.Add(cursorItem);
                }

                // 如果没有光标，添加一个禁用的菜单项
                if (chart.ViewXY.LineSeriesCursors.Count == 0)
                {
                    deleteMenuItem.Items.Add(
                        new MenuItem() { Header = "没有可用的光标", IsEnabled = false }
                    );
                }

                menu.Items.Add(deleteMenuItem);

                chart.ContextMenu = menu;
                // 初始化图表事件处理
                chart.MouseRightButtonDown += (_, e) => e.Handled = true;
                chart.EndUpdate();
            }
        }

        /// <summary>
        /// 删除曲线
        /// </summary>
        /// <param name="chart"></param>
        /// <param name="seriesIndex"></param>
        private void DeleteSample(LightningChart chart, int seriesIndex)
        {
            if (
                chart == null
                || seriesIndex < 0
                || seriesIndex >= chart.ViewXY.SampleDataSeries.Count
            )
                return;

            chart.BeginUpdate();

            // 获取需要删除的曲线
            var series = chart.ViewXY.SampleDataSeries[seriesIndex];

            // 删除与该曲线相关的所有注释 每个光标都有一组注释(X轴注释+每条曲线的注释)
            for (
                int cursorIndex = 0;
                cursorIndex < chart.ViewXY.LineSeriesCursors.Count;
                cursorIndex++
            )
            {
                // 计算当前光标的注释起始索引
                int annotationBaseIndex = cursorIndex * (chart.ViewXY.SampleDataSeries.Count + 1);

                // 计算该曲线对应注释的索引
                // +1是因为首个注释是X轴注释
                int annotationIndex = annotationBaseIndex + seriesIndex + 1;

                // 确保索引在有效范围内
                if (annotationIndex < chart.ViewXY.Annotations.Count)
                {
                    // 获取要删除的注释
                    var annotation = chart.ViewXY.Annotations[annotationIndex];

                    // 从集合中移除注释
                    chart.ViewXY.Annotations.RemoveAt(annotationIndex);

                    // 释放注释资源
                    annotation.Dispose();
                }
            }

            // 从集合中移除曲线
            chart.ViewXY.SampleDataSeries.RemoveAt(seriesIndex);

            // 释放曲线资源
            series.Dispose();

            // 更新相关注释
            UpdateCursorResult(chart);

            chart.EndUpdate();
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
            cursor.TrackPoint.Shape = Shape.Circle;
            cursor.Tag = $"光标{chart.ViewXY.LineSeriesCursors.Count + 1}";
            cursor.PositionChanged += Cursor_PositionChanged;
            chart.ViewXY.LineSeriesCursors.Add(cursor);

            CursorlDict.Add(cursor.Tag, cursor.ValueAtXAxis);
            TempCursorlDict = CursorlDict;

            // 创建注释
            for (int i = 0; i < chart.ViewXY.SampleDataSeries.Count + 1; i++)
            {
                chart.ViewXY.Annotations.Add(CreateAnnotation(chart));
            }

            chart.EndUpdate();
            // 直接更新光标结果，不使用异步调用
            UpdateCursorResult(chart);
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
            annot.Fill.Bitmap.ImageTintColor = Colors.Transparent;
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
        /// <param name="sample"></param>
        public void UpdateTitle(SampleDataSeries sample)
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
                if (textEdit.EditValue != null && (string)textEdit.EditValue != string.Empty)
                {
                    sample.Title.Text = textEdit.Text;
                    UpdateCursorResult(sample.OwnerView.OwnerChart);
                }
            }
        }

        /// <summary>
        /// 删除图表中所有添加的曲线
        /// </summary>
        /// <param name="chart">要操作的图表</param>
        public void DeleteAllAddSeries(LightningChart chart)
        {
            if (chart == null)
                return;

            try
            {
                // 阻止正在进行的渲染
                chart.BeginUpdate();
                
                // 存储需要删除的注释
                var annotationsToRemove = new List<AnnotationXY>();
                
                // 收集需要删除的序列
                var seriesToRemove = new List<SampleDataSeries>();
                var seriesIndices = new List<int>();
                
                // 只收集索引 8 及之后的曲线（添加的曲线）
                for (int i = 8; i < chart.ViewXY.SampleDataSeries.Count; i++)
                {
                    seriesToRemove.Add(chart.ViewXY.SampleDataSeries[i]);
                    seriesIndices.Add(i);
                }
                
                // 如果没有需要删除的曲线，直接返回
                if (seriesToRemove.Count == 0)
                {
                    chart.EndUpdate();
                    return;
                }
                
                // 处理光标相关的注释
                // 每个光标有 (系列数+1) 个注释，第一个是X轴注释，其余是每个系列的注释
                for (int cursorIndex = 0; cursorIndex < chart.ViewXY.LineSeriesCursors.Count; cursorIndex++)
                {
                    // 计算当前光标注释的基础索引
                    int annotationBase = cursorIndex * (chart.ViewXY.SampleDataSeries.Count + 1);
                    
                    // 计算需要删除注释的索引
                    foreach (int seriesIndex in seriesIndices)
                    {
                        // 计算注释索引：基础索引 + 1(X轴注释) + 系列索引
                        int annotationIndex = annotationBase + 1 + seriesIndex;
                        
                        // 确保索引有效
                        if (annotationIndex < chart.ViewXY.Annotations.Count)
                        {
                            annotationsToRemove.Add(chart.ViewXY.Annotations[annotationIndex]);
                        }
                    }
                }
                
                // 安全地移除注释
                foreach (var annotation in annotationsToRemove)
                {
                    chart.ViewXY.Annotations.Remove(annotation);
                    annotation.Dispose();
                }
                
                // 分别进行曲线删除和资源释放
                foreach (var series in seriesToRemove)
                {
                    // 先从集合中移除
                    chart.ViewXY.SampleDataSeries.Remove(series);
                    // 然后安全地释放资源
                    series.Dispose();
                }
                
                // 更新图表
                chart.EndUpdate();
                
                // 更新光标结果
                DXMessageBox.Show("已删除所有添加的曲线", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // 主线程中更新光标结果，避免UI冻结
                Application.Current.Dispatcher.InvokeAsync(() => 
                {
                    UpdateCursorResult(chart);
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                chart.EndUpdate();
                Debug.WriteLine($"删除曲线时出错: {ex.Message}");
                DXMessageBox.Show($"删除曲线时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 删除选中的光标
        /// </summary>
        /// <param name="chart"></param>
        /// <param name="cursorIndex"></param>
        private void DeleteSeriesCursor(LightningChart chart, int cursorIndex)
        {
            if (
                chart == null
                || cursorIndex < 0
                || cursorIndex >= chart.ViewXY.LineSeriesCursors.Count
            )
                return;

            chart.BeginUpdate();

            // 获取需要删除的光标
            var cursor = chart.ViewXY.LineSeriesCursors[cursorIndex];

            // 删除该光标对应的注释（如果有）
            var series = chart.ViewXY.SampleDataSeries;
            int annotationStartIndex = cursorIndex * (series.Count + 1);

            // 删除与此光标关联的所有注释（X轴注释和每个系列注释）
            int annotationsToRemove = series.Count + 1;
            for (int i = 0; i < annotationsToRemove; i++)
            {
                // 确保我们不会越界
                if (annotationStartIndex < chart.ViewXY.Annotations.Count)
                {
                    chart.ViewXY.Annotations.RemoveAt(annotationStartIndex);
                }
            }

            // 断开光标事件连接
            cursor.PositionChanged -= Cursor_PositionChanged;

            // 从集合中移除光标
            chart.ViewXY.LineSeriesCursors.Remove(cursor);

            // 释放光标资源
            cursor.Dispose();

            chart.EndUpdate();

            // 更新其他光标的显示结果
            UpdateCursorResult(chart);
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

                // 使用分页加载的方式获取文件，避免一次性加载所有文件
                Task.Run(async () =>
                {
                    // 限制文件数量，只加载最新的1000个文件
                    const int maxFileCount = 1000;

                    // 获取所有文件并按修改时间排序，只取最新的maxFileCount个
                    var fileInfos = new DirectoryInfo(FolderPath)
                        .GetFiles("*.txt")
                        .OrderByDescending(f => f.LastWriteTime)
                        .Take(maxFileCount)
                        .Select(f => f.Name)
                        .ToList();

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        // 清空现有集合，使用新集合替代
                        FileNames.Clear();

                        if (fileInfos.Count > 0)
                        {
                            foreach (var file in fileInfos)
                            {
                                FileNames.Add(file);
                            }
                        }

                        // 执行垃圾回收，释放内存
                        GC.Collect();
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
            //CleanupResources();

            await GlobalValues.TcpClient.SendDataClient(1);

            IsRunning = true; // 更新运行状态

            // 启动数据保存任务

            SaveData();

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

            if (sender is LineSeriesCursor cursor)
            {
                var chart = cursor.OwnerView.OwnerChart;

                if (cursor.ValueAtXAxis < chart.ViewXY.XAxes[0].Minimum)
                {
                    cursor.ValueAtXAxis = chart.ViewXY.XAxes[0].Minimum;
                }

                if (cursor.ValueAtXAxis > chart.ViewXY.XAxes[0].Maximum)
                {
                    cursor.ValueAtXAxis = chart.ViewXY.XAxes[0].Maximum;
                }

                var cur = TempCursorlDict.First(tuple => tuple.Key == cursor.Tag);
                if (!cursor.ValueAtXAxis.Equals(cur.Value))
                {
                    TempCursorlDict[cursor.Tag] = cursor.ValueAtXAxis;
                }

                Task.Run(
                    async () =>
                        await Application.Current.Dispatcher.InvokeAsync(
                            () =>
                            {
                                UpdateCursorResult(chart);
                            },
                            DispatcherPriority.Background
                        )
                );
            }
        }

        /// <summary>
        /// 根据X值解决Y值
        /// </summary>
        /// <param name="series"></param>
        /// <param name="xValue"></param>
        /// <param name="yValue"></param>
        /// <returns></returns>
        private static bool SolveValueAccurate(
            SampleDataSeries series,
            double xValue,
            out double yValue
        )
        {
            yValue = 0;

            var result = series.SolveYValueAtXValue(xValue);

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
                var item = list[lastInd]; // take item ref from list.
                list.RemoveAt(lastInd); // remove item first
                if (item != null)
                {
                    item.Dispose(); // then dispose it.
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
        private void ClearFolder()
        {
            try
            {
                if ((DialogResult)DXMessageBox.Show("是否清空文件夹?", "提示", MessageBoxButton.YesNo) == DialogResult.Yes)
                {
                    // 删除文件夹中所有的文件
                    FileOperation.Delete(FolderPath);

                    // 清空文件名集合
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        FileNames.Clear();

                        // 强制垃圾回收
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    });

                    DXMessageBox.Show("文件夹已清空");
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
                        if (DataBuffer.TryGetValue(i, out var buffer) && buffer.Count >= 1024)
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

                layPanel.ContextMenuCustomizations.AddRange(Res);
                layPanel.AllowDrop = true;
                layPanel.Drop += (_, e) =>
                {
                    if (e.Data.GetDataPresent(System.Windows.DataFormats.StringFormat))
                    {
                        if (
                            e.Data.GetData(System.Windows.DataFormats.StringFormat)
                            is string fileData
                        )
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
                                        Color = ChartTools.CalcGradient(
                                            GenerateUniqueColor(),
                                            Colors.White,
                                            50
                                        ),
                                    },
                                    SampleFormat = SampleFormat.SingleFloat,
                                };

                                series.MouseOverOn += (_, _) =>
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

                canvas.PreviewMouseDown += (_, e) =>
                {
                    var hitTest = VisualTreeHelper.HitTest(canvas, e.GetPosition(canvas));

                    if (hitTest == null || hitTest.VisualHit == canvas)
                    {
                        _text.ClearFocus();
                    }
                };
                canvas.SizeChanged += (_, _) =>
                {
                    chart.Width = canvas.ActualWidth;
                    chart.Height = canvas.ActualHeight;
                };
                canvas.PreviewMouseRightButtonDown += (_, _) =>
                {
                    ShowMenu(canvas);
                };

                canvas.Children.Add(chart);
                layPanel.Content = canvas;
                _layoutGroup.Items.Add(layPanel);
            }
        }

        private void InitializeFileWatcher()
        {
            // 关闭之前的监视器
            _fileWatcher?.Dispose();

            _fileWatcher = new FileSystemWatcher(FolderPath)
            {
                Filter = "*.txt",
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
            };

            // 只有在新文件创建时才添加到列表
            _fileWatcher.Created += (_, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (FileNames.Count >= 1000)
                    {
                        FileNames.RemoveAt(FileNames.Count - 1); // 移除最旧的一项
                    }
                    FileNames.Insert(0, Path.GetFileName(e.FullPath)); // 添加到最前面
                });
            };

            // 文件删除时从列表移除
            _fileWatcher.Deleted += (_, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var fileName = Path.GetFileName(e.FullPath);
                    if (FileNames.Contains(fileName))
                    {
                        FileNames.Remove(fileName);
                    }
                });
            };
        }

        #endregion Main

        public void Dispose()
        {
            // 取消所有任务
            Source.Cancel();
            Source.Dispose();

            // 清理通道缓冲区
            lock (_updateLock)
            {
                foreach (var buffer in _channelBuffers.Values)
                {
                    buffer.Clear();
                }
                _channelBuffers.Clear();
            }

            // 清空文件名集合
            FileNames.Clear();

            // 清空通道数据
            while (FileData.TryDequeue(out _)) { }

            // 释放图表资源
            foreach (var chart in Charts)
            {
                chart.Dispose();
            }
            Charts.Clear();

            // 释放文件监视器
            _fileWatcher?.Dispose();

            // 强制垃圾回收
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}