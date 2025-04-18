using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

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

        private void StartEditing()
        {
            textBox.Text = Text;
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
            e.Handled = true;
            MainContextMenu.PlacementTarget = textBox;
            MainContextMenu.Placement = PlacementMode.RelativePoint;
            var pos = e.GetPosition(textBox);
            MainContextMenu.HorizontalOffset = pos.X;
            MainContextMenu.VerticalOffset = pos.Y;
            MainContextMenu.IsOpen = true;
        }

        private void UserControl_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (e.OriginalSource is ScrollViewer) return;
            UpdateMenuState();
        }

        private void UpdateMenuState()
        {
            var isEditing = textBox.Visibility == Visibility.Visible;
            var hasText = !string.IsNullOrEmpty(Text);

            foreach (MenuItem item in MainContextMenu.Items)
            {
                switch (item.Header?.ToString())
                {
                    case "全选":
                        item.IsEnabled = isEditing;
                        break;

                    case "剪切":
                        item.IsEnabled = isEditing && hasText;
                        break;

                    case "复制":
                        item.IsEnabled = hasText;
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
            if (!string.IsNullOrEmpty(Text))
            {
                Clipboard.SetText(Text);
                Text = "";
                UpdateTextDisplay();
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(Text))
            {
                Clipboard.SetText(Text);
            }
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText())
            {
                Text += Clipboard.GetText();
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
                    .Select(Canvas.GetZIndex)
                    .DefaultIfEmpty(0)
                    .Max();

                Canvas.SetZIndex(this, maxZ + 1);
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
