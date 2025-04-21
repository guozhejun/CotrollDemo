using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CotrollerDemo.Models
{
    public class ResizableTextBox : TextBox
    {
        private const double ResizeMargin = 10; // 边缘调整区域宽度
        private Point _dragStartPosition;
        private bool _isDragging;
        private bool _isResizing;
        private Point _resizeStartPoint;
        private double _originalWidth;
        private double _originalHeight;
        private double _originalLeft;
        private double _originalTop;
        private ResizeDirection _resizeDirection;

        private enum ResizeDirection
        {
            None,
            Right,
            Bottom,
            BottomRight
        }

        public ResizableTextBox()
        {
            // 设置TextBox属性
            this.AcceptsReturn = true;
            this.TextWrapping = TextWrapping.Wrap;
            this.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            this.Background = Brushes.Transparent;
            this.BorderBrush = Brushes.Gray;
            this.BorderThickness = new Thickness(1);
            this.MinWidth = 50;
            this.MinHeight = 30;
            this.Width = 200;
            this.Height = 150;
            this.Foreground = Brushes.White;
            this.Background = Brushes.White;

            this.Focusable = true;

            // 事件处理
            this.PreviewMouseLeftButtonDown += OnMouseLeftButtonDown;
            this.PreviewMouseMove += OnMouseMove;
            this.PreviewMouseLeftButtonUp += OnMouseLeftButtonUp;
            this.LostMouseCapture += OnLostMouseCapture;
            this.PreviewMouseDoubleClick += OnMouseDoubleClick;
            this.GotFocus += ResizableTextBox_GotFocus;
        }

        private void ResizableTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            _isDragging = false;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var mousePos = e.GetPosition(this);
            _resizeDirection = GetResizeDirection(mousePos);

            if (_resizeDirection != ResizeDirection.None)
            {
                _isResizing = true;
                _resizeStartPoint = e.GetPosition(Window.GetWindow(this));
                _originalWidth = this.Width;
                _originalHeight = this.Height;
                this.CaptureMouse();
                e.Handled = true;
            }
            else if (e.ClickCount == 1 && e.LeftButton == MouseButtonState.Pressed)
            {
                _isDragging = true;
                _dragStartPosition = e.GetPosition(Window.GetWindow(this));
                _originalLeft = Canvas.GetLeft(this);
                _originalTop = Canvas.GetTop(this);
                this.CaptureMouse();
                e.Handled = true;
            }
        }

        private ResizeDirection GetResizeDirection(Point mousePos)
        {
            bool onRightEdge = mousePos.X >= this.ActualWidth - ResizeMargin;
            bool onBottomEdge = mousePos.Y >= this.ActualHeight - ResizeMargin;

            if (onRightEdge && onBottomEdge) return ResizeDirection.BottomRight;
            if (onRightEdge) return ResizeDirection.Right;
            if (onBottomEdge) return ResizeDirection.Bottom;

            return ResizeDirection.None;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var mousePos = e.GetPosition(this);

            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(Window.GetWindow(this));
                var offset = currentPosition - _dragStartPosition;

                Canvas.SetLeft(this, _originalLeft + offset.X);
                Canvas.SetTop(this, _originalTop + offset.Y);
            }
            else if (_isResizing && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPoint = e.GetPosition(Window.GetWindow(this));
                var delta = currentPoint - _resizeStartPoint;

                switch (_resizeDirection)
                {
                    case ResizeDirection.Right:
                        this.Width = Math.Max(this.MinWidth, _originalWidth + delta.X);
                        break;

                    case ResizeDirection.Bottom:
                        this.Height = Math.Max(this.MinHeight, _originalHeight + delta.Y);
                        break;

                    case ResizeDirection.BottomRight:
                        this.Width = Math.Max(this.MinWidth, _originalWidth + delta.X);
                        this.Height = Math.Max(this.MinHeight, _originalHeight + delta.Y);
                        break;
                }

                UpdateCursor(_resizeDirection);
            }
            else
            {
                var direction = GetResizeDirection(mousePos);
                UpdateCursor(direction);
            }
        }

        private void UpdateCursor(ResizeDirection direction)
        {
            this.Cursor = direction switch
            {
                ResizeDirection.Right => Cursors.SizeWE,
                ResizeDirection.Bottom => Cursors.SizeNS,
                ResizeDirection.BottomRight => Cursors.SizeNWSE,
                _ => _isDragging ? Cursors.SizeAll : Cursors.IBeam,
            };
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging || _isResizing)
            {
                _isDragging = false;
                _isResizing = false;
                _resizeDirection = ResizeDirection.None;
                this.ReleaseMouseCapture();

                if (!HasMoved(_dragStartPosition, e.GetPosition(Window.GetWindow(this))))
                {
                    this.Focus();
                }
            }
        }

        private bool HasMoved(Point start, Point end)
        {
            return Math.Abs(start.X - end.X) > SystemParameters.MinimumHorizontalDragDistance ||
               Math.Abs(start.Y - end.Y) > SystemParameters.MinimumVerticalDragDistance;
        }

        private void OnLostMouseCapture(object sender, MouseEventArgs e)
        {
            _isDragging = false;
            _isResizing = false;
            _resizeDirection = ResizeDirection.None;
        }

        private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            this.ReleaseMouseCapture();
            this.Focus();
            //this.SelectAll();
        }
    }
}