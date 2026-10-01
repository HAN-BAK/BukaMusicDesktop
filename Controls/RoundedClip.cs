using System;
using System.Runtime.CompilerServices;
using System.Numerics;
using BukaMusicDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace BukaMusicDesktop.Controls;

/// <summary>
/// Rounds an element's own content off with a composition geometric clip.
///
/// <c>Border.CornerRadius</c> only rounds the border's own background, so an
/// <c>Image</c> inside a rounded border keeps its square corners: the picture
/// pokes past the rounded frame and the dark background shows through as a
/// sliver along the arcs. Clipping the element itself fixes both.
/// </summary>
public static class RoundedClip
{
    private static readonly ConditionalWeakTable<FrameworkElement, object> Attached = new();

    public static void Attach(FrameworkElement element, Func<double> radius)
    {
        // Templates re-fire Loaded on every realized container; attaching twice
        // would only add duplicate handlers, so remember what is already done.
        if (Attached.TryGetValue(element, out _)) return;
        Attached.Add(element, new object());

        void Apply()
        {
            double width = element.ActualWidth;
            double height = element.ActualHeight;
            if (width < 1 || height < 1) return;
            double cornerRadius = Math.Max(0.0, radius());
            if (cornerRadius <= 0.01) return;
            try
            {
                var visual = ElementCompositionPreview.GetElementVisual(element);
                var compositor = visual.Compositor;
                var geometry = compositor.CreateRoundedRectangleGeometry();
                geometry.Size = new Vector2((float)width, (float)height);
                geometry.CornerRadius = new Vector2((float)cornerRadius, (float)cornerRadius);
                visual.Clip = compositor.CreateGeometricClip(geometry);
            }
            catch (Exception ex)
            {
                LogBus.Warn("圆角裁切失败：" + ex.Message);
            }
        }

        element.Loaded += (_, _) => Apply();
        element.SizeChanged += (_, _) => Apply();
    }
}
