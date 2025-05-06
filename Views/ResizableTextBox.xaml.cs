using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Runtime.InteropServices;
using System.Threading;

namespace CotrollerDemo.Views
{
    /// <summary>
    /// ResizableTextBox.xaml 的交互逻辑
    /// </summary>
    public partial class ResizableTextBox : UserControl
    {
        #region Dependency Properties

        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            "Text", typeof(string), typeof(ResizableTextBox),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        #endregion Dependency Properties

        private Point? dragStart;
        private Canvas ParentCanvas => Parent as Canvas;

        #region 剪贴板Win32API

        [DllImport("user32.dll")]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll")]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalSize(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardOwner();

        private const uint GMEM_MOVEABLE = 0x0002;
        private const uint GMEM_ZEROINIT = 0x0040;
        private const uint CF_UNICODETEXT = 13;

        // 安全地设置剪贴板文本
        private bool SafeSetClipboardText(string text)
        {
            const int maxRetries = 5;
            const int retryDelayMs = 100;
            
            for (int i = 0; i < maxRetries; i++)
            {
                if (SetClipboardTextNative(text))
                    return true;
                
                Thread.Sleep(retryDelayMs);
            }
            
            return false;
        }

        private bool SetClipboardTextNative(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            
            IntPtr hGlobal = IntPtr.Zero;
            
            try
            {
                // 获取字符串的字节长度（包括结束符）
                var bytes = (text.Length + 1) * 2;
                hGlobal = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (UIntPtr)bytes);
                
                if (hGlobal == IntPtr.Zero)
                    return false;
                
                IntPtr lpGlobal = GlobalLock(hGlobal);
                
                if (lpGlobal == IntPtr.Zero)
                    return false;
                
                try
                {
                    // 复制文本到全局内存
                    Marshal.Copy(text.ToCharArray(), 0, lpGlobal, text.Length);
                }
                finally
                {
                    GlobalUnlock(hGlobal);
                }
                
                // 打开剪贴板并设置数据
                if (!OpenClipboard(IntPtr.Zero))
                    return false;
                
                try
                {
                    EmptyClipboard();
                    IntPtr result = SetClipboardData(CF_UNICODETEXT, hGlobal);
                    return (result != IntPtr.Zero);
                }
                finally
                {
                    CloseClipboard();
                }
            }
            catch
            {
                if (hGlobal != IntPtr.Zero)
                    GlobalFree(hGlobal);
                
                return false;
            }
        }

        // 获取剪贴板文本
        private string SafeGetClipboardText()
        {
            const int maxRetries = 5;
            const int retryDelayMs = 100;
            
            for (int i = 0; i < maxRetries; i++)
            {
                string text = GetClipboardTextNative();
                if (text != null)
                    return text;
                
                Thread.Sleep(retryDelayMs);
            }
            
            return null;
        }

        private string GetClipboardTextNative()
        {
            if (!OpenClipboard(IntPtr.Zero))
                return null;
            
            try
            {
                IntPtr hClipboardData = GetClipboardData(CF_UNICODETEXT);
                if (hClipboardData == IntPtr.Zero)
                    return null;
                
                IntPtr lpText = GlobalLock(hClipboardData);
                if (lpText == IntPtr.Zero)
                    return null;
                
                try
                {
                    return Marshal.PtrToStringUni(lpText);
                }
                finally
                {
                    GlobalUnlock(hClipboardData);
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                CloseClipboard();
            }
        }

        #endregion

        public ResizableTextBox()
        {
            InitializeComponent();
            InitializeEvents();
            InitializeThumbs();
            UpdateTextDisplay();
        }

        private void InitializeEvents()
        {
            // 拖动事件
            MouseLeftButtonDown += (s, e) =>
            {
                BringToFront();
                dragStart = e.GetPosition(ParentCanvas);
                CaptureMouse();
            };

            MouseMove += (s, e) =>
            {
                if (dragStart.HasValue && IsMouseCaptured)
                {
                    var currentPos = e.GetPosition(ParentCanvas);
                    var deltaX = currentPos.X - dragStart.Value.X;
                    var deltaY = currentPos.Y - dragStart.Value.Y;

                    Canvas.SetLeft(this, Canvas.GetLeft(this) + deltaX);
                    Canvas.SetTop(this, Canvas.GetTop(this) + deltaY);
                    dragStart = currentPos;
                }
            };

            MouseLeftButtonUp += (s, e) =>
            {
                dragStart = null;
                ReleaseMouseCapture();
            };
        }

        private void InitializeThumbs()
        {
            void SetupThumb(Thumb thumb, Action<double> widthAction, Action<double> heightAction)
            {
                thumb.DragDelta += (s, e) =>
                {
                    widthAction?.Invoke(e.HorizontalChange);
                    heightAction?.Invoke(e.VerticalChange);
                    e.Handled = true;
                };
            }

            // 边缩放
            SetupThumb(leftThumb,
                dx => { Width = Math.Max(20, Width - dx); Canvas.SetLeft(this, Canvas.GetLeft(this) + dx); },
                null);

            SetupThumb(rightThumb,
                dx => Width = Math.Max(20, Width + dx),
                null);

            SetupThumb(topThumb,
                null,
                dy => { Height = Math.Max(20, Height - dy); Canvas.SetTop(this, Canvas.GetTop(this) + dy); });

            SetupThumb(bottomThumb,
                null,
                dy => Height = Math.Max(20, Height + dy));

            // 角缩放
            SetupThumb(topLeftThumb,
                dx => { Width = Math.Max(20, Width - dx); Canvas.SetLeft(this, Canvas.GetLeft(this) + dx); },
                dy => { Height = Math.Max(20, Height - dy); Canvas.SetTop(this, Canvas.GetTop(this) + dy); });

            SetupThumb(topRightThumb,
                dx => Width = Math.Max(20, Width + dx),
                dy => { Height = Math.Max(20, Height - dy); Canvas.SetTop(this, Canvas.GetTop(this) + dy); });

            SetupThumb(bottomLeftThumb,
                dx => { Width = Math.Max(20, Width - dx); Canvas.SetLeft(this, Canvas.GetLeft(this) + dx); },
                dy => Height = Math.Max(20, Height + dy));

            SetupThumb(bottomRightThumb,
                dx => Width = Math.Max(20, Width + dx),
                dy => Height = Math.Max(20, Height + dy));
        }

        #region 文本编辑处理

        private void UserControl_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            //Text = textBox.Text;
            StartEditing();
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            FinishEditing();
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FinishEditing();
            }
        }

        public void StartEditing()
        {
            textBlock.Visibility = Visibility.Collapsed;
            textBox.Visibility = Visibility.Visible;
            textBox.Focus();
            textBox.SelectAll();
        }

        private void FinishEditing()
        {
            Text = textBox.Text;
            textBlock.Visibility = Visibility.Visible;
            textBox.Visibility = Visibility.Collapsed;
        }

        #endregion 文本编辑处理

        #region 右键菜单处理

        private void TextBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 阻止默认行为
            e.Handled = true;

            // 保存选中状态
            int selectionStart = textBox.SelectionStart;
            int selectionLength = textBox.SelectionLength;

            // 手动打开菜单
            MainContextMenu.PlacementTarget = textBox;
            MainContextMenu.Placement = PlacementMode.RelativePoint;
            var pos = e.GetPosition(textBox);
            MainContextMenu.HorizontalOffset = pos.X;
            MainContextMenu.VerticalOffset = pos.Y;
            MainContextMenu.IsOpen = true;

            // 恢复选中状态
            Dispatcher.BeginInvoke(new Action(() =>
            {
                textBox.SelectionStart = selectionStart;
                textBox.SelectionLength = selectionLength;
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void UserControl_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (e.OriginalSource is ScrollViewer) return;
            UpdateMenuState();
        }

        private void UpdateMenuState()
        {
            var isEditing = textBox.Visibility == Visibility.Visible;
            var hasSelection = !string.IsNullOrEmpty(textBox.SelectedText);

            foreach (MenuItem item in MainContextMenu.Items)
            {
                switch (item.Header?.ToString())
                {
                    case "全选":
                        item.IsEnabled = isEditing;
                        break;

                    case "剪切":
                        item.IsEnabled = isEditing && hasSelection;
                        break;

                    case "复制":
                        item.IsEnabled = hasSelection;
                        break;

                    case "粘贴":
                        item.IsEnabled = isEditing && Clipboard.ContainsText();
                        break;
                }
            }
        }

        #endregion 右键菜单处理

        #region 菜单命令实现

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (textBox.Visibility == Visibility.Visible)
            {
                textBox.SelectAll();
            }
        }

        private void Cut_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(textBox.SelectedText))
            {
                string selectedText = textBox.SelectedText;
                
                // 使用原生API设置剪贴板文本
                if (SafeSetClipboardText(selectedText))
                {
                    // 成功后清除选择的文本
                    textBox.SelectedText = "";
                    Text = textBox.Text;
                    UpdateTextDisplay();
                }
                else
                {
                    // 备选方案：使用SendKeys模拟剪切操作
                    try 
                    {
                        System.Windows.Forms.SendKeys.SendWait("^X");
                    }
                    catch
                    {
                        MessageBox.Show("剪切操作失败，请手动使用Ctrl+X", "提示");
                    }
                }
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(textBox.SelectedText))
            {
                string selectedText = textBox.SelectedText;
                
                // 使用原生API设置剪贴板文本
                if (!SafeSetClipboardText(selectedText))
                {
                    // 备选方案：使用SendKeys模拟复制操作
                    try 
                    {
                        System.Windows.Forms.SendKeys.SendWait("^C");
                    }
                    catch
                    {
                        MessageBox.Show("复制操作失败，请手动使用Ctrl+C", "提示");
                    }
                }
            }
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            // 首先使用Win32 API获取剪贴板文本
            string clipboardText = SafeGetClipboardText();
            
            if (!string.IsNullOrEmpty(clipboardText))
            {
                // 文本获取成功，插入到文本框
                if (textBox.SelectionLength > 0)
                {
                    textBox.SelectedText = clipboardText;
                }
                else
                {
                    int caretIndex = textBox.CaretIndex;
                    textBox.Text = textBox.Text.Insert(caretIndex, clipboardText);
                    textBox.CaretIndex = caretIndex + clipboardText.Length;
                }
                
                Text = textBox.Text;
                UpdateTextDisplay();
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (ParentCanvas == null) return;

            var animation = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            animation.Completed += (s, _) => ParentCanvas.Children.Remove(this);
            BeginAnimation(OpacityProperty, animation);
        }

        #endregion 菜单命令实现

        #region 公共方法

        public void BringToFront()
        {
            if (ParentCanvas != null)
            {
                var maxZ = ParentCanvas.Children.OfType<UIElement>()
                    .Select(Panel.GetZIndex)
                    .DefaultIfEmpty(0)
                    .Max();

                Panel.SetZIndex(this, maxZ + 1);
            }
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ResizableTextBox control)
            {
                control.UpdateTextDisplay();
            }
        }

        private void UpdateTextDisplay()
        {
            textBlock.Text = Text;
            textBox.Text = Text;
        }

        public void ClearFocus()
        {
            textBlock.Text = textBox.Text;
            Text = textBox.Text;
            textBox.Visibility = Visibility.Collapsed;
            textBlock.Visibility = Visibility.Visible;
            Keyboard.ClearFocus();
        }

        #endregion 公共方法
    }
}