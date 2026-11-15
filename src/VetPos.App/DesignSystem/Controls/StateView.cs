using System.Windows;
using System.Windows.Controls;

namespace VetPos.App.DesignSystem.Controls;

public enum StateTone
{
    Empty,
    Loading,
    Error,
}

/// <summary>What a screen shows instead of nothing: an empty list, loading, or a problem.</summary>
public sealed class StateView : Control
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(StateView), new PropertyMetadata(""));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(StateView));
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(nameof(Message), typeof(string), typeof(StateView));
    public static readonly DependencyProperty ActionProperty = DependencyProperty.Register(nameof(Action), typeof(object), typeof(StateView));
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(nameof(Tone), typeof(StateTone), typeof(StateView), new PropertyMetadata(StateTone.Empty));

    static StateView() => DefaultStyleKeyProperty.OverrideMetadata(typeof(StateView), new FrameworkPropertyMetadata(typeof(StateView)));

    public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Message { get => (string?)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public object? Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }
    public StateTone Tone { get => (StateTone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
}
