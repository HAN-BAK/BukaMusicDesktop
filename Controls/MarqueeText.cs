using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace BukaMusicDesktop.Controls;

/// <summary>
/// A TextBlock that scrolls its text when it is wider than the space it was
/// given, and stays still when it fits. Long text scrolls to the left, waits,
/// and slides back - like the stock marquee on the phone app.
/// </summary>
public sealed class MarqueeText : UserControl
{
    /// <summary>Pixels per second while scrolling.</summary>
    private const double Speed = 45;
    /// <summary>Pause at both ends of the run (seconds).</summary>
    private const double Hold = 0.9;

    private static TextBlock NewText() => new()
    {
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.None,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock _first = NewText();
    private readonly TextBlock _second = NewText();
    private readonly StackPanel _strip;

    private readonly TranslateTransform _offset = new();
    /// <summary>Waits for the layout to settle before a scroll is started.</summary>
    private readonly Microsoft.UI.Xaml.DispatcherTimer _settle = new()
    {
        Interval = TimeSpan.FromMilliseconds(60),
    };
    private Storyboard? _story;
    private double _animated;
    /// <summary>Cache of the rendered text width (measuring is not free).</summary>
    private double _measuredWidth = -1;
    /// <summary>Texts already reported in the log, so long lists stay quiet.</summary>
    private static readonly System.Collections.Generic.HashSet<string> _logged = new();

    public MarqueeText()
    {
        _strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            RenderTransform = _offset,
        };
        _strip.Children.Add(_first);
        _strip.Children.Add(_second);
        _second.Visibility = Visibility.Collapsed;
        Content = _strip;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        RegisterPropertyChangedCallback(FontSizeProperty, (_, _) => OnFontChanged());
        RegisterPropertyChangedCallback(FontFamilyProperty, (_, _) => OnFontChanged());
        RegisterPropertyChangedCallback(FontWeightProperty, (_, _) => OnFontChanged());
        RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => Sync());
        Loaded += (_, _) => { Sync(); Update(); };
        SizeChanged += (_, _) => Update();
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            ApplyNow();
        };
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MarqueeText),
        new PropertyMetadata("", (sender, _) =>
        {
            var marquee = (MarqueeText)sender;
            marquee._measuredWidth = -1;
            marquee.Update();
        }));

    /// <summary>Text to show; long values start scrolling on their own.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Colour the label actually renders with (diagnostics).</summary>
    public string TextColor => _first.Foreground is SolidColorBrush brush
        ? brush.Color.ToString()
        : "none";

    /// <summary>Width the text was given by the layout (diagnostics).</summary>
    public double AvailableWidth => ActualWidth;

    /// <summary>Width the text needs (diagnostics).</summary>
    public double NeededWidth => _measuredWidth;

    /// <summary>True while the label is scrolling (diagnostics).</summary>
    public bool IsScrolling => _story != null;

    public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
        nameof(TextAlignment), typeof(TextAlignment), typeof(MarqueeText),
        new PropertyMetadata(TextAlignment.Left, (sender, _) =>
        {
            ((MarqueeText)sender).Sync();
        }));

    /// <summary>How the text sits inside the control (left / centre / right).</summary>
    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>Copies the text properties of the control onto its TextBlock.</summary>
    private void Sync()
    {
        foreach (TextBlock block in new[] { _first, _second })
        {
            block.FontSize = FontSize;
            block.FontFamily = FontFamily;
            block.FontWeight = FontWeight;
            block.TextAlignment = TextAlignment;
            // Foreground is deliberately left inherited: buttons and navigation
            // entries swap colour per state (selected / pointer over), and that
            // state colour only reaches the label through inheritance.
        }
    }

    /// <summary>Starts or stops the scrolling animation for the current size.</summary>
    private void Update()
    {
        // Text and size change several times while a page is being laid out (and
        // every language has different widths); deciding immediately made labels
        // jump for a frame. The decision is taken once the values stop moving.
        _settle.Stop();
        _settle.Start();
    }

    private void ApplyNow()
    {
        _first.Text = Text;
        _second.Text = Text;
        Sync();

        double available = ActualWidth;
        if (available <= 1)
        {
            // Not laid out yet (or hidden): try again when a size arrives.
            Stop();
            return;
        }

        double needed = MeasureRendered();
        double distance = needed - available;
        if (distance <= 1)
        {
            _strip.Width = double.NaN;
            // Not scrolling: the strip is only as wide as the text, so the
            // alignment has to be applied to it - a right-aligned element would
            // otherwise still show its text hugging the left edge.
            _strip.HorizontalAlignment = TextAlignment switch
            {
                TextAlignment.Center => HorizontalAlignment.Center,
                TextAlignment.Right => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Left,
            };
            Stop();
            return;
        }

        // Only rebuild the animation when the overflow actually changed, so a
        // running scroll is not restarted on every layout pass.
        if (Math.Abs(_animated - distance) < 0.5 && _story != null) return;
        Stop();
        _animated = distance;
        // The strip has to be as wide as the text itself, otherwise the layout
        // system clips it to the control's width and the part that scrolls into
        // view would simply not be rendered. The control then crops the window
        // itself (see ArrangeOverride).
        _strip.Width = needed;
        // While scrolling the strip starts at the left edge and slides.
        _strip.HorizontalAlignment = HorizontalAlignment.Left;
        if (_logged.Add(Text))
        {
            Core.LogBus.Info($"文本超出 {distance:0} 像素（可见 {available:0}，需要 {needed:0}），"
                             + $"开始滚动：{Shorten(Text)}");
        }

        // Left, pause, back, pause - and again.
        _story = BuildPingPong(_offset, distance);
        _story.Begin();
    }

    /// <summary>
    /// The shared "slide left, wait, slide back, wait" animation. Used by the
    /// labels and by the text box placeholders, so both scroll the same way.
    /// </summary>
    internal static Storyboard BuildPingPong(TranslateTransform offset, double distance)
    {
        // Exactly the overflow: the last character ends up on the right edge, so
        // nothing runs off the left and no blank strip opens on the right.
        double travel = distance;
        double run = travel / Speed;
        var keys = new DoubleAnimationUsingKeyFrames
        {
            EnableDependentAnimation = true,
            RepeatBehavior = RepeatBehavior.Forever,
            Duration = new Duration(TimeSpan.FromSeconds(Hold * 2 + run * 2)),
        };
        keys.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(0), Value = 0 });
        keys.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(Hold), Value = 0 });
        keys.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(Hold + run), Value = -travel });
        keys.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(Hold * 2 + run), Value = -travel });
        keys.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(Hold * 2 + run * 2), Value = 0 });
        Storyboard.SetTarget(keys, offset);
        Storyboard.SetTargetProperty(keys, "X");
        var story = new Storyboard();
        story.Children.Add(keys);
        return story;
    }

    private void Stop()
    {
        if (_story != null)
        {
            _story.Stop();
            _story = null;
        }
        _animated = 0;
        _offset.X = 0;
    }

    /// <summary>
    /// Width the text really renders with. The live TextBlock is measured with
    /// unlimited width (its parent strip is laid out with an explicit width, so
    /// this does not disturb the page), which keeps the measurement in sync with
    /// the inherited font - a detached probe drifted and reported false
    /// overflows.
    /// </summary>
    /// <summary>Font changes change the width, so the cache is dropped.</summary>
    private void OnFontChanged()
    {
        _measuredWidth = -1;
        Sync();
        Update();
    }

    private double MeasureRendered()
    {
        if (_measuredWidth >= 0) return _measuredWidth;
        _first.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _measuredWidth = _first.DesiredSize.Width;
        return _measuredWidth;
    }

    /// <summary>
    /// Wraps the label of every button in the tree into a scrolling text, so a
    /// button whose caption does not fit scrolls instead of being cut off. Runs
    /// after the page was translated, so the label is already in the right
    /// language.
    /// </summary>
    public static void Enhance(DependencyObject? root)
    {
        if (root == null) return;
        if (root is TextBox box)
        {
            // Text boxes keep their own behaviour; only the placeholder used to be
            // scrolled and that is gone, so nothing is attached here any more.
            return;
        }
        if (root is Button button && button.Content is string label && label.Length > 0)
        {
            var marquee = new MarqueeText
            {
                Text = label,
                FontSize = button.FontSize,
                FontFamily = button.FontFamily,
                FontWeight = button.FontWeight,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            button.Content = marquee;
        }
        else if (root is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton toggle
                 && toggle.Content is string toggleLabel && toggleLabel.Length > 0)
        {
            toggle.Content = new MarqueeText
            {
                Text = toggleLabel,
                FontSize = toggle.FontSize,
                FontFamily = toggle.FontFamily,
                FontWeight = toggle.FontWeight,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
        }
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            Enhance(VisualTreeHelper.GetChild(root, i));
        }
    }

    private static string Shorten(string value)
        => value.Length <= 28 ? value : value[..28] + "…";

    /// <summary>Clips the scrolling text to the control's own bounds.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        Size size = base.ArrangeOverride(finalSize);
        Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, Math.Max(0, size.Width), Math.Max(0, size.Height)),
        };
        return size;
    }
}
