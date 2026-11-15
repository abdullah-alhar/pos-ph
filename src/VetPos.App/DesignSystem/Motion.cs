using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VetPos.App.DesignSystem;

/// <summary>
/// The motion tokens and the few animations the app uses. Rules:
/// keyboard actions never animate (they happen hundreds of times a day); dialogs and toasts
/// use a short ease-out (enter 180 ms, exit 120 ms — leaving is faster than arriving);
/// nothing scales from zero; when Windows animations are off, things appear at once.
/// </summary>
public static class Motion
{
    public static readonly Duration Press = TimeSpan.FromMilliseconds(100);
    public static readonly Duration Enter = TimeSpan.FromMilliseconds(180);
    public static readonly Duration Exit = TimeSpan.FromMilliseconds(120);
    public static readonly Duration FlashFade = TimeSpan.FromMilliseconds(900);

    /// <summary>Strong ease-out: starts fast so the UI feels instant, settles softly.</summary>
    public static readonly KeySpline EaseOut = Freeze(new KeySpline(0.23, 1, 0.32, 1));

    /// <summary>Windows "Show animations in Windows" setting.</summary>
    /// <summary>Off when Windows animations are off, or when there is no graphics card (virtual machines,
    /// remote desktop): software drawing makes animation stutter, so changes just appear.</summary>
    public static bool Enabled => SystemParameters.ClientAreaAnimation && (System.Windows.Media.RenderCapability.Tier >> 16) >= 2;

    private static KeySpline Freeze(KeySpline k)
    {
        k.Freeze();
        return k;
    }

    private static DoubleAnimationUsingKeyFrames Spline(double from, double to, Duration d) => new()
    {
        KeyFrames =
        {
            new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)),
            new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(d.TimeSpan), EaseOut),
        },
    };

    private static (ScaleTransform Scale, TranslateTransform Move) Transforms(UIElement e)
    {
        if (e.RenderTransform is TransformGroup { Children: [ScaleTransform s, TranslateTransform t] }) return (s, t);
        var scale = new ScaleTransform(1, 1);
        var move = new TranslateTransform();
        e.RenderTransform = new TransformGroup { Children = { scale, move } };
        e.RenderTransformOrigin = new Point(0.5, 0.5);
        return (scale, move);
    }

    /// <summary>Dialog / sheet arriving: fade in and grow from 97 % (never from 0).</summary>
    public static void FadeScaleIn(UIElement e)
    {
        e.Opacity = 1;
        if (!Enabled) return;
        var (scale, _) = Transforms(e);
        e.BeginAnimation(UIElement.OpacityProperty, Spline(0, 1, Enter));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Spline(0.97, 1, Enter));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Spline(0.97, 1, Enter));
    }

    /// <summary>Dialog / sheet leaving, the way it came. Calls <paramref name="done"/> at the end.</summary>
    public static void FadeScaleOut(UIElement e, Action done)
    {
        if (!Enabled)
        {
            done();
            return;
        }
        var (scale, _) = Transforms(e);
        var fade = Spline(e.Opacity, 0, Exit);
        fade.Completed += (_, _) => done();
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Spline(1, 0.98, Exit));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Spline(1, 0.98, Exit));
        e.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Plain fade (the scrim behind a dialog).</summary>
    public static void Fade(UIElement e, double to, Action? done = null)
    {
        if (!Enabled)
        {
            e.BeginAnimation(UIElement.OpacityProperty, null);
            e.Opacity = to;
            done?.Invoke();
            return;
        }
        var anim = Spline(e.Opacity, to, to > e.Opacity ? Enter : Exit);
        if (done is not null) anim.Completed += (_, _) => done();
        e.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    /// <summary>Toast arriving: slides 8 px up from where it will rest, and fades in.</summary>
    public static void SlideIn(UIElement e)
    {
        e.Opacity = 1;
        if (!Enabled) return;
        var (_, move) = Transforms(e);
        e.BeginAnimation(UIElement.OpacityProperty, Spline(0, 1, Enter));
        move.BeginAnimation(TranslateTransform.YProperty, Spline(8, 0, Enter));
    }

    /// <summary>Toast leaving the way it came (down and out).</summary>
    public static void SlideOut(UIElement e, Action done)
    {
        if (!Enabled)
        {
            done();
            return;
        }
        var (_, move) = Transforms(e);
        var fade = Spline(e.Opacity, 0, Exit);
        fade.Completed += (_, _) => done();
        move.BeginAnimation(TranslateTransform.YProperty, Spline(0, 8, Exit));
        e.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Invalid input: three quick 4 px shakes (240 ms). Kept even with animations off, but shorter.</summary>
    public static void Shake(FrameworkElement e)
    {
        var (_, move) = Transforms(e);
        var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(Enabled ? 240 : 120) };
        double[] steps = Enabled ? [0, -4, 4, -4, 4, -2, 0] : [0, -2, 2, 0];
        for (var i = 0; i < steps.Length; i++)
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(steps[i], KeyTime.FromPercent((double)i / (steps.Length - 1))));
        move.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    /// <summary>
    /// A row that just changed glows in the accent tint and fades back (900 ms). Colour only —
    /// no movement — so it never slows down scanning.
    /// </summary>
    public static void Flash(FrameworkElement e)
    {
        if (e.TryFindResource("Color.AccentSoft") is not Color tint) return;
        var brush = new SolidColorBrush(tint);
        switch (e)
        {
            case Panel p: p.Background = brush; break;
            case Border b: b.Background = brush; break;
            default: return;
        }
        if (!Enabled)
        {
            brush.Opacity = 0;
            return;
        }
        brush.BeginAnimation(Brush.OpacityProperty, new DoubleAnimation(1, 0, FlashFade)
        {
            BeginTime = TimeSpan.FromMilliseconds(250),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        });
    }
}
