using Avalonia.Media;

namespace Avalonia.Controls.DataGridTests;

/// <summary>
/// The DataGrid hides recycled containers with an empty <see cref="RectangleGeometry"/> clip instead
/// of IsVisible=false, because toggling IsVisible invalidates measure and forces a full re-measure of
/// the row template on every recycle/re-insert cycle. Tests must therefore ask this helper rather than
/// reading <see cref="Visual.IsVisible"/> when they want to know whether a container is on screen.
/// </summary>
internal static class RecycledContainer
{
    public static bool IsHidden(Visual element) =>
        element.Clip is RectangleGeometry { Rect.Width: <= 0 }
        || element.Clip is RectangleGeometry { Rect.Height: <= 0 };

    public static bool IsShown(Visual element) => !IsHidden(element);
}
