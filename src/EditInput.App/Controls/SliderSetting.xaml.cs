using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EditInput.App.Controls;

/// <summary>Slider + precise numeric entry (commit with Enter or on focus loss; clamped to range).</summary>
public partial class SliderSetting : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(SliderSetting),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((SliderSetting)d).SyncBox()));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(int), typeof(SliderSetting), new PropertyMetadata(0));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(int), typeof(SliderSetting), new PropertyMetadata(100));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(SliderSetting), new PropertyMetadata(""));

    public static readonly DependencyProperty HintProperty =
        DependencyProperty.Register(nameof(Hint), typeof(string), typeof(SliderSetting), new PropertyMetadata(""));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(SliderSetting), new PropertyMetadata("ms"));

    public SliderSetting()
    {
        InitializeComponent();
        SyncBox();
    }

    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int Minimum { get => (int)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    private void SyncBox()
    {
        if (Box is not null && !Box.IsKeyboardFocused)
            Box.Text = Value.ToString(CultureInfo.CurrentCulture);
    }

    private void Commit()
    {
        var text = Box.Text.Trim().TrimEnd('%').Replace("ms", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var v))
            Value = Math.Clamp(v, Minimum, Maximum);
        Box.Text = Value.ToString(CultureInfo.CurrentCulture);
    }

    private void OnBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Commit();
                Box.SelectAll();
                e.Handled = true;
                break;
            case Key.Up:
                Value = Math.Min(Maximum, Value + 1);
                Box.Text = Value.ToString(CultureInfo.CurrentCulture);
                e.Handled = true;
                break;
            case Key.Down:
                Value = Math.Max(Minimum, Value - 1);
                Box.Text = Value.ToString(CultureInfo.CurrentCulture);
                e.Handled = true;
                break;
            case Key.Escape:
                Box.Text = Value.ToString(CultureInfo.CurrentCulture);
                e.Handled = true;
                break;
        }
    }

    private void OnBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit();
}
