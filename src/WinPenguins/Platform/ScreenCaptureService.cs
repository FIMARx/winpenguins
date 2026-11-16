using System.Drawing;
using System.Drawing.Imaging;

namespace WinPenguins.Platform;

public static class ScreenCaptureService
{
    public static void CaptureAndSaveInteractive(IWin32Window? owner = null)
    {
        Rectangle bounds = SystemInformation.VirtualScreen;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(
                bounds.Left,
                bounds.Top,
                0,
                0,
                bounds.Size,
                CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Save WinPenguins Screenshot",
            Filter = "PNG Image (*.png)|*.png|Bitmap Image (*.bmp)|*.bmp",
            DefaultExt = "png",
            FileName = $"WinPenguins_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        };

        if (dialog.ShowDialog(owner) == DialogResult.OK)
        {
            ImageFormat fmt = Path.GetExtension(dialog.FileName)
                .Equals(".bmp", StringComparison.OrdinalIgnoreCase)
                ? ImageFormat.Bmp
                : ImageFormat.Png;
            bitmap.Save(dialog.FileName, fmt);
        }
    }
}
