﻿using System;
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
using System.Windows.Input;
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

namespace CotrollerDemo.ViewModels;

public class ControllerViewModel : BindableBase, IDisposable
{
    #region Property

    private ObservableCollection<string> _fileNames = [];

    /// <summary>
    ///     文件名集合
    /// </summary>
    public ObservableCollection<string> FileNames
    {
        get => _fileNames;
        set => SetProperty(ref _fileNames, value);
    }

    private ObservableCollection<DeviceInfoModel> _devices;

    /// <summary>
    ///     设备列表
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
    ///     曲线数量
    /// </summary>
    private readonly int _seriesCount = 8;

    /// <summary>
    ///     存放已生成的颜色
    /// </summary>
    private static readonly HashSet<Color> GeneratedColors = [];

    // 存放路径
    public string FolderPath;

    private bool _isRunning;

    /// <summary>
    ///     是否正在运行
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
    ///     是否可拖拽
    /// </summary>
    public bool IsDrop
    {
        get => _isDrop;
        set => SetProperty(ref _isDrop, value);
    }

    /// <summary>
    ///     正弦波数据
    /// </summary>
    public List<List<float>> SineWaves { get; set; } = [];

    /// <summary>
    ///     曲线点数数量
    /// </summary>
    public int[] PointNumbers = new int[8];

    private readonly ResizableTextBox _text = new();

    public readonly ConcurrentDictionary<int, List<float>> DataBuffer = new();

    public readonly ConcurrentQueue<float[][]> FileData = new();

    public SampleDataSeries Sample { get; set; } = new();

    public CancellationTokenSource Source = new();

    public Point MousePos;

    public ObservableCollection<IControllerAction> Res;

    public readonly int Points = 2048;

    private readonly object _updateLock = new();
    private readonly Dictionary<int, Queue<float[]>> _channelBuffers = new();

    //private const int BufferThreshold = 2048; // 缓冲区阈值

    private FileSystemWatcher _fileWatcher;

    #endregion Property

    #region Command

    /// <summary>
    ///     开始试验
    /// </summary>
    public AsyncDelegateCommand StartTestCommand { get; set; }

    /// <summary>
    ///     停止试验
    /// </summary>
    public AsyncDelegateCommand StopTestCommand { get; set; }

    /// <summary>
    ///     查询设备
    /// </summary>
    public DelegateCommand DeviceQueryCommand { get; set; }

    /// <summary>
    ///     打开文件夹
    /// </summary>
    public DelegateCommand OpenFolderCommand { get; set; }

    /// <summary>
    ///     清空文件夹
    /// </summary>
    public DelegateCommand ClearFolderCommand { get; set; }

    /// <summary>
    ///     连接设备
    /// </summary>
    public DelegateCommand<object> ConnectCommand { get; set; }

    /// <summary>
    ///     断开连接
    /// </summary>
    public DelegateCommand<object> DisconnectCommand { get; set; }

    /// <summary>
    ///     删除文件
    /// </summary>
    public DelegateCommand<object> DeleteFileCommand { get; set; }

    /// <summary>
    ///     右键菜单
    /// </summary>
    public DelegateCommand<object> ShowMenuCommand { get; set; }

    /// <summary>
    ///     添加图表
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

        // 设置基本路径后创建数据文件夹 (注意顺序)
        var baseFolder = @"D:\Datas";
        // 确保基本文件夹存在
        if (!Directory.Exists(baseFolder)) Directory.CreateDirectory(baseFolder);

        // 创建以日期时间为名的子文件夹，但不要嵌套在原路径中
        FolderPath = Path.Combine(baseFolder, DateTime.Now.ToString("yyyyMMddHHmmss"));
        Directory.CreateDirectory(FolderPath);

        // 初始化文件监视器 - 在文件夹创建后
        InitializeFileWatcher();

        // 加载文件列表 - 在监视器初始化后
        Task.Run(GetFolderFiles);

        // 创建图表 - 在文件操作设置完成后
        var chart = CreateChart();
        Charts.Add(chart);
            // 创建图表 - 在文件操作设置完成后
            var chart = CreateChart();
            Charts.Add(chart);

        IsRunning = false;

        StartTestCommand = new AsyncDelegateCommand(StartTest, CanStartChart).ObservesProperty(() => IsRunning
        );
        StopTestCommand = new AsyncDelegateCommand(StopTest, CanStopChart).ObservesProperty(() => IsRunning
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
    ///     创建图表
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
        for (var i = 0; i < _seriesCount; i++)
        {
            var series = new SampleDataSeries(view, view.XAxes[0], view.YAxes[0])
            {
                Title = new SeriesTitle { Text = $"CH {i + 1}" },
                //AllowUserInteraction = true,
                LineStyle =
                {
                    Color = ChartTools.CalcGradient(GenerateUniqueColor(), Colors.White, 50)
                },
                SampleFormat = SampleFormat.SingleFloat
            };

            series.MouseOverOn += (_, _) => { Sample = series; };

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
    ///     移动到图例栏中的曲线标题时获取当前的曲线
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
    ///     是否可以停止绘制图表
    /// </summary>
    /// <returns></returns>
    private bool CanStopChart()
    {
        return IsRunning;
    }

    /// <summary>
    ///     是否可以开始绘制图表
    /// </summary>
    /// <returns></returns>
    private bool CanStartChart()
    {
        return !IsRunning;
    }

    /// <summary>
    ///     连接设备
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
    ///     断开设备
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
    ///     更新设备列表
    /// </summary>
    private void UpdateDeviceList()
    {
        GlobalValues.UdpClient.StartUdpListen();
        Devices = GlobalValues.Devices;
    }

    public int CursorUpdateCounter;

    /// <summary>
    ///     更新曲线数据
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
                for (var i = 0; i < _seriesCount; i++)
                    if (!_channelBuffers.ContainsKey(i))
            int processedDataCount = 0;
            const int maxProcessPerFrame = 10000; // 每帧最大处理数据量，防止卡顿

            // 使用TryRead读取数据，限制每帧处理量
            while (processedDataCount < maxProcessPerFrame && GlobalValues.TcpClient.ChannelReader.TryRead(out var data))
            {
                if (data == null || data.Data == null || data.Data.Length == 0)
                    continue;
                while (GlobalValues.TcpClient.ChannelReader.TryRead(out var data))
                {
                    if (data == null || data.Data == null || data.Data.Length == 0)
                        continue;
                        continue;

                lock (_updateLock)
                {
                    if (data.ChannelId >= 0 && data.ChannelId < _seriesCount)
                    {
                        // 检查是否所有通道都有足够的数据进行一次完整更新
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
                                shouldUpdateUi = false;
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
        // 必须确保所有通道都已初始化并且有足够数据
        if (_channelBuffers.Count != _seriesCount)
            return false;
        
        return _channelBuffers.All(buffer => {
            int totalLength = 0;
            foreach (var arr in buffer.Value)
            {
                totalLength += arr.Length;
                if (totalLength >= Points) return true;
            }
            return false;
        });
    }

    private void ProcessAndUpdateCharts()
    {
        try
        {
            var channelData = new float[_seriesCount][];
            var allPointsCount = Points; // 所有通道使用相同的数据点数
            
            // 保存最小可用数据点数，确保所有通道使用相同数量的点
            int minAvailablePoints = allPointsCount;
            
            // 检查所有通道可用的最小点数
            for (var i = 0; i < _seriesCount; i++)
            {
                if (!_channelBuffers.TryGetValue(i, out var queue) || queue.Count == 0)
                {
                    minAvailablePoints = 0;
                    break;
                }
                
                int availablePoints = 0;
                foreach (var chunk in queue)
                {
                    availablePoints += chunk.Length;
                    if (availablePoints >= allPointsCount) break;
                }
                
                minAvailablePoints = Math.Min(minAvailablePoints, availablePoints);
            }
            
            // 如果没有足够的点，直接返回
            if (minAvailablePoints < allPointsCount)
                return;
                
            // 为每个通道准备数据 - 确保所有通道处理相同数量的点
            for (var i = 0; i < _seriesCount; i++)
            // 使用低优先级更新UI以减少卡顿
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // 批量更新所有图表，减少BeginUpdate/EndUpdate调用次数
                    foreach (var chart in Charts)
                        try
                        {
                            chart.BeginUpdate();

                            // 确保所有系列数据同时更新
                            for (var i = 0; i < _seriesCount; i++)
                                if (i < chart.ViewXY.SampleDataSeries.Count && channelData[i] != null && channelData[i].Length == allPointsCount)
                                {
                                    var series = chart.ViewXY.SampleDataSeries[i];
                                    if (series != null) series.SamplesSingle = channelData[i];
                                }

                            chart.EndUpdate();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Chart update error: {ex.Message}");
                        }
                                chart.BeginUpdate();
                    // 降低光标更新频率，减少不必要的UI操作
                    if (Interlocked.Increment(ref CursorUpdateCounter) % 30 == 0) // 从20改为30，进一步降低频率
                    {
                        var chartIndex = CursorUpdateCounter / 30 % Charts.Count;
                        if (chartIndex < Charts.Count) UpdateCursorResult(Charts[chartIndex]);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"UI update error: {ex.Message}");
                }
            }), DispatcherPriority.Background);
                                }

                                chart.EndUpdate();
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Chart update error: {ex.Message}");
                            }
                        }
                                for (int i = 0; i < _seriesCount; i++)
                        // 只有当需要时才更新光标
                        if (Interlocked.Increment(ref CursorUpdateCounter) % 20 == 0) // 降低光标更新频率
                        {
                            // 一次只更新一个图表的光标，轮流更新所有图表
                            int chartIndex = (CursorUpdateCounter / 20) % Charts.Count;
                            if (chartIndex < Charts.Count)
                            {
                                UpdateCursorResult(Charts[chartIndex]);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"UI update error: {ex.Message}");
                    }
                }), DispatcherPriority.Background);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Chart update error: {ex.Message}");
                            }
                        }

                        // 只有当需要时才更新光标
    /// <summary>
    ///     更新光标结果
    /// </summary>
    public void UpdateCursorResult(LightningChart chart)
    {
        try
        {
            if (chart?.ViewXY == null)
                return;

            // 如果没有光标或图表不可见，直接返回
            if (chart.ViewXY.LineSeriesCursors.Count == 0 || !chart.IsVisible)
                return;

            chart.BeginUpdate();

            // 批量处理策略：先收集所有更新，然后一次性应用
            var annotationUpdates = new Dictionary<AnnotationXY, Action<AnnotationXY>>();
            var seriesTitleUpdates = new Dictionary<SampleDataSeries, string>();

            //获取注释
            var cursorValues = chart.ViewXY.Annotations;
            if (cursorValues.Count == 0)
            {
                chart.EndUpdate();
                return;
            }
        /// 更新光标结果
            // 获取图表可见区域的范围
            var visibleRect = chart.ViewXY.GetMarginsRect();
            var targetYCoord = (float)visibleRect.Bottom - 20;
            chart.ViewXY.YAxes[0].CoordToValue(targetYCoord, out var y);
            {
            // 获取chart控件宽度
            var chartWidth = chart.ActualWidth > 0 ? chart.ActualWidth : 800; // 兜底

                // 如果没有光标或图表不可见，直接返回
                if (chart.ViewXY.LineSeriesCursors.Count == 0 || !chart.IsVisible)
                    return;

                chart.BeginUpdate();

            var cursors = chart.ViewXY.LineSeriesCursors;
            var series = chart.ViewXY.SampleDataSeries;

            for (var i = 0; i < cursors.Count; i++)
            {
                var cursorIndex = i * (series.Count + 1);
                if (cursorIndex >= cursorValues.Count)
                    continue;
                    chart.EndUpdate();
                    return;
                }
                // 收集所有有效的Y值和对应的注释索引
                var validAnnotations = new List<(int Index, double YValue, string Text, string Title)>();
                var visibleRect = chart.ViewXY.GetMarginsRect();
                var targetYCoord = (float)visibleRect.Bottom - 20;
                chart.ViewXY.YAxes[0].CoordToValue(targetYCoord, out var y);

                // 获取chart控件宽度
                double chartWidth = chart.ActualWidth > 0 ? chart.ActualWidth : 800; // 兜底
                    return;

                chart.BeginUpdate();

                // 批量处理策略：先收集所有更新，然后一次性应用
                var annotationUpdates = new Dictionary<AnnotationXY, Action<AnnotationXY>>();
                var seriesTitleUpdates = new Dictionary<SampleDataSeries, string>();
                var cursors = chart.ViewXY.LineSeriesCursors;
                var series = chart.ViewXY.SampleDataSeries;

                for (int i = 0; i < cursors.Count; i++)
                {
                    int cursorIndex = i * (series.Count + 1);
                    if (cursorIndex >= cursorValues.Count)
                        continue;
                        // 收集曲线标题更新（不立即更新）
                        seriesTitleUpdates[t] = $"{title}: {Math.Round(seriesYValue, 2)}";
                    }
                    else if (seriesNumber + cursorIndex < cursorValues.Count)
                    {
                        // 如果无法解析Y值，隐藏注释
                        if (cursorValues[seriesNumber + cursorIndex].Visible)
                        {
                            var annotationIndex = seriesNumber + cursorIndex;
                            annotationUpdates[cursorValues[annotationIndex]] = a => a.Visible = false;
                        }
                    }
            var xAxisVisible = xAxisMax - xAxisMin;
            var xPerPixel = chartWidth > 0 ? xAxisVisible / chartWidth : 0;

                var cursors = chart.ViewXY.LineSeriesCursors;
                // 第二步：按Y值排序注释
                validAnnotations.Sort((a, b) => a.YValue.CompareTo(b.YValue));
                for (int i = 0; i < cursors.Count; i++)
                // 第三步：分配注释位置，避免重叠
                const double minYSpacing = 1; // 注释之间的最小Y轴间距
                    if (cursorIndex >= cursorValues.Count)
                        continue;

                var seriesNumber = 1;

                            // 收集曲线标题更新（不立即更新）
                            seriesTitleUpdates[t] = $"{title}: {Math.Round(seriesYValue, 2)}";
                        }
                        else if (seriesNumber + cursorIndex < cursorValues.Count)
                        {
                // 应用排序后的位置
                for (var j = 0; j < validAnnotations.Count; j++)
                {
                    var annotationIndex = validAnnotations[j].Index;
                    if (annotationIndex >= cursorValues.Count)
                        continue;

                    var annotation = cursorValues[annotationIndex];
                    var originalY = validAnnotations[j].YValue;
                    var annotationText = validAnnotations[j].Text;
                            (
                    // 第二步：按Y值排序注释
                    validAnnotations.Sort((a, b) => a.YValue.CompareTo(b.YValue));
                                $"{title}: {Math.Round(seriesYValue, 2)}",
                    // 检查与前一个注释的间距
                    if (j > 0)
                    {
                        var prevY = validAnnotations[j - 1].YValue;
                        var prevIndex = validAnnotations[j - 1].Index;
                        seriesTitleUpdates[t] = $"{title}: {Math.Round(seriesYValue, 2)}";
                        if (prevIndex < cursorValues.Count)
                        {
                            var prevYMax = cursorValues[prevIndex].AxisValuesBoundaries.YMax;

                            // 如果太接近前一个注释，调整位置
                            if (originalY - prevY < minYSpacing) adjustedY = prevYMax + 0.5; // 在前一个注释下方放置
                        }
                    }
                    {
                        int annotationIndex = validAnnotations[j].Index;
                        if (annotationIndex >= cursorValues.Count)
                            continue;
                    var requiredWidth = requiredWidthPx * xPerPixel;
                    var annotationOffset = annotationOffsetPx * xPerPixel;

                    // 收集注释更新（不立即更新）
                    annotationUpdates[annotation] = a =>
                    {
                        // 根据光标位置调整注释位置（左侧或右侧）
                        if (isNearRightEdge)
                        {
                            a.AxisValuesBoundaries.XMax = cursorX - 2 * xPerPixel;
                            a.AxisValuesBoundaries.XMin = a.AxisValuesBoundaries.XMax - requiredWidth;
                        }
                        else
                        {
                            a.AxisValuesBoundaries.XMin = cursorX + annotationOffset;
                            a.AxisValuesBoundaries.XMax = a.AxisValuesBoundaries.XMin + requiredWidth;
                        }

                        a.AxisValuesBoundaries.YMax = adjustedY + 0.25;
                        a.AxisValuesBoundaries.YMin = adjustedY - 0.25;
                        a.Text = annotationText;
                        a.Visible = true;
                    };
                }
                        }
                // 设置X轴注释位置和内容
                if (cursorIndex < cursorValues.Count)
                {
                    var xAnnotationText = $"X: {Math.Round(cursors[i].ValueAtXAxis, 2)}";
                        double requiredWidth = requiredWidthPx * xPerPixel;
                    // 计算所需的宽度
                    var xRequiredWidthPx = CalculateAnnotationWidth(xAnnotationText);
                    var xRequiredWidth = xRequiredWidthPx * xPerPixel;
                    var perPixelLocal = annotationOffsetPx * xPerPixel;

                    // 收集X轴注释更新（不立即更新）
                    annotationUpdates[cursorValues[cursorIndex]] = a =>
                    {
                        // 同样根据光标位置调整X轴注释
                        if (isNearRightEdge)
                        {
                            a.AxisValuesBoundaries.XMax = cursorX - 2 * xPerPixel;
                            a.AxisValuesBoundaries.XMin = a.AxisValuesBoundaries.XMax - xRequiredWidth;
                        }
                        else
                        {
                            a.AxisValuesBoundaries.XMin = cursorX + perPixelLocal;
                            a.AxisValuesBoundaries.XMax = a.AxisValuesBoundaries.XMin + xRequiredWidth;
                        }

                        a.AxisValuesBoundaries.YMax = y + 0.5;
                        a.AxisValuesBoundaries.YMin = y;
                        a.Text = xAnnotationText;
                        a.Visible = true;
                    };
                }
            }

            // 一次性批量应用所有注释更新
            foreach (var update in annotationUpdates) update.Value(update.Key);
                        double xRequiredWidthPx = CalculateAnnotationWidth(xAnnotationText);
            // 一次性批量应用所有标题更新
            foreach (var update in seriesTitleUpdates) update.Key.Title.Text = update.Value;

            chart.EndUpdate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"更新光标结果时出错: {ex.Message}");
        }
    }
                            }
    /// <summary>
    ///     清理资源
    /// </summary>
    private void CleanupResources()
    {
        // 重置光标更新计数器
        CursorUpdateCounter = 0;
        // 清理文件数据队列
        while (FileData.TryDequeue(out _))
        {
                        };
                    }
                }

                // 一次性批量应用所有注释更新
                foreach (var update in annotationUpdates)
                {
                    update.Value(update.Key);
                }

                // 一次性批量应用所有标题更新
                foreach (var update in seriesTitleUpdates)
                {
                    update.Key.Title.Text = update.Value;
                }

                chart.EndUpdate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"更新光标结果时出错: {ex.Message}");
            }
        }
                                a.AxisValuesBoundaries.XMin = cursorX + perPixelLocal;
        /// <summary>
        /// 清理资源
        /// </summary>
        private void CleanupResources()
        {

            // 重置光标更新计数器
            CursorUpdateCounter = 0;
            // 清理文件数据队列
            while (FileData.TryDequeue(out _)) { }

                // 一次性批量应用所有注释更新
                foreach (var update in annotationUpdates)
                {
                    update.Value(update.Key);
                }

                // 一次性批量应用所有标题更新
                foreach (var update in seriesTitleUpdates)
                {
                    update.Key.Title.Text = update.Value;
                }

                chart.EndUpdate();
                                for (var j = i; j < end; j++)
                                    batch.AppendLine(
                                        $"{j}-{Math.Round(data[channelId][j], 5)}"
                                    );
            }
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        private void CleanupResources()
        {

            // 重置光标更新计数器
            CursorUpdateCounter = 0;
            // 清理文件数据队列
            while (FileData.TryDequeue(out _)) { }
        }
    }

    /// <summary>
    ///     保存折线数据
    /// </summary>
    private void SaveData()
    {
        Task.Run(async () =>
        {
            try
            {
                while (IsRunning)
                    if (FileData.TryDequeue(out var data))
                    {
                        if (data == null || data.Length != _seriesCount) continue;

                                    for (int j = i; j < end; j++)
                                    {
                                        batch.AppendLine(
                                            $"{j}-{Math.Round(data[channelId][j], 5)}"
                                        );
                                    }

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
                        for (var channelId = 0; channelId < _seriesCount; channelId++)
                        {
                            if (data[channelId] == null || data[channelId].Length == 0)
                                continue;

                            await sw.WriteLineAsync($"CH{channelId + 1}:");

                            // 分批写入数据，避免一次性构建大字符串
                            const int batchSize = 1000;
                            for (var i = 0; i < data[channelId].Length; i += batchSize)
                            {
                                var batch = new StringBuilder();
                                var end = Math.Min(i + batchSize, data[channelId].Length);

                                    for (int j = i; j < end; j++)
                                    {
                                        batch.AppendLine(
                                            $"{j}-{Math.Round(data[channelId][j], 5)}"
                                        );
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
            catch (Exception ex)
            {
                Debug.WriteLine($"保存数据时出错: {ex.Message}");
            }
        });
    }

    #region 右键菜单

    /// <summary>
    ///     显示右键菜单
    /// </summary>
    private void ShowMenu(object obj)
    {
        if (obj is not Canvas canvas)
            return;

        // 获取鼠标当前位置
        MousePos = Mouse.GetPosition(canvas);

        if (canvas.Children[0] is LightningChart chart)
        {
            chart.BeginUpdate();

            ContextMenu menu = new();

            var deleteItem = new MenuItem { Header = "删除曲线" };

            if (chart.ViewXY.SampleDataSeries.Count > 8)
                // 为每条曲线创建子菜单项
                for (var i = 0; i < chart.ViewXY.SampleDataSeries.Count - 8; i++)
                {
                    var seriesIndex = i + 8;
                    var series = chart.ViewXY.SampleDataSeries[seriesIndex];
                    var seriesTitle = series.Title?.Text;

                    var seriesItem = new MenuItem
                    {
                        Header = seriesTitle,
                        Command = new DelegateCommand(() => { DeleteSample(chart, seriesIndex); })
                    };

                    deleteItem.Items.Add(seriesItem);
                }
            else
                deleteItem.Items.Add(
                    new MenuItem { Header = "没有可用的曲线", IsEnabled = false }
                );

            var updateTitleItem = new MenuItem { Header = "修改标题" };

            // 为每条曲线创建子菜单项
            foreach (
                var seriesItem in from series in chart.ViewXY.SampleDataSeries
                let series1 = series
                select new MenuItem
                // 为每个光标创建子菜单项
                var cursorItem = new MenuItem
                {
                    Header = $"{cursor.Tag} (X: {Math.Round(cursor.ValueAtXAxis, 2)})",
                    Command = new DelegateCommand(() => { DeleteSeriesCursor(chart, cursorIndex); })
                };

            if (chart.ViewXY.SampleDataSeries.Count > _seriesCount)
                menu.Items.Add(
                    new MenuItem
                    {
                        Header = "删除添加曲线",
                        Command = new DelegateCommand<LightningChart>(DeleteAllAddSeries),
                        CommandParameter = chart
                    }
                );

            menu.Items.Add(
                new MenuItem
                {
                    Header = "切换图例",
                    Command = new DelegateCommand(() =>
                    {
                        chart.ViewXY.LegendBoxes[0].Visible = !chart
                            .ViewXY
                            .LegendBoxes[0]
                            .Visible;
                    })
                }
            );

            menu.Items.Add(
                new MenuItem
                    // 为每个光标创建子菜单项
                    var cursorItem = new MenuItem()
                    {
                        Header = $"{cursor.Tag} (X: {Math.Round(cursor.ValueAtXAxis, 2)})",
                        Command = new DelegateCommand(() =>
                        {
                            DeleteSeriesCursor(chart, cursorIndex);
                        }),
                    };
                    Header = "添加注释",
                    Command = new DelegateCommand(() => AddComment(canvas))
                }
            );

            menu.Items.Add(
                new MenuItem
                {
                    Header = "添加光标",
                    Command = new DelegateCommand(() =>
                    {
                        // 直接调用方法，不使用异步调用
                        CreateLineSeriesCursor(chart);
                    })
                }
            );

            var deleteMenuItem = new MenuItem { Header = "删除光标" };

            for (var i = 0; i < chart.ViewXY.LineSeriesCursors.Count; i++)
            {
                var cursorIndex = i;
                var cursor = chart.ViewXY.LineSeriesCursors[i];

                    // 为每个光标创建子菜单项
                    var cursorItem = new MenuItem()
                    {
                        Header = $"{cursor.Tag} (X: {Math.Round(cursor.ValueAtXAxis, 2)})",
                        Command = new DelegateCommand(() =>
                        {
                            DeleteSeriesCursor(chart, cursorIndex);
                        }),
                    };

                deleteMenuItem.Items.Add(cursorItem);
            }

            // 如果没有光标，添加一个禁用的菜单项
            if (chart.ViewXY.LineSeriesCursors.Count == 0)
                deleteMenuItem.Items.Add(
                    new MenuItem { Header = "没有可用的光标", IsEnabled = false }
                );

            menu.Items.Add(deleteMenuItem);

            chart.ContextMenu = menu;
            // 初始化图表事件处理
            chart.MouseRightButtonDown += (_, e) => e.Handled = true;
            chart.EndUpdate();
        }
    }

    /// <summary>
    ///     删除曲线
    /// </summary>
    /// <param name="chart"></param>
    /// <param name="seriesIndex"></param>
    private void DeleteSample(LightningChart chart, int seriesIndex)
    {
        if (
            chart == null
            || seriesIndex < 0
        // 创建注释
        for (var i = 0; i < chart.ViewXY.SampleDataSeries.Count + 1; i++)
            chart.ViewXY.Annotations.Add(CreateAnnotation(chart));

        // 获取需要删除的曲线
        var series = chart.ViewXY.SampleDataSeries[seriesIndex];

        // 删除与该曲线相关的所有注释 每个光标都有一组注释(X轴注释+每条曲线的注释)
        for (
            var cursorIndex = 0;
            cursorIndex < chart.ViewXY.LineSeriesCursors.Count;
            cursorIndex++
        )
        {
            // 计算当前光标的注释起始索引
            var annotationBaseIndex = cursorIndex * (chart.ViewXY.SampleDataSeries.Count + 1);

            // 计算该曲线对应注释的索引
            // +1是因为首个注释是X轴注释
            var annotationIndex = annotationBaseIndex + seriesIndex + 1;

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
            // 创建注释
            for (int i = 0; i < chart.ViewXY.SampleDataSeries.Count + 1; i++)
            {
                chart.ViewXY.Annotations.Add(CreateAnnotation(chart));
            }
        series.Dispose();

        // 更新相关注释
        UpdateCursorResult(chart);

        chart.EndUpdate();
    }

    /// <summary>
    ///     添加光标
    /// </summary>
    /// <param name="chart"></param>
    public void CreateLineSeriesCursor(LightningChart chart)
    {
        chart.BeginUpdate();
        LineSeriesCursor cursor = new(chart.ViewXY, chart.ViewXY.XAxes[0])
        {
            Visible = true,
            SnapToPoints = true,
            ValueAtXAxis = 100
        };
        cursor.LineStyle.Color = Color.FromArgb(150, 255, 0, 0);
        cursor.TrackPoint.Color1 = Colors.White;
        cursor.TrackPoint.Shape = Shape.Circle;
        cursor.Tag = $"光标{chart.ViewXY.LineSeriesCursors.Count + 1}";
        cursor.PositionChanged += Cursor_PositionChanged;
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

    /// <summary>
    ///     创建注释
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
            AxisValuesBoundaries = new BoundsDoubleXY(0, 0, -4, -3.5)
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
            for (var i = 8; i < chart.ViewXY.SampleDataSeries.Count; i++)
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

            // 处理光标相关的注释 每个光标有 (系列数+1) 个注释，第一个是X轴注释，其余是每个系列的注释
            for (
                var cursorIndex = 0;
                cursorIndex < chart.ViewXY.LineSeriesCursors.Count;
                cursorIndex++
            )
            {
                // 计算当前光标注释的基础索引
                var annotationBase = cursorIndex * (chart.ViewXY.SampleDataSeries.Count + 1);

                // 计算需要删除注释的索引
                foreach (var seriesIndex in seriesIndices)
                {
                    // 计算注释索引：基础索引 + 1(X轴注释) + 系列索引
                    var annotationIndex = annotationBase + 1 + seriesIndex;

                    // 确保索引有效
                    if (annotationIndex < chart.ViewXY.Annotations.Count)
                        annotationsToRemove.Add(chart.ViewXY.Annotations[annotationIndex]);
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
            DXMessageBox.Show(
                "已删除所有添加的曲线",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );

            // 主线程中更新光标结果，避免UI冻结
            Application.Current.Dispatcher.InvokeAsync(
                () => { UpdateCursorResult(chart); },
                DispatcherPriority.Background
            );
        }
        catch (Exception ex)
        {
            chart.EndUpdate();
            Debug.WriteLine($"删除曲线时出错: {ex.Message}");
            DXMessageBox.Show(
                $"删除曲线时发生错误: {ex.Message}",
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
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
                DXMessageBox.Show(
                    "已删除所有添加的曲线",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                // 主线程中更新光标结果，避免UI冻结
                Application.Current.Dispatcher.InvokeAsync(
                    () =>
                    {
                        UpdateCursorResult(chart);
                    },
                    DispatcherPriority.Background
                );
            }
            catch (Exception ex)
            {
                chart.EndUpdate();
                Debug.WriteLine($"删除曲线时出错: {ex.Message}");
                DXMessageBox.Show(
                    $"删除曲线时发生错误: {ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }
                    // 先从集合中移除
                    chart.ViewXY.SampleDataSeries.Remove(series);
                    // 然后安全地释放资源
                    series.Dispose();
                }

                // 更新图表
                chart.EndUpdate();

                // 更新光标结果
                DXMessageBox.Show(
                    "已删除所有添加的曲线",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

            // 使用分页加载的方式获取文件，避免一次性加载所有文件
            Task.Run(async () =>
            {
                // 获取所有文件并按修改时间排序，只取最新的maxFileCount个
                var fileInfos = new DirectoryInfo(FolderPath)
                    .GetFiles("*.txt")
                    .OrderByDescending(f => f.LastWriteTime)
                    .Select(f => f.Name)
                    .ToList();
            catch (Exception ex)
            {
                chart.EndUpdate();
                Debug.WriteLine($"删除曲线时出错: {ex.Message}");
                DXMessageBox.Show(
                    $"删除曲线时发生错误: {ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

    /// <summary>
    ///     删除选中的光标
    /// </summary>
    /// <param name="chart"></param>
    /// <param name="cursorIndex"></param>
    private void DeleteSeriesCursor(LightningChart chart, int cursorIndex)
    {
        if (
    /// <summary>
    ///     重置所有图表的曲线数据
    /// </summary>
    private void ResetAllCharts()
    {
        try
        {
            // 记录当前运行状态，重置后恢复
            var wasRunning = IsRunning;

            // 如果正在运行，暂时停止渲染更新，但不停止试验
            if (wasRunning) CompositionTarget.Rendering -= CompositionTarget_Rendering;

            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var chart in Charts)
                {
                    chart.BeginUpdate();

                    // 清空每条曲线的数据
                    foreach (var series in chart.ViewXY.SampleDataSeries)
                        // 保留曲线但清空其数据
                        series?.Clear();

                    // 重置X轴范围
                    chart.ViewXY.XAxes[0].SetRange(0, Points);

                    // 更新光标位置
                    foreach (var cursor in chart.ViewXY.LineSeriesCursors) cursor.ValueAtXAxis = 100; // 重置到初始位置

                    // 清空注释文本
                    foreach (var annotation in chart.ViewXY.Annotations) annotation.Visible = false;

                    chart.EndUpdate();

                    // 更新光标结果
                    UpdateCursorResult(chart);
                }
            });

            // 清理数据缓冲区
            lock (_updateLock)
            {
                foreach (var buffer in _channelBuffers.Values) buffer.Clear();
            }

            // 清空文件数据队列
            while (FileData.TryDequeue(out _))
            {
            }

            // 重置点数计数
            for (var i = 0; i < PointNumbers.Length; i++) PointNumbers[i] = 0;

            // 重置光标更新计数器
            CursorUpdateCounter = 0;

            // 如果之前在运行，恢复渲染更新
            if (wasRunning) CompositionTarget.Rendering += CompositionTarget_Rendering;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"重置图表时出错: {ex.Message}");
        }
    }

    /// <summary>
    ///     开始试验
    /// </summary>
    private async Task StartTest()
    {
        // 取消任何正在进行的操作
        Source.Cancel();
        Source = new CancellationTokenSource();
        
        // 彻底清除旧数据
        lock (_updateLock)
        {
            foreach (var buffer in _channelBuffers.Values)
                buffer.Clear();
            _channelBuffers.Clear();
        }
        
        // 清空文件数据队列
        while (FileData.TryDequeue(out _)) { }
        
        // 重置点数计数
        for (var i = 0; i < PointNumbers.Length; i++)
            PointNumbers[i] = 0;
        
        // 重置光标更新计数器
        CursorUpdateCounter = 0;

        // 先重置所有图表，确保数据正确显示
        ResetAllCharts();
                // 清理数据缓冲区
        // 发送开始命令
        await GlobalValues.TcpClient.SendDataClient(1);
                    foreach (var buffer in _channelBuffers.Values)
                    {
                        buffer.Clear();
        // 启动数据保存任务
        SaveData();

                // 清空文件数据队列
                while (FileData.TryDequeue(out _)) { }

                // 重置点数计数
                for (int i = 0; i < PointNumbers.Length; i++)
                {
                    PointNumbers[i] = 0;
                }

                // 重置光标更新计数器
                CursorUpdateCounter = 0;

                // 如果之前在运行，恢复渲染更新
                if (wasRunning)
                {
                    CompositionTarget.Rendering += CompositionTarget_Rendering;
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"重置图表时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 开始试验
        /// </summary>
        private async Task StartTest()
        {
            // 先重置所有图表，确保数据正确显示
            ResetAllCharts();
                    foreach (var buffer in _channelBuffers.Values)
            // 发送开始命令
            await GlobalValues.TcpClient.SendDataClient(1);
                    }
                }

            // 启动数据保存任务
            SaveData();

                // 重置点数计数
                for (int i = 0; i < PointNumbers.Length; i++)
                {
                    PointNumbers[i] = 0;
                }

                // 重置光标更新计数器
                CursorUpdateCounter = 0;

                // 如果之前在运行，恢复渲染更新
                if (wasRunning)
                {
            Task.Run(async () =>
                await Application.Current.Dispatcher.InvokeAsync(
                    () => { UpdateCursorResult(chart); },
                    DispatcherPriority.Background
                )
            );
        }
    }
        /// </summary>
        private async Task StartTest()
        {
            // 先重置所有图表，确保数据正确显示
            ResetAllCharts();

            // 发送开始命令
            await GlobalValues.TcpClient.SendDataClient(1);

        IsRunning = true; // 更新运行状态

            // 启动数据保存任务
            SaveData();

        // 添加渲染事件处理
        CompositionTarget.Rendering += CompositionTarget_Rendering;

        foreach (var chart in Charts)
        {
            var count = chart.ViewXY.PointLineSeries.Count;

            if (count > 8)
                for (var i = count; i > 8; i--)
                {
                    chart.ViewXY.PointLineSeries.Remove(chart.ViewXY.PointLineSeries[i - 1]);
                    chart.ViewXY.YAxes.Remove(chart.ViewXY.YAxes[i - 1]);
                }
        }
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
        CleanupResources();

        // 获取文件列表
        GetFolderFiles();
    }

    /// <summary>
    ///     更新光标位置
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
                cursor.ValueAtXAxis = chart.ViewXY.XAxes[0].Minimum;

            if (cursor.ValueAtXAxis > chart.ViewXY.XAxes[0].Maximum)
                cursor.ValueAtXAxis = chart.ViewXY.XAxes[0].Maximum;

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
    ///     打开文件夹
    /// </summary>
    private void OpenFolder()
    {
        try
        {
            var openFolderDialog = new OpenFolderDialog();

            if (openFolderDialog.ShowDialog() == true)
            {
                // 获取完整路径（包含文件名）
                FolderPath = openFolderDialog.FolderName;

                InitializeFileWatcher();

                GetFolderFiles();
            }
        }
        catch (Exception ex)
        {
            // 处理异常
            DXMessageBox.Show("无法打开文件夹: " + ex.Message);
        }
    }

    /// <summary>
    ///     清空文件夹
    /// </summary>
    private void ClearFolder()
    {
        try
        {
            if (
                (DialogResult)
                DXMessageBox.Show("是否清空文件夹?", "提示", MessageBoxButton.YesNo)
                == DialogResult.Yes
            )
            {
                // 删除文件夹中所有的文件
                FolderPath.Delete();
        /// <summary>
        /// 打开文件夹
        /// </summary>
        private void OpenFolder()
        {
            try
            {
                var openFolderDialog = new OpenFolderDialog();

                if (openFolderDialog.ShowDialog() == true)
                {
                    // 获取完整路径（包含文件名）
                    FolderPath = openFolderDialog.FolderName;

                    InitializeFileWatcher();

                    GetFolderFiles();
                }
            }
            catch (Exception ex)
            {
                // 处理异常
                DXMessageBox.Show("无法打开文件夹: " + ex.Message);
            }
        }
        Random random = new();
        /// <summary>
        /// 清空文件夹
        /// </summary>
        private void ClearFolder()
        {
            try
            {
                if (
                    (DialogResult)
                        DXMessageBox.Show("是否清空文件夹?", "提示", MessageBoxButton.YesNo)
                    == DialogResult.Yes
                )
                {
                    // 删除文件夹中所有的文件
                    FolderPath.Delete();
        private void OpenFolder()
        {
            try
            {
                var openFolderDialog = new OpenFolderDialog();

                if (openFolderDialog.ShowDialog() == true)
                {
                    // 获取完整路径（包含文件名）
                    FolderPath = openFolderDialog.FolderName;

                    InitializeFileWatcher();

                    GetFolderFiles();
                }
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
                if (
                    (DialogResult)
        if (_layoutGroup != null)
        {
            // 创建面板和画布（UI操作保留在UI线程）
            var layPanel = new LayoutPanel();
            var canvas = new Canvas();

            // 创建图表但不立即加载数据
            var chart = CreateChart();
                Application.Current.Dispatcher.Invoke(() =>
            // 将图表添加到Charts集合（这应该在数据加载前完成）
            Charts.Add(chart);

            // 异步加载数据
            if (IsRunning)
                Task.Run(() =>
                {
                    // 在后台线程准备数据
                    var channelData = new Dictionary<int, float[]>();

                    lock (_updateLock)
                    {
                        for (var i = 0; i < _seriesCount; i++)
                            if (_channelBuffers.TryGetValue(i, out var buffer) && buffer.Count > 0)
                            {
                                var dataList = new List<float>();
                                foreach (var chunk in buffer)
                                {
                                    dataList.AddRange(chunk.Take(Math.Min(chunk.Length, Points)));
                                    if (dataList.Count >= Points) break;
                                }

                                channelData[i] = dataList.Count > 0 ? dataList.Take(Points).ToArray() : null;
                            }
                    }

                    // 回到UI线程更新图表
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            chart.BeginUpdate();

                            // 应用准备好的数据
                            for (var i = 0; i < _seriesCount; i++)
                            {
                                var series = chart.ViewXY.SampleDataSeries[i];
                                if (channelData.TryGetValue(i, out var data) && data is { Length: > 0 })
                                    series.SamplesSingle = data;
                            }

                            chart.EndUpdate();
                Charts.Add(chart);
                            // 只在完成所有更新后调用一次UpdateCursorResult
                            UpdateCursorResult(chart);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Chart update error: {ex.Message}");
                        }
                    }, DispatcherPriority.Background); // 使用Background优先级
                });
                        lock (_updateLock)
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
                                    channelData[i] = dataList.Count > 0 ? dataList.Take(Points).ToArray() : null;
            layPanel.ContextMenuCustomizations.AddRange(Res);
            layPanel.AllowDrop = true;

            // 优化拖放处理
            layPanel.Drop += (_, e) =>
            {
                try
                {
                    // 检查是否可以获取文本格式的数据
                    if (e.Data.GetDataPresent(DataFormats.Text))
                    {
                        var path = e.Data.GetData(DataFormats.Text) as string;
                        if (string.IsNullOrEmpty(path))
                            return;
                                    var series = chart.ViewXY.SampleDataSeries[i];
                        var filePath = Path.Combine(FolderPath, path);

                        if (File.Exists(filePath))
                        {
                            // 确保DataContext和控制器已经初始化

                            // 读取文件内容
                            var allLines = File.ReadAllLines(filePath);

                            // 解析文件中的通道数据
                            var channelData = new Dictionary<string, List<float>>();
                            string currentChannel = null;
                            {
                            foreach (var line in allLines)
                                // 检查是否是通道标识行
                                if (line.StartsWith("CH") && line.Contains(':'))
                                {
                                    currentChannel = line.Trim().TrimEnd(':');
                                    channelData[currentChannel] = [];
                                }
                                // 如果是数据行且有当前通道
                                else if (currentChannel != null && line.Contains('-'))
                                {
                                    var parts = line.Split(
                                        '-',
                                        2,
                                        StringSplitOptions.RemoveEmptyEntries
                                    );
                                    if (parts.Length == 2 && float.TryParse(parts[1], out var value))
                                        channelData[currentChannel].Add(value);
                                }
                                // 如果是空行，重置当前通道
                                else if (string.IsNullOrWhiteSpace(line))
                                {
                                    currentChannel = null;
                                }
                layPanel.Drop += (_, e) =>
                            // 如果没有找到任何通道数据
                            if (channelData.Count == 0)
                            {
                                DXMessageBox.Show(
                                    "未在文件中找到任何有效的通道数据。",
                                    "数据错误",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Error
                                );
                                return;
                            }

                            // 创建多选对话框，让用户选择要添加的通道
                            var checkBoxes = new List<CheckBox>();
                            var dialogContent = new StackPanel { Orientation = Orientation.Vertical };

                            // 添加说明文本
                            dialogContent.Children.Add(
                                new TextBlock
                                {
                                    Text = "请选择要添加的通道：",
                                    Margin = new Thickness(0, 0, 0, 10),
                                    FontWeight = FontWeights.Bold
                                }
                            );

                            // 创建通道复选框
                            foreach (
                                var checkBox in from channel in channelData.Keys
                                let dataCount = channelData[channel].Count
                                select new CheckBox
                                {
                                    Content = $"{channel} (数据点: {dataCount})",
                                    Margin = new Thickness(5),
                                    IsChecked = false
                                }
                            )
                            {
                                checkBoxes.Add(checkBox);
                                dialogContent.Children.Add(checkBox);
                            }

                            // 创建全选/取消全选按钮
                            var selectAllButton = new Button
                            {
                                Content = "全选",
                                Width = 80,
                                Margin = new Thickness(5),
                                HorizontalAlignment = HorizontalAlignment.Left
                            };
                            selectAllButton.Click += (_, _) =>
                            {
                                foreach (var cb in checkBoxes)
                                    cb.IsChecked = true;
                            };

                            var deselectAllButton = new Button
                            {
                                Content = "取消全选",
                                Width = 80,
                                Margin = new Thickness(5),
                                HorizontalAlignment = HorizontalAlignment.Left
                            };
                            deselectAllButton.Click += (_, _) =>
                            {
                                foreach (var cb in checkBoxes)
                                    cb.IsChecked = false;
                            };

                            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };
                            buttonPanel.Children.Add(selectAllButton);
                            buttonPanel.Children.Add(deselectAllButton);
                            dialogContent.Children.Add(buttonPanel);

                            // 创建并显示对话框
                            var dialog = new DXDialog
                            {
                                Content = dialogContent,
                                Width = 400,
                                Height = 420,
                                Title = $"选择通道 - {Path.GetFileName(filePath)}",
                                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                                Buttons = DialogButtons.OkCancel,
                                ShowIcon = true
                            };

                            if (dialog.ShowDialog() == true)
                            {
                                // 用户点击了确定，处理选中的通道
                                var selectedChannels = new List<string>();
                                for (var i = 0; i < checkBoxes.Count; i++)
                                    if (checkBoxes[i].IsChecked == true)
                                    {
                                        // 从复选框内容中提取通道名称
                                        var channelName = channelData.Keys.ElementAt(i);
                                        selectedChannels.Add(Path.GetFileName(filePath).Split('.', 2)[0] + "_" +
                                                             channelName);
                                    }

                                // 如果没有选择任何通道，直接返回
                                if (selectedChannels.Count == 0)
                                    return;

                                // 开始绘制选定的通道
                                chart.BeginUpdate();
                                        cb.IsChecked = true;
                                foreach (var channel in selectedChannels)
                                {
                                    // 检查图表中是否已经有相同标题的曲线
                                    var existingSeries = chart
                                        .ViewXY.SampleDataSeries.FirstOrDefault(s =>
                                            s.Title.Text.Split(':')[0] == channel
                                        );

                                    if (existingSeries != null)
                                    {
                                        var result = DXMessageBox.Show(
                                            $"图表中已存在通道 {channel}，是否覆盖？",
                                            "确认覆盖",
                                            MessageBoxButton.YesNo,
                                            MessageBoxImage.Question
                                        );

                                        if (result != MessageBoxResult.Yes)
                                            continue;

                                        // 移除现有曲线
                                        chart
                                            .ViewXY.SampleDataSeries.Remove(existingSeries);
                                        existingSeries.Dispose();
                                    }

                                    var yData = channelData[channel.Split('_', 2)[1]].ToArray();

                                    // 创建新曲线
                                    SampleDataSeries series = new(
                                        chart.ViewXY,
                                        chart.ViewXY.XAxes[0],
                                        chart.ViewXY.YAxes[0]
                                    )
                                    {
                                        Title = new SeriesTitle
                                        {
                                            Text = channel
                                        },
                                        LineStyle =
                                        {
                                            Color = ChartTools.CalcGradient(
                                                GenerateUniqueColor(),
                                                Colors.White,
                                                50
                                            )
                                        },
                                        SampleFormat = SampleFormat.SingleFloat
                                    };

                                    series.MouseOverOn += (_, _) => { Sample = series; };
                                    {
                                    series.AddSamples(yData, false);
                                    chart.ViewXY.SampleDataSeries.Add(series);
                                            .ViewXY.SampleDataSeries.FirstOrDefault(s =>
                                    // 添加新曲线的注释
                                    for (
                                        var i = 0;
                                        i < chart.ViewXY.LineSeriesCursors.Count;
                                        i++
                                    )
                                    {
                                        var ann = CreateAnnotation(chart);
                                        chart.ViewXY.Annotations.Add(ann);
                                    }
                                }

                                chart.EndUpdate();
                                UpdateCursorResult(chart);

                                // 显示成功消息
                                DXMessageBox.Show(
                                    $"已成功添加 {selectedChannels.Count} 个通道的数据。",
                                    "添加完成",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information
                                );
                            }
                        }
                        else
                        {
                            DXMessageBox.Show(
                                $"找不到文件: {filePath}",
                                "文件错误",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ChartDockGroup_Drop出错: {ex.Message}");
                    DXMessageBox.Show(
                        $"处理文件时出错: {ex.Message}",
                        "错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            };
                                        {
                                           Sample = series;
                                        };
                                            .ViewXY.SampleDataSeries.FirstOrDefault(s =>
                                        series.AddSamples(yData, false);
                if (hitTest == null || hitTest.VisualHit == canvas) _text.ClearFocus();
            };

            canvas.SizeChanged += (_, _) =>
            {
                chart.Width = canvas.ActualWidth;
                chart.Height = canvas.ActualHeight;
            };

            canvas.PreviewMouseRightButtonDown += (_, _) => { ShowMenu(canvas); };
                                    chart.EndUpdate();
                                    UpdateCursorResult(chart);

                                    // 显示成功消息
                                    DXMessageBox.Show(
                                        $"已成功添加 {selectedChannels.Count} 个通道的数据。",
                                        "添加完成",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Information
                                    );
                                }
                            }
        _fileWatcher = new FileSystemWatcher(FolderPath)
        {
            Filter = "*.txt",
            EnableRaisingEvents = true,
            IncludeSubdirectories = false
        };
                                    MessageBoxImage.Error
                                );
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ChartDockGroup_Drop出错: {ex.Message}");
                        DXMessageBox.Show(
                            $"处理文件时出错: {ex.Message}",
                            "错误",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    }
                };

                                        series.AddSamples(yData, false);
                                        chart.ViewXY.SampleDataSeries.Add(series);

                                        // 添加新曲线的注释
                    if (hitTest == null || hitTest.VisualHit == canvas)
    public void Dispose()
    {
        // 取消所有任务
        Source.Cancel();
        Source.Dispose();
                canvas.SizeChanged += (_, _) =>
                {
                    chart.Width = canvas.ActualWidth;
                    chart.Height = canvas.ActualHeight;
                };

                canvas.PreviewMouseRightButtonDown += (_, _) =>
                {
                    ShowMenu(canvas);
                };
                                    // 显示成功消息
                                    DXMessageBox.Show(
                                        $"已成功添加 {selectedChannels.Count} 个通道的数据。",
                                        "添加完成",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Information
                                    );
                                }
                            }
                            else
                            {
                                DXMessageBox.Show(
            _fileWatcher = new FileSystemWatcher(FolderPath)
        // 强制垃圾回收
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    #endregion Main
}                    {
                        Debug.WriteLine($"ChartDockGroup_Drop出错: {ex.Message}");
                        DXMessageBox.Show(
                            $"处理文件时出错: {ex.Message}",
                            "错误",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    }
                };

            canvas.PreviewMouseDown += (_, e) =>
            {
                var hitTest = VisualTreeHelper.HitTest(canvas, e.GetPosition(canvas));

                    if (hitTest == null || hitTest.VisualHit == canvas)
                    {
                        _text.ClearFocus();
                    }
        public void Dispose()
        {
            // 取消所有任务
            Source.Cancel();
            Source.Dispose();
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
            // 强制垃圾回收
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        #endregion Main
    }
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
                if (FileNames.Contains(fileName)) FileNames.Remove(fileName);
            });
        };
    }

        public void Dispose()
        {
            // 取消所有任务
            Source.Cancel();
            Source.Dispose();

        // 清理通道缓冲区
        lock (_updateLock)
        {
            foreach (var buffer in _channelBuffers.Values) buffer.Clear();
            _channelBuffers.Clear();
        }

        // 清空文件名集合
        FileNames.Clear();

        // 清空通道数据
        while (FileData.TryDequeue(out _))
        {
        }

        // 释放图表资源
        foreach (var chart in Charts) chart.Dispose();
        Charts.Clear();

        // 释放文件监视器
        _fileWatcher?.Dispose();

            // 强制垃圾回收
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        #endregion Main
    }
}
