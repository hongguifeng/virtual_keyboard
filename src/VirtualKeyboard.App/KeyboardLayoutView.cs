using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VirtualKeyboard.Core.Configuration;
using VirtualKeyboard.Core.Layouts;

namespace VirtualKeyboard.App;

public sealed class KeyInvokedEventArgs(KeyViewModel key, bool isRepeat = false) : EventArgs
{
    public KeyViewModel Key { get; } = key ?? throw new ArgumentNullException(nameof(key));

    public bool IsRepeat { get; } = isRepeat;
}

internal sealed class KeyButtonInvokedEventArgs(bool isRepeat) : EventArgs
{
    internal bool IsRepeat { get; } = isRepeat;
}

/// <summary>Builds non-focusable weighted key rows from an immutable layout view model.</summary>
public sealed class KeyboardLayoutView : Grid
{
    public const double MinimumKeyWidth = 36;
    public const double MinimumKeyHeight = 36;

    public KeyboardLayoutView()
    {
        Focusable = false;
        KeyboardNavigation.SetIsTabStop(this, false);
    }

    public event EventHandler<KeyInvokedEventArgs>? KeyInvoked;

    public KeyboardLayoutViewModel? Layout { get; private set; }

    public void UpdateState(KeyboardControllerState state)
    {
        foreach (NonFocusableKeyButton button in DescendantButtons(this))
        {
            bool active = button.Key.Action.Type == LayoutActionTypes.Modifier && button.Key.Action.Modifier?.ToLowerInvariant() switch
            {
                "shift" => state.ShiftLatched,
                "control" => state.ControlLatched,
                "alt" => state.AltLatched,
                "windows" or "win" => state.WindowsLatched,
                "fn" => state.FunctionLayerActive,
                "capslock" => state.IsCapsLockKnown && state.IsCapsLockOn,
                _ => false,
            };
            button.SetModifierActive(active);
            if (button.Key.Action.Type == LayoutActionTypes.Key && button.Key.Action.FnVirtualKey is not null)
            {
                button.Content = state.FunctionLayerActive ? button.Key.Action.FnVirtualKey : button.Key.Label;
            }
        }
    }

    public void LoadLayout(KeyboardLayoutViewModel layout, bool passwordTarget = false)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Layout = layout;
        Children.Clear();
        RowDefinitions.Clear();

        for (int rowIndex = 0; rowIndex < layout.Rows.Count; rowIndex++)
        {
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = MinimumKeyHeight });
            Grid row = CreateRow(layout.Rows[rowIndex], passwordTarget);
            SetRow(row, rowIndex);
            Children.Add(row);
        }
    }

    private Grid CreateRow(KeyboardRowViewModel rowModel, bool passwordTarget)
    {
        var row = new Grid { Focusable = false };
        IReadOnlyList<KeyViewModel> keys = passwordTarget
            ? rowModel.Keys.Where(static key => key.SafeForPassword && PasswordActionPolicy.Check(key.Action).IsAllowed).ToArray()
            : rowModel.Keys;
        for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
        {
            KeyViewModel key = keys[keyIndex];
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(key.Width, GridUnitType.Star),
            });
            var button = new NonFocusableKeyButton(key)
            {
                Content = key.Label,
                Margin = new Thickness(2),
                MinHeight = KeyboardLayoutView.MinimumKeyHeight,
                FontSize = key.Label.Contains('\n', StringComparison.Ordinal) ? 12 : 16,
            };
            AutomationProperties.SetAutomationId(button, key.Id == "key.a" ? "KeyAButton" : key.Id);
            button.Invoked += OnButtonInvoked;
            SetColumn(button, keyIndex);
            row.Children.Add(button);
        }
        return row;
    }

    private void OnButtonInvoked(object? sender, KeyButtonInvokedEventArgs e)
    {
        var button = (NonFocusableKeyButton)sender!;
        KeyInvoked?.Invoke(this, new KeyInvokedEventArgs(button.Key, e.IsRepeat));
    }

    private static IEnumerable<NonFocusableKeyButton> DescendantButtons(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is NonFocusableKeyButton button)
            {
                yield return button;
            }
            foreach (NonFocusableKeyButton nested in DescendantButtons(child))
            {
                yield return nested;
            }
        }
    }
}

/// <summary>Hosts configured actions in right-side columns aligned to the five keyboard rows.</summary>
public sealed class CustomKeyColumnView : Grid
{
    public const int KeysPerColumn = 5;

    public CustomKeyColumnView()
    {
        Focusable = false;
        KeyboardNavigation.SetIsTabStop(this, false);
    }

    public event EventHandler<KeyInvokedEventArgs>? KeyInvoked;

    public int ColumnCount => ColumnDefinitions.Count;

    public void LoadKeys(IReadOnlyList<CustomKeyConfiguration> keys, bool passwordTarget)
    {
        ArgumentNullException.ThrowIfNull(keys);
        Children.Clear();
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();
        Visibility = !passwordTarget && keys.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (passwordTarget) return;

        for (int row = 0; row < KeysPerColumn && keys.Count > 0; row++)
        {
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = KeyboardLayoutView.MinimumKeyHeight });
        }
        int columnCount = (keys.Count + KeysPerColumn - 1) / KeysPerColumn;
        for (int column = 0; column < columnCount; column++)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (int index = 0; index < keys.Count; index++)
        {
            CustomKeyConfiguration configured = keys[index];
            var key = new KeyViewModel(
                $"key.custom.{index}", configured.Label, 1, false, configured.ToLayoutAction());
            var button = new NonFocusableKeyButton(key)
            {
                Content = configured.Label,
                Margin = new Thickness(2),
                MinHeight = KeyboardLayoutView.MinimumKeyHeight,
                FontSize = 14,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            AutomationProperties.SetAutomationId(button, $"CustomKey{index}");
            button.Invoked += (_, args) => KeyInvoked?.Invoke(this, new KeyInvokedEventArgs(button.Key, args.IsRepeat));
            SetRow(button, index % KeysPerColumn);
            SetColumn(button, index / KeysPerColumn);
            Children.Add(button);
        }
    }
}

internal sealed class NonFocusableKeyButton : Button
{
    private readonly KeyGestureController _gesture = new();
    private readonly AcceleratingKeyRepeatController? _repeat;
    private readonly DispatcherTimer? _repeatTimer;
    private double _restingOpacity = 1;

    internal NonFocusableKeyButton(KeyViewModel key)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Focusable = false;
        IsTabStop = false;
        ClickMode = ClickMode.Release;
        if (key.Action.Type == LayoutActionTypes.Key &&
            key.Action.VirtualKey?.Equals("Backspace", StringComparison.OrdinalIgnoreCase) == true)
        {
            _repeat = new AcceleratingKeyRepeatController();
            _repeatTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher);
            _repeatTimer.Tick += OnRepeatTimerTick;
            Unloaded += (_, _) => CancelGesture();
            IsVisibleChanged += (_, args) =>
            {
                if (args.NewValue is false) CancelGesture();
            };
        }
    }

    internal event EventHandler<KeyButtonInvokedEventArgs>? Invoked;

    internal KeyViewModel Key { get; }

    internal bool IsGesturePressed => _gesture.IsPressed;

    internal bool IsModifierActive { get; private set; }

    internal void SetModifierActive(bool active)
    {
        IsModifierActive = active;
        if (active)
        {
            Background = new SolidColorBrush(Color.FromRgb(0x28, 0x78, 0xF0));
            Foreground = Brushes.White;
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x75, 0xAE, 0xFF));
            BorderThickness = new Thickness(2);
            FontWeight = FontWeights.SemiBold;
        }
        else
        {
            ClearValue(BackgroundProperty);
            ClearValue(ForegroundProperty);
            ClearValue(BorderBrushProperty);
            ClearValue(BorderThicknessProperty);
            ClearValue(FontWeightProperty);
        }
        if (!_gesture.IsPressed)
        {
            Opacity = 1;
        }
    }

    internal bool BeginGestureForTest()
    {
        return BeginGesture();
    }

    internal bool EndGestureForTest(bool isInside)
    {
        bool invoke = EndGesture(isInside);
        if (invoke)
        {
            Invoked?.Invoke(this, new KeyButtonInvokedEventArgs(isRepeat: false));
        }
        return invoke;
    }

    internal void CancelGestureForTest() => CancelGesture();

    internal bool RepeatTickForTest() => RepeatTick();

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (BeginGesture() && CaptureMouse())
        {
            e.Handled = true;
        }
        else
        {
            CancelGesture();
        }
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (!IsMouseCaptured)
        {
            return;
        }
        bool invoke = EndGesture(IsMouseOver);
        ReleaseMouseCapture();
        if (invoke)
        {
            Invoked?.Invoke(this, new KeyButtonInvokedEventArgs(isRepeat: false));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_repeat is not null && IsMouseCaptured && !IsMouseOver)
        {
            CancelGesture();
            ReleaseMouseCapture();
        }
    }

    protected override void OnTouchDown(TouchEventArgs e)
    {
        base.OnTouchDown(e);
        e.Handled = true;
        if (BeginGesture() && e.TouchDevice.Capture(this))
        {
            return;
        }
        e.TouchDevice.Capture(null);
        CancelGesture();
    }

    protected override void OnTouchUp(TouchEventArgs e)
    {
        base.OnTouchUp(e);
        e.Handled = true;
        if (e.TouchDevice.Captured != this)
        {
            CancelGesture();
            return;
        }
        Point position = e.GetTouchPoint(this).Position;
        bool invoke = EndGesture(position.X >= 0 && position.Y >= 0 && position.X <= ActualWidth && position.Y <= ActualHeight);
        e.TouchDevice.Capture(null);
        if (invoke)
        {
            Invoked?.Invoke(this, new KeyButtonInvokedEventArgs(isRepeat: false));
        }
    }

    protected override void OnTouchMove(TouchEventArgs e)
    {
        base.OnTouchMove(e);
        if (e.TouchDevice.Captured == this)
        {
            e.Handled = true;
            Point position = e.GetTouchPoint(this).Position;
            if (_repeat is not null &&
                (position.X < 0 || position.Y < 0 || position.X > ActualWidth || position.Y > ActualHeight))
            {
                CancelGesture();
                e.TouchDevice.Capture(null);
            }
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        CancelGesture();
        base.OnLostMouseCapture(e);
    }

    protected override void OnLostTouchCapture(TouchEventArgs e)
    {
        CancelGesture();
        base.OnLostTouchCapture(e);
    }

    private bool BeginGesture()
    {
        if (!_gesture.Begin())
        {
            return false;
        }
        _restingOpacity = Opacity;
        Opacity = 0.65;
        if (_repeat is not null && _repeatTimer is not null)
        {
            _repeatTimer.Interval = _repeat.Begin();
            _repeatTimer.Start();
        }
        return true;
    }

    private bool EndGesture(bool isInside)
    {
        bool releasedInside = _gesture.Release(isInside);
        _repeatTimer?.Stop();
        bool invoke = _repeat?.Release(releasedInside) ?? releasedInside;
        Opacity = _restingOpacity;
        return invoke;
    }

    private void CancelGesture()
    {
        _repeatTimer?.Stop();
        _repeat?.Cancel();
        if (_gesture.Cancel())
        {
            Opacity = _restingOpacity;
        }
    }

    private void OnRepeatTimerTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        RepeatTick();
    }

    private bool RepeatTick()
    {
        if (_repeat is null || _repeatTimer is null || !_gesture.IsPressed)
        {
            _repeatTimer?.Stop();
            _repeat?.Cancel();
            return false;
        }

        KeyRepeatTick tick = _repeat.Tick();
        if (!tick.ShouldInvoke) return false;
        _repeatTimer.Interval = tick.NextDelay;
        Invoked?.Invoke(this, new KeyButtonInvokedEventArgs(isRepeat: true));
        return true;
    }
}
