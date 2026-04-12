using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Vidvix.Views.Controls;

public sealed partial class DualThumbRangeSelector : UserControl
{
    private const double ThumbSize = 18d;
    private bool _isCoercing;

    public DualThumbRangeSelector()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public event EventHandler? SelectionChanged;

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(double),
        typeof(DualThumbRangeSelector),
        new PropertyMetadata(0d, OnRangePropertyChanged));

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(DualThumbRangeSelector),
        new PropertyMetadata(1d, OnRangePropertyChanged));

    public double StartValue
    {
        get => (double)GetValue(StartValueProperty);
        set => SetValue(StartValueProperty, value);
    }

    public static readonly DependencyProperty StartValueProperty = DependencyProperty.Register(
        nameof(StartValue),
        typeof(double),
        typeof(DualThumbRangeSelector),
        new PropertyMetadata(0d, OnRangePropertyChanged));

    public double EndValue
    {
        get => (double)GetValue(EndValueProperty);
        set => SetValue(EndValueProperty, value);
    }

    public static readonly DependencyProperty EndValueProperty = DependencyProperty.Register(
        nameof(EndValue),
        typeof(double),
        typeof(DualThumbRangeSelector),
        new PropertyMetadata(1d, OnRangePropertyChanged));

    public double MinimumRange
    {
        get => (double)GetValue(MinimumRangeProperty);
        set => SetValue(MinimumRangeProperty, value);
    }

    public static readonly DependencyProperty MinimumRangeProperty = DependencyProperty.Register(
        nameof(MinimumRange),
        typeof(double),
        typeof(DualThumbRangeSelector),
        new PropertyMetadata(1d, OnRangePropertyChanged));

    private static void OnRangePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DualThumbRangeSelector)d;
        control.CoerceValues();
        control.UpdateVisuals();
        control.SelectionChanged?.Invoke(control, EventArgs.Empty);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CoerceValues();
        UpdateVisuals();
    }

    private void OnLayoutRootSizeChanged(object sender, SizeChangedEventArgs e) => UpdateVisuals();

    private void OnStartThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!IsEnabled)
        {
            return;
        }

        StartValue += ConvertPixelDeltaToValueDelta(e.HorizontalChange);
    }

    private void OnEndThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!IsEnabled)
        {
            return;
        }

        EndValue += ConvertPixelDeltaToValueDelta(e.HorizontalChange);
    }

    private void CoerceValues()
    {
        if (_isCoercing)
        {
            return;
        }

        _isCoercing = true;
        try
        {
            var minimum = Minimum;
            var maximum = Maximum < minimum ? minimum : Maximum;
            var minimumRange = Math.Max(0d, MinimumRange);
            var start = Clamp(StartValue, minimum, Math.Max(minimum, EndValue - minimumRange));
            var end = Clamp(EndValue, Math.Min(maximum, start + minimumRange), maximum);

            if (!AreClose(start, StartValue))
            {
                SetValue(StartValueProperty, start);
            }

            if (!AreClose(end, EndValue))
            {
                SetValue(EndValueProperty, end);
            }
        }
        finally
        {
            _isCoercing = false;
        }
    }

    private void UpdateVisuals()
    {
        var usableWidth = Math.Max(0d, LayoutRoot.ActualWidth - ThumbSize);
        var trackLeft = ThumbSize / 2d;
        TrackBackground.Width = usableWidth;
        Canvas.SetLeft(TrackBackground, trackLeft);

        var minimum = Minimum;
        var maximum = Maximum <= minimum ? minimum + 1d : Maximum;
        var startRatio = (StartValue - minimum) / (maximum - minimum);
        var endRatio = (EndValue - minimum) / (maximum - minimum);
        var startCenter = trackLeft + Math.Clamp(startRatio, 0d, 1d) * usableWidth;
        var endCenter = trackLeft + Math.Clamp(endRatio, 0d, 1d) * usableWidth;

        Canvas.SetLeft(StartThumb, startCenter - ThumbSize / 2d);
        Canvas.SetLeft(EndThumb, endCenter - ThumbSize / 2d);
        Canvas.SetLeft(SelectedRangeBar, startCenter);
        SelectedRangeBar.Width = Math.Max(0d, endCenter - startCenter);
    }

    private double ConvertPixelDeltaToValueDelta(double pixelDelta)
    {
        var usableWidth = Math.Max(1d, LayoutRoot.ActualWidth - ThumbSize);
        var range = Math.Max(0d, Maximum - Minimum);
        return pixelDelta / usableWidth * range;
    }

    private static double Clamp(double value, double minimum, double maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;

    private static bool AreClose(double left, double right) => Math.Abs(left - right) < 0.001d;
}
