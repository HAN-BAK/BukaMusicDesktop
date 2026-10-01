using System;
using System.Collections.Generic;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI;

namespace BukaMusicDesktop.Controls;

/// <summary>
/// Typography backend for the Sonnet stage: the desktop equivalent of the
/// Android SonnetStageView's serif Paint (bundled Noto Serif SC on Android).
/// Text is measured and drawn through Win2D text layouts so the engine can
/// place every word by its baseline, exactly like Canvas.drawText does.
/// </summary>
public sealed class SonnetText : ISonnetTextMetrics, IDisposable
{
    // Folia resolves a serif CJK face; Noto Serif SC is what ships inside the
    // Android app, so it is preferred here as well.
    private static readonly string[] FamilyPreference =
    {
        "Noto Serif SC", "Source Han Serif SC", "Noto Serif CJK SC",
        "Noto Serif JP", "Source Han Serif JP", "SimSun", "NSimSun",
        "STSong", "Songti SC", "MS Mincho", "Yu Mincho", "SimSun-ExtB",
    };

    private readonly CanvasDevice _device;
    private readonly string _family;
    private readonly Dictionary<int, CanvasTextFormat> _formats = new();
    private readonly Dictionary<(string Text, int Size), CanvasTextLayout> _layouts = new();
    private float _ascentRatio;
    private float _descentRatio;

    public SonnetText(CanvasDevice device)
    {
        _device = device;
        _family = ResolveFamily();
        MeasureFontRatios();
    }

    public string Family => _family;

    private static string ResolveFamily()
    {
        try
        {
            var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string family in CanvasTextFormat.GetSystemFontFamilies())
            {
                available.Add(family);
            }
            foreach (string candidate in FamilyPreference)
            {
                if (available.Contains(candidate)) return candidate;
            }
        }
        catch (Exception)
        {
            // Fall through to the generic serif family.
        }
        return "Georgia";
    }

    private void MeasureFontRatios()
    {
        // Android's Paint.getFontMetrics(); kept as a ratio of the font size so
        // the vertical advance of vertical compositions matches.
        const float reference = 100f;
        var layout = new CanvasTextLayout(_device, "字Ag", Format(reference), 4000f, 1000f);
        CanvasLineMetrics metrics = layout.LineMetrics[0];
        // CanvasLineMetrics exposes the line box, so the ascent is the distance
        // from the line top to the baseline and the descent is the rest of it.
        _ascentRatio = -metrics.Baseline / reference;
        _descentRatio = (metrics.Height - metrics.Baseline) / reference;
        if (_descentRatio - _ascentRatio < 1.05f)
        {
            // Some faces report tight metrics; the Android layout always sees at
            // least a 1.30 em line for CJK.
            _ascentRatio = -1.05f;
            _descentRatio = 0.30f;
        }
    }

    public CanvasTextFormat Format(float fontSize)
    {
        int key = (int)Math.Round(fontSize * 10);
        if (_formats.TryGetValue(key, out CanvasTextFormat? cached)) return cached;
        var format = new CanvasTextFormat
        {
            FontFamily = _family,
            FontSize = key / 10f,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            WordWrapping = CanvasWordWrapping.NoWrap,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top,
            TrimmingGranularity = CanvasTextTrimmingGranularity.None,
        };
        _formats[key] = format;
        return format;
    }

    public CanvasTextLayout LayoutFor(string text, float fontSize)
    {
        int key = (int)Math.Round(fontSize * 10);
        var cacheKey = (text, key);
        if (_layouts.TryGetValue(cacheKey, out CanvasTextLayout? cached)) return cached;
        if (_layouts.Count > 4000) _layouts.Clear();
        var layout = new CanvasTextLayout(_device, text, Format(key / 10f), 8000f, 4000f);
        _layouts[cacheKey] = layout;
        return layout;
    }

    public float MeasureText(string text, float fontSize)
    {
        if (string.IsNullOrEmpty(text)) return fontSize * 0.5f;
        Rect bounds = LayoutFor(text, fontSize).LayoutBounds;
        return Math.Max(fontSize * 0.2f, (float)bounds.Width + (float)bounds.Left);
    }

    public float Ascent(float fontSize) => _ascentRatio * fontSize;
    public float Descent(float fontSize) => _descentRatio * fontSize;

    /// <summary>
    /// Draws text centred on <paramref name="centerX"/> with its baseline at
    /// <paramref name="baselineY"/> - the same contract Android's
    /// Paint.Align.CENTER + canvas.drawText(text, x, baseline) gives.
    /// </summary>
    public void DrawCentered(CanvasDrawingSession session, string text, float centerX,
                             float baselineY, float fontSize, Color color)
    {
        if (string.IsNullOrEmpty(text)) return;
        CanvasTextLayout layout = LayoutFor(text, fontSize);
        Rect bounds = layout.LayoutBounds;
        float baseline = layout.LineMetrics.Length > 0 ? layout.LineMetrics[0].Baseline : fontSize;
        float x = centerX - (float)bounds.Left - (float)bounds.Width * 0.5f;
        float y = baselineY - baseline;
        session.DrawTextLayout(layout, new System.Numerics.Vector2(x, y), color);
    }

    /// <summary>Left aligned variant used by the HUD style decorations.</summary>
    public void DrawAt(CanvasDrawingSession session, string text, float left, float baselineY,
                       float fontSize, Color color)
    {
        if (string.IsNullOrEmpty(text)) return;
        CanvasTextLayout layout = LayoutFor(text, fontSize);
        float baseline = layout.LineMetrics.Length > 0 ? layout.LineMetrics[0].Baseline : fontSize;
        session.DrawTextLayout(layout, new System.Numerics.Vector2(left, baselineY - baseline), color);
    }

    public float BaselineOffset(float fontSize)
    {
        CanvasTextLayout layout = LayoutFor("字", fontSize);
        return layout.LineMetrics.Length > 0 ? layout.LineMetrics[0].Baseline : fontSize;
    }

    public void Dispose()
    {
        foreach (CanvasTextLayout layout in _layouts.Values) layout.Dispose();
        _layouts.Clear();
        foreach (CanvasTextFormat format in _formats.Values) format.Dispose();
        _formats.Clear();
    }
}
