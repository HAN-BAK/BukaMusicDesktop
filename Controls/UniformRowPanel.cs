using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace BukaMusicDesktop.Controls;

/// <summary>
/// Lays the children out as equal columns of one row, each centred in its
/// column. The equalizer needs it: ten fixed-width bands (86 + 6 DIP) need
/// 920 DIP, which is wider than the settings card, so the last slider used to
/// spill out of the card. With this panel the ten bands always share whatever
/// width the card has.
/// </summary>
public sealed class UniformRowPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        int count = Math.Max(1, Children.Count);
        double width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        double column = width > 0 ? width / count : 0;
        double height = 0;
        foreach (UIElement child in Children)
        {
            child.Measure(new Size(column > 0 ? column : double.PositiveInfinity,
                    availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int count = Math.Max(1, Children.Count);
        double column = finalSize.Width / count;
        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Arrange(new Rect(i * column, 0, column, finalSize.Height));
        }
        return finalSize;
    }
}
