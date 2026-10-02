using System;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BukaMusicDesktop.Core;

/// <summary>
/// The dedicated "no cover" placeholder, the same picture the Android app uses
/// (res/drawable-nodpi/ic_airplay.png). Songs whose metadata carries no cover
/// have to fall back to this instead of an empty box or a note glyph.
/// </summary>
public static class PlaceholderCover
{
    private static BitmapImage? _image;

    /// <summary>The placeholder as an image source (created once, on the UI thread).</summary>
    public static BitmapImage Image
    {
        get
        {
            _image ??= new BitmapImage(new Uri("ms-appx:///Assets/cover_placeholder.png"));
            return _image;
        }
    }
}
