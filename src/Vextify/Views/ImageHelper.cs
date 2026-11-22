using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vextify.Core;

namespace Vextify.Views;

/// <summary>Loads preview images at the size they are shown, so a grid of previews doesn't hold full-size bitmaps in memory.</summary>
public static class ImageHelper
{
    public static ImageSource? Load(string? url, int decodeWidth, System.Action? onFail = null)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.DecodePixelWidth = decodeWidth;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.EndInit();
            if (bmp.IsDownloading && onFail != null) bmp.DownloadFailed += (_, _) => onFail();
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Write("Bild laden: " + ex.Message);
            onFail?.Invoke();
            return null;
        }
    }
}
