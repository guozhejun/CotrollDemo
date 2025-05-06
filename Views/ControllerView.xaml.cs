using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Arction.Wpf.Charting;
using Arction.Wpf.Charting.SeriesXY;
using CotrollerDemo.Models;
using CotrollerDemo.ViewModels;
using DevExpress.Mvvm.Native;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Grid;

namespace CotrollerDemo.Views
{
    /// <summary>
    /// ControllerView.xaml 的交互逻辑
    /// </summary>
    public partial class ControllerView : UserControl
    {
        // 使用类级别的字段跟踪拖拽状态，避免重复触发
        private bool _isDragging = false;

        private readonly List<string> _titleList = [];

        public ControllerView()
        {
            InitializeComponent();

            if (Application.Current.MainWindow is MainWindow mainView)
            {
                _main = mainView;
                _main.LayoutUpdated += MainView_LayoutUpdated;
            }
        }

        private ObservableCollection<string> _tempFiles = [];
        private ControllerViewModel _controller;
        private readonly MainWindow _main = new();

        private void MainView_LayoutUpdated(object sender, EventArgs e)
        {
            double newHeight = _main.ActualHeight - 80;
            if (!FileGroup.Height.Equals(newHeight))
            {
                FileGroup.Height = newHeight;
                FileList.Height = FileGroup.Height - 100;
            }
        }

        private void CanvasBase_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _controller = DataContext as ControllerViewModel;
            if (_controller != null)
            {
                _controller.Res = ChartLayPanel.ContextMenuCustomizations;

                if (_controller.Devices is { Count: > 0 })
                {
                    GridControl.SelectedItem = _controller.Devices.First();
                }
                _tempFiles = _controller.FileNames;

                if (!CanvasBase.Children.Contains(_controller.Charts[0]))
                {
                    CanvasBase.Children.Add(_controller.Charts[0]);
                }
                _controller.Charts[0].Width = CanvasBase.ActualWidth;
                _controller.Charts[0].Height = CanvasBase.ActualHeight;
            }
        }

        void OnDragRecordOver(object sender, DragRecordOverEventArgs e)
        {
            if (e.IsFromOutside)
                e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        /// <summary>
        /// 拖拽文件触发事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ListBoxEdit_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                // 如果已经在拖拽中，直接返回 检查鼠标左键是否按下 检查发送者是否为ListBoxEdit 检查是否有选中的项目
                if (
                    _isDragging
                    || e.LeftButton != MouseButtonState.Pressed
                    || sender is not ListBoxEdit listBoxEdit
                    || listBoxEdit.SelectedItem == null
                )
                    return;

                string fileName = listBoxEdit.SelectedItem.ToString();

                if (string.IsNullOrEmpty(fileName))
                    return;

                // 确保DataContext和控制器已经初始化
                if (DataContext is not ControllerViewModel controller)
                    return;

                _controller = controller;

                // 清空标题列表，避免重复添加
                _titleList.Clear();

                // 如果图表尚未初始化或没有图表，则返回
                if (
                    _controller.Charts == null
                    || _controller.Charts.Count == 0
                    || _controller.Charts[0] == null
                )
                    return;

                // 获取当前图表中所有曲线的标题
                foreach (
                    var title in _controller
                        .Charts[0]
                        .ViewXY.SampleDataSeries.Select(series =>
                            series.Title.Text.Split([':'], 2)[0]
                        )
                )
                {
                    _titleList.Add(title);
                }

                // 检查当前选中的文件是否已经存在于图表中
                string fileTitle = fileName.Split('.')[0];
                if (_titleList.Contains(fileTitle))
                {
                    // 如果文件已经存在，显示提示并返回
                    DXMessageBox.Show(
                        $"文件 {fileName} 已经添加到图表中，不能重复添加。",
                        "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    return;
                }

                // 设置拖拽状态
                _isDragging = true;

                try
                {
                    Dispatcher.Invoke(
                        new Action(() =>
                        {
                            // 创建拖拽数据
                            DataObject data = new();
                            data.SetData(DataFormats.Text, fileName);
                            DragDrop.DoDragDrop(listBoxEdit, data, DragDropEffects.Link);
                        })
                    );
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"拖拽操作出错: {ex.Message}");
                }
                finally
                {
                    // 无论成功与否，都重置拖拽状态
                    _isDragging = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ListBoxEdit_PreviewMouseMove出错: {ex.Message}");
                // 确保拖拽状态被重置
                _isDragging = false;
            }
        }

        /// <summary>
        /// 拖拽文件到曲线图中
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChartLayPanel_Drop(object sender, DragEventArgs e)
        {
            try
            {
                // 检查是否可以获取文本格式的数据
                if (e.Data.GetDataPresent(DataFormats.Text))
                {
                    string path = e.Data.GetData(DataFormats.Text) as string;
                    if (string.IsNullOrEmpty(path))
                        return;

                    string filePath = Path.Combine("D:\\Datas", path);

                    if (File.Exists(filePath))
                    {
                        // 确保DataContext和控制器已经初始化
                        if (DataContext is not ControllerViewModel controller)
                            return;

                        _controller = controller;

                        // 读取文件内容
                        string[] allLines = File.ReadAllLines(filePath);

                        // 解析文件中的通道数据
                        var channelData = new Dictionary<string, List<float>>();
                        string currentChannel = null;

                        foreach (var line in allLines)
                        {
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
                                if (parts.Length == 2 && float.TryParse(parts[1], out float value))
                                {
                                    channelData[currentChannel].Add(value);
                                }
                            }
                            // 如果是空行，重置当前通道
                            else if (string.IsNullOrWhiteSpace(line))
                            {
                                currentChannel = null;
                            }
                        }

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
                                FontWeight = FontWeights.Bold,
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
                                                IsChecked = true,
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
                            HorizontalAlignment = HorizontalAlignment.Left,
                        };
                        selectAllButton.Click += (s, args) =>
                        {
                            foreach (var cb in checkBoxes)
                                cb.IsChecked = true;
                        };

                        var deselectAllButton = new Button
                        {
                            Content = "取消全选",
                            Width = 80,
                            Margin = new Thickness(5),
                            HorizontalAlignment = HorizontalAlignment.Left,
                        };
                        deselectAllButton.Click += (s, args) =>
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
                            ShowIcon = true,
                        };

                        if (dialog.ShowDialog() == true)
                        {
                            // 用户点击了确定，处理选中的通道
                            var selectedChannels = new List<string>();
                            for (int i = 0; i < checkBoxes.Count; i++)
                            {
                                if (checkBoxes[i].IsChecked == true)
                                {
                                    // 从复选框内容中提取通道名称
                                    string channelName = channelData.Keys.ElementAt(i);
                                    selectedChannels.Add(channelName);
                                }
                            }

                            // 如果没有选择任何通道，直接返回
                            if (selectedChannels.Count == 0)
                                return;

                            // 开始绘制选定的通道
                            _controller.Charts[0].BeginUpdate();

                            foreach (var channel in selectedChannels)
                            {
                                // 检查图表中是否已经有相同标题的曲线
                                var existingSeries = _controller
                                    .Charts[0]
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
                                    _controller
                                        .Charts[0]
                                        .ViewXY.SampleDataSeries.Remove(existingSeries);
                                    existingSeries.Dispose();
                                }

                                var yData = channelData[channel].ToArray();

                                // 创建新曲线
                                SampleDataSeries series = new(
                                    _controller.Charts[0].ViewXY,
                                    _controller.Charts[0].ViewXY.XAxes[0],
                                    _controller.Charts[0].ViewXY.YAxes[0]
                                )
                                {
                                    Title = new Arction.Wpf.Charting.Titles.SeriesTitle()
                                    {
                                        Text = Path.GetFileName(filePath) + channel,
                                    },
                                    LineStyle =
                                    {
                                        Color = ChartTools.CalcGradient(
                                            _controller.GenerateUniqueColor(),
                                            Colors.White,
                                            50
                                        ),
                                    },
                                    SampleFormat = SampleFormat.SingleFloat,
                                };

                                series.MouseOverOn += (_, _) =>
                                {
                                    _controller.Sample = series;
                                };

                                series.AddSamples(yData, false);
                                _controller.Charts[0].ViewXY.SampleDataSeries.Add(series);

                                // 添加新曲线的注释
                                for (
                                    int i = 0;
                                    i < _controller.Charts[0].ViewXY.LineSeriesCursors.Count;
                                    i++
                                )
                                {
                                    var ann = _controller.CreateAnnotation(_controller.Charts[0]);
                                    _controller.Charts[0].ViewXY.Annotations.Add(ann);
                                }
                            }

                            _controller.Charts[0].EndUpdate();
                            _controller.UpdateCursorResult(_controller.Charts[0]);

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
        }

        private void TextEdit_EditValueChanged(object sender, EditValueChangedEventArgs e)
        {
            var textEdit = sender as TextEdit;
            var list = _tempFiles.Where(file => file.Contains(textEdit.EditText));

            _controller.FileNames = [];
            list.ForEach(file =>
            {
                _controller.FileNames.Add(file);
            });
        }

        private void GridControl_OnSelectedItemChanged(
            object sender,
            SelectedItemChangedEventArgs e
        )
        {
            _controller = DataContext as ControllerViewModel;
            if (e.NewItem is DeviceInfoModel devices)
            {
                if (_controller != null)
                {
                    _controller.CanConnect = devices.Status == "未连接";
                    _controller.CanDisConnect = devices.Status != "未连接";
                }
            }
        }
    }
}
