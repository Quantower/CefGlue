using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Reactive;
using Avalonia.Threading;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;

namespace Xilium.CefGlue.Avalonia.Platform
{
    /// <summary>
    /// The Avalonia control wrapper.
    /// </summary>
    internal class AvaloniaOffScreenControlHost : AvaloniaControl, IOffScreenControlHost
    {
        // TODO avalonia: get value from OS
        private const int MouseWheelDelta = 100;

        private IDisposable _windowStateChangedObservable;
        private TopLevel _topLevel;

        private PointerPressedEventArgs _lastPointerEvent;
        private Cursor _currentDragCursor;
        private Cursor _previousCursor;

        public event Action LostFocus;
        public event KeyEventHandler KeyDown;
        public event KeyEventHandler KeyUp;
        public event TextInputEventHandler TextInput;
        public event Action<IOffScreenControlHost, CefMouseEvent, CefMouseButtonType, int> MouseButtonPressed;
        public event Action<CefMouseEvent, CefMouseButtonType> MouseButtonReleased;
        public event Action<CefMouseEvent> MouseLeave;
        public event Action<CefMouseEvent> MouseMoved;
        public event Action<CefMouseEvent, int, int> MouseWheelChanged;
        public event Action<CefMouseEvent, CefDragData, CefDragOperationsMask> DragEnter;
        public event Action<CefMouseEvent, CefDragOperationsMask> DragOver;
        public event Action DragLeave;
        public event Action<CefMouseEvent, CefDragOperationsMask> Drop;
        public event Action<float> ScreenInfoChanged;
        public event Action<bool> VisibilityChanged;

        public AvaloniaOffScreenControlHost(Control control, IAvaloniaList<Visual> visualChildren) :
            base(control, visualChildren)
        {
            control.ClipToBounds = true;

            control.AttachedToVisualTree += OnAttachedToVisualTree;
            control.DetachedFromVisualTree += OnDetachedFromVisualTree;

            control.LostFocus += OnLostFocus;

            control.PointerMoved += OnPointerMoved;
            control.PointerExited += OnPointerExited;

            control.PointerPressed += OnPointerPressed;
            control.PointerReleased += OnPointerReleased;

            control.PointerWheelChanged += OnPointerWheelChanged;

            control.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
            control.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
            control.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            control.AddHandler(DragDrop.DropEvent, OnDrop);

            control.KeyDown += OnKeyDown;
            control.KeyUp += OnKeyUp;
            control.TextInput += OnTextInput;

            var image = CreateImage();

            // The frame is a visual-only child, so it inherits nothing, and DragDropDevice tests AllowDrop on the visual it hit alone.
            image[!DragDrop.AllowDropProperty] = control[!DragDrop.AllowDropProperty];

            SetContent(image);
            RenderSurface = new AvaloniaRenderSurface(image);
        }

        public OffScreenRenderSurface RenderSurface { get; }

        private void OnTextInput(object sender, TextInputEventArgs e)
        {
            var handled = false;
            TextInput?.Invoke(e.Text, out handled);
            e.Handled = handled;
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            var handled = false;
            KeyUp?.Invoke(e.AsCefKeyEvent(true), out handled);
            e.Handled = handled;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            var handled = false;
            KeyDown?.Invoke(e.AsCefKeyEvent(false), out handled);

            // Avalonia raises no TextInput for control characters, so Chromium would never get the char event that inserts the line break.
            if (e.Key == Key.Enter && (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None)
            {
                KeyDown?.Invoke(new CefKeyEvent()
                {
                    EventType = CefKeyEventType.Char,
                    WindowsKeyCode = '\r',
                    Character = '\r',
                    Modifiers = e.KeyModifiers.AsCefKeyboardModifiers()
                }, out _);
            }

            var key = e.Key;
            if (key == Key.Tab  // Avoid tabbing out the web browser control
                || key == Key.Home || key == Key.End // Prevent keyboard navigation using home and end keys
                || key == Key.Up || key == Key.Down || key == Key.Left || key == Key.Right // Prevent keyboard navigation using arrows
            )
            {
                handled = true;
            }

            e.Handled = handled;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            _control.Cursor = _previousCursor; // restore cursor
            Drop?.Invoke(e.AsCefMouseEvent(MousePositionReferential), e.DragEffects.AsCefDragOperationsMask());
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            DragOver?.Invoke(e.AsCefMouseEvent(MousePositionReferential), e.DragEffects.AsCefDragOperationsMask());
            _control.Cursor = _currentDragCursor;
        }

        private void OnDragLeave(object sender, RoutedEventArgs e)
        {
            DragLeave?.Invoke();
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            _previousCursor = _control.Cursor;
            DragEnter?.Invoke(e.AsCefMouseEvent(MousePositionReferential), e.GetDragData(), e.DragEffects.AsCefDragOperationsMask());
        }

        private void OnPointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            MouseWheelChanged?.Invoke(e.AsCefMouseEvent(MousePositionReferential), (int)e.Delta.X * MouseWheelDelta, (int)e.Delta.Y * MouseWheelDelta);
        }

        private void OnPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            var button = e.AsCefMouseButtonType();
            MouseButtonReleased?.Invoke(e.AsCefMouseEvent(MousePositionReferential), button);
            if (button == CefMouseButtonType.Left)
            {
                e.Pointer.Capture(null);
            }
        }

        private void OnPointerPressed(object sender, PointerPressedEventArgs e)
        {
            _lastPointerEvent = e;
            var button = e.AsCefMouseButtonType();
            MouseButtonPressed?.Invoke(this, e.AsCefMouseEvent(MousePositionReferential), button, e.ClickCount);
            if (button == CefMouseButtonType.Left)
            {
                e.Pointer.Capture(_control);
            }
        }

        private void OnPointerExited(object sender, PointerEventArgs e)
        {
            // A captured pointer has not left anything: this exit is the one Capture() manufactures, and it would reach the page as a mouse-leave mid-click.
            if (e.Pointer.Captured != null)
            {
                return;
            }

            MouseLeave?.Invoke(e.AsCefMouseEvent(MousePositionReferential));
        }

        private void OnPointerMoved(object sender, PointerEventArgs e)
        {
            MouseMoved?.Invoke(e.AsCefMouseEvent(MousePositionReferential));
        }

        private void OnLostFocus(object sender, FocusChangedEventArgs e)
        {
            LostFocus?.Invoke();
        }

        private void OnDetachedFromVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            _lastPointerEvent = null;
            _previousCursor = null;
            _currentDragCursor = null;
            _windowStateChangedObservable?.Dispose();

            if (_topLevel != null)
            {
                _topLevel.ScalingChanged -= OnScalingChanged;
                _topLevel = null;
            }

            VisibilityChanged(false);
        }

        private void OnAttachedToVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            VisibilityChanged?.Invoke(true);
            if (e.RootVisual is Window newWindow)
            {
                _windowStateChangedObservable = newWindow.GetPropertyChangedObservable(Window.WindowStateProperty).Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs>(OnHostWindowStateChanged));
            }

            _topLevel = TopLevel.GetTopLevel(_control);
            if (_topLevel != null)
            {
                _topLevel.ScalingChanged += OnScalingChanged;
                UpdateDeviceScaleFactor(_topLevel.RenderScaling);
            }
        }

        private void OnScalingChanged(object sender, EventArgs e)
        {
            if (_topLevel != null)
            {
                UpdateDeviceScaleFactor(_topLevel.RenderScaling);
            }
        }

        private void UpdateDeviceScaleFactor(double renderScaling)
        {
            if (renderScaling == RenderSurface.DeviceScaleFactor)
            {
                return;
            }

            RenderSurface.DeviceScaleFactor = (float)renderScaling;
            ScreenInfoChanged?.Invoke(RenderSurface.DeviceScaleFactor);
        }

        private void OnHostWindowStateChanged(AvaloniaPropertyChangedEventArgs e)
        {
            switch ((WindowState)e.NewValue)
            {
                case WindowState.Normal:
                case WindowState.Maximized:
                    VisibilityChanged?.Invoke(_control.IsEffectivelyVisible);
                    break;

                case WindowState.Minimized:
                    VisibilityChanged?.Invoke(false);
                    break;
            }
        }

        protected virtual Visual MousePositionReferential => _control;

        public CefPoint PointToScreen(CefPoint point, float deviceScaleFactor)
        {
            var screenCoordinates = _control.PointToScreen(new Point(point.X, point.Y));

            var result = new CefPoint(0, 0);
            result.X = screenCoordinates.X;
            result.Y = screenCoordinates.Y;
            return result;
        }

        public override IntPtr? GetHostViewHandle(int initialWidth, int initialHeight)
        {
            var platformHandle = GetHostWindowPlatformHandle();
            if (platformHandle is IMacOSTopLevelPlatformHandle macOSHandle)
            {
                return macOSHandle.NSView;
            }
            return platformHandle?.Handle;
        }

        public override void SetTooltip(string text)
        {
            // TODO BUG: sometimes the tooltips are left hanging when the user moves the cursor over the tooltip but meanwhile
            // the tooltip is destroyed
            Dispatcher.UIThread.Post(
                () =>
                {
                    ToolTip.SetTip(_control, null);
                    if (string.IsNullOrEmpty(text) || !_control.IsPointerOver)
                    {
                        return;
                    }

                    // ToolTipService restarts ShowDelay only for a newly hovered control, and no pointer event follows this text to tell it.
                    UpdateToolTipService();
                    ToolTip.SetTip(_control, text);
                    UpdateToolTipService();
                }, DispatcherPriority.Input);
        }

        // Enabling ServiceEnabled re-runs ToolTipService.Update; TopLevel offsets a child's area twice, hence the root.
        private void UpdateToolTipService()
        {
            if (_topLevel == null)
            {
                return;
            }

            ToolTip.SetServiceEnabled(_topLevel, false);
            ToolTip.SetServiceEnabled(_topLevel, true);
            _topLevel.ClearValue(ToolTip.ServiceEnabledProperty);
        }

        public void Focus()
        {
            _control.Focus();
        }

        public async Task<CefDragOperationsMask> StartDrag(CefDragData dragData, CefDragOperationsMask allowedOps, int x, int y)
        {
            var lastPointerEvent = this._lastPointerEvent; // story a copy, since this might be other thread
            if (lastPointerEvent != null)
            {
                // Replace the deprecated DataObject with the officially recommended DataTransfer
                var dataObject = new DataTransfer();
                if (!string.IsNullOrEmpty(dragData.FragmentText))
                {
                    var item = new DataTransferItem();
                    item.SetText(dragData.FragmentText); 
                    dataObject.Add(item);
                }

                // Use asynchronous DoDragDropAsync to trigger the OS's native drag-and-drop action
                var result = await Dispatcher.UIThread.InvokeAsync(() =>
                    DragDrop.DoDragDropAsync(lastPointerEvent, dataObject, allowedOps.AsDragDropEffects()));

                this._lastPointerEvent = null;
                _previousCursor = null;
                _currentDragCursor = null;
                return result.AsCefDragOperationsMask();
            }
            return CefDragOperationsMask.None;
        }

        public void UpdateDragCursor(CefDragOperationsMask allowedOps)
        {
            _currentDragCursor = CursorsProvider.GetCursorFromCefType(allowedOps);
        }

        public override bool SetCursor(IntPtr cursorHandle, CefCursorType cursorType)
        {
            var cursor = CursorsProvider.GetCursorFromCefType(cursorType);
            Dispatcher.UIThread.Post(() => _control.Cursor = cursor);
            return true;
        }

        /// <summary>
        /// Create an image that is used to render the browser frame and popups
        /// </summary>
        /// <returns></returns>
        private static Image CreateImage()
        {
            return new Image()
            {
                Focusable = false,
                Stretch = Stretch.Fill,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
            };
        }
    }
}
