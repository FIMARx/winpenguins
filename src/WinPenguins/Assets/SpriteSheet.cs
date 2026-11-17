using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using WinPenguins.Engine;

namespace WinPenguins.Assets;

public sealed class SpriteFrame
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>
    /// Premultiplied 32bpp ARGB pixels (little-endian BGRA in memory: 0xAARRGGBB).
    /// </summary>
    public uint[] Pixels { get; }

    public SpriteFrame(int width, int height, uint[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }
}

public sealed class SpriteSheet
{
    private readonly SpriteFrame[,] _frames;

    public AnimationSpec Spec { get; }
    public int SheetWidth { get; }
    public int SheetHeight { get; }
    public uint[] SheetPixels { get; }
    public int FrameWidth { get; }
    public int FrameHeight { get; }
    public int TextureFrameWidth => Math.Max(1, SheetWidth / Math.Max(1, Spec.FrameCount));
    public int TextureFrameHeight => Math.Max(1, SheetHeight / Math.Max(1, Spec.DirectionCount));

    private SpriteSheet(
        AnimationSpec spec,
        int sheetWidth,
        int sheetHeight,
        uint[] sheetPixels,
        int frameWidth,
        int frameHeight,
        SpriteFrame[,] frames)
    {
        Spec = spec;
        SheetWidth = sheetWidth;
        SheetHeight = sheetHeight;
        SheetPixels = sheetPixels;
        FrameWidth = frameWidth;
        FrameHeight = frameHeight;
        _frames = frames;
    }

    public SpriteFrame GetFrame(FacingDirection direction, int frameIndex)
    {
        int dirIdx = Spec.DirectionCount > 1 ? Math.Clamp((int)direction, 0, Spec.DirectionCount - 1) : 0;
        int frmIdx = Math.Clamp(frameIndex, 0, Spec.FrameCount - 1);
        return _frames[dirIdx, frmIdx];
    }

    public static SpriteSheet LoadFromEmbeddedMaskedBmp(AnimationSpec spec, double scale)
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        string colorRes = $"WinPenguins.Assets.{spec.ResourcePrefix}.bmp";
        string maskRes = $"WinPenguins.Assets.{spec.ResourcePrefix}Mask.bmp";

        using Stream colorStream = asm.GetManifestResourceStream(colorRes)
            ?? throw new InvalidOperationException($"Missing embedded sprite resource: {colorRes}");
        using Stream maskStream = asm.GetManifestResourceStream(maskRes)
            ?? throw new InvalidOperationException($"Missing embedded mask resource: {maskRes}");

        using var colorBmp = new Bitmap(colorStream);
        using var maskBmp = new Bitmap(maskStream);

        return BuildFromColorAndMask(spec, colorBmp, maskBmp, scale);
    }

    public static SpriteSheet LoadFromPng(
        AnimationSpec spec,
        string pngPath,
        bool reverseRightRow,
        double scale)
    {
        using var bmp = new Bitmap(pngPath);
        return BuildFromRgbaBitmap(spec, bmp, reverseRightRow, scale);
    }

    public static SpriteSheet LoadFromGif(
        AnimationSpec spec,
        string gifPath,
        bool reverseRightRow,
        double scale)
    {
        using var gif = new Bitmap(gifPath);
        var dim = new FrameDimension(gif.FrameDimensionsList[0]);
        int frameTotal = Math.Max(1, gif.GetFrameCount(dim));
        int framesToUse = Math.Max(1, spec.FrameCount);
        int fw = gif.Width;
        int fh = gif.Height;

        using var strip = new Bitmap(framesToUse * fw, spec.DirectionCount * fh, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(strip))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            for (int f = 0; f < framesToUse; f++)
            {
                gif.SelectActiveFrame(dim, f % frameTotal);
                g.DrawImage(gif, new Rectangle(f * fw, 0, fw, fh), 0, 0, fw, fh, GraphicsUnit.Pixel);
                if (spec.DirectionCount > 1)
                {
                    using var flipped = new Bitmap(gif);
                    flipped.RotateFlip(RotateFlipType.RotateNoneFlipX);
                    g.DrawImage(flipped, new Rectangle(f * fw, fh, fw, fh), 0, 0, fw, fh, GraphicsUnit.Pixel);
                }
            }
        }

        var scaledSpec = spec with { BaseWidth = fw, BaseHeight = fh };
        return BuildFromRgbaBitmap(scaledSpec, strip, reverseRightRow, scale);
    }

    /// <summary>
    /// Creates an EPX / Scale2x smooth vector-pixel 2x upscaled sheet from a classic sheet.
    /// </summary>
    public SpriteSheet CreateSmoothHdVariant(double scale)
    {
        int fw = Spec.BaseWidth;
        int fh = Spec.BaseHeight;
        int upW = fw * 2;
        int upH = fh * 2;

        uint[] srcAll = SheetPixels;
        int upSheetW = Spec.FrameCount * upW;
        int upSheetH = Spec.DirectionCount * upH;
        var upSheet = new uint[upSheetW * upSheetH];

        for (int d = 0; d < Spec.DirectionCount; d++)
        {
            for (int f = 0; f < Spec.FrameCount; f++)
            {
                int srcBaseX = f * fw;
                int srcBaseY = d * fh;
                int dstBaseX = f * upW;
                int dstBaseY = d * upH;

                for (int y = 0; y < fh; y++)
                {
                    for (int x = 0; x < fw; x++)
                    {
                        uint p = srcAll[(srcBaseY + y) * SheetWidth + srcBaseX + x];
                        uint a = srcAll[(srcBaseY + Math.Max(0, y - 1)) * SheetWidth + srcBaseX + x];
                        uint b = srcAll[(srcBaseY + y) * SheetWidth + srcBaseX + Math.Min(fw - 1, x + 1)];
                        uint c = srcAll[(srcBaseY + y) * SheetWidth + srcBaseX + Math.Max(0, x - 1)];
                        uint dPx = srcAll[(srcBaseY + Math.Min(fh - 1, y + 1)) * SheetWidth + srcBaseX + x];

                        uint e0 = (c == a && c != dPx && a != b) ? a : p;
                        uint e1 = (a == b && a != c && b != dPx) ? b : p;
                        uint e2 = (dPx == c && dPx != b && c != a) ? c : p;
                        uint e3 = (b == dPx && b != a && dPx != c) ? dPx : p;

                        int dy = (dstBaseY + y * 2) * upSheetW + dstBaseX + x * 2;
                        upSheet[dy] = e0;
                        upSheet[dy + 1] = e1;
                        upSheet[dy + upSheetW] = e2;
                        upSheet[dy + upSheetW + 1] = e3;
                    }
                }
            }
        }

        int dstW = Math.Max(1, (int)Math.Round(fw * scale));
        int dstH = Math.Max(1, (int)Math.Round(fh * scale));
        var frames = new SpriteFrame[Spec.DirectionCount, Spec.FrameCount];
        for (int d = 0; d < Spec.DirectionCount; d++)
        {
            for (int f = 0; f < Spec.FrameCount; f++)
            {
                uint[] scaled = new uint[dstW * dstH];
                int origX = f * upW;
                int origY = d * upH;
                for (int y = 0; y < dstH; y++)
                {
                    int sy = origY + (y * upH) / dstH;
                    for (int x = 0; x < dstW; x++)
                    {
                        int sx = origX + (x * upW) / dstW;
                        scaled[y * dstW + x] = upSheet[sy * upSheetW + sx];
                    }
                }
                frames[d, f] = new SpriteFrame(dstW, dstH, scaled);
            }
        }

        return new SpriteSheet(Spec, upSheetW, upSheetH, upSheet, dstW, dstH, frames);
    }

    public static SpriteSheet BuildFromRgbaBitmap(
        AnimationSpec spec,
        Bitmap rgbaBitmap,
        bool reverseRightRow,
        double scale)
    {
        using var copy = To32bppArgbExact(rgbaBitmap);
        int[] raw = CopyPixels(copy);
        int sheetW = copy.Width;
        int sheetH = copy.Height;

        return BuildSheetAndFrames(
            spec,
            sheetW,
            sheetH,
            reverseRightRow,
            scale,
            srcIdx =>
            {
                uint px = (uint)raw[srcIdx];
                uint a = (px >> 24) & 0xFF;
                if (a == 0)
                {
                    return 0u;
                }

                if (a == 255)
                {
                    return px;
                }

                uint r = (((px >> 16) & 0xFF) * a + 127) / 255;
                uint g = (((px >> 8) & 0xFF) * a + 127) / 255;
                uint b = ((px & 0xFF) * a + 127) / 255;
                return (a << 24) | (r << 16) | (g << 8) | b;
            });
    }

    public static SpriteSheet BuildFromColorAndMask(
        AnimationSpec spec,
        Bitmap colorBmp,
        Bitmap maskBmp,
        double scale)
    {
        using var color32 = To32bppArgbExact(colorBmp);
        using var mask32 = To32bppArgbExact(maskBmp);

        int[] colorPixels = CopyPixels(color32);
        int[] maskPixels = CopyPixels(mask32);
        int sheetW = color32.Width;
        int sheetH = color32.Height;

        uint[] cleaned = BuildCleanedClassicSheet(spec, colorPixels, maskPixels, sheetW, sheetH);

        // Original 2-row BMP sheets had row 1 created by horizontally mirroring the 256px row 0 strip.
        return BuildSheetAndFrames(
            spec,
            sheetW,
            sheetH,
            reverseRightRow: true,
            scale,
            srcIdx => cleaned[srcIdx]);
    }

    private static uint[] BuildCleanedClassicSheet(
        AnimationSpec spec,
        int[] colorPixels,
        int[] maskPixels,
        int sheetW,
        int sheetH)
    {
        var result = new uint[colorPixels.Length];
        bool deFringe = spec.Id != PenguinAnimationId.Explosion;

        for (int y = 0; y < sheetH; y++)
        {
            int frameYStart = (y / spec.BaseHeight) * spec.BaseHeight;
            int frameYEnd = Math.Min(sheetH, frameYStart + spec.BaseHeight);

            for (int x = 0; x < sheetW; x++)
            {
                int idx = y * sheetW + x;
                uint maskRgb = (uint)maskPixels[idx];
                if (((maskRgb >> 16) & 0xFF) >= 128)
                {
                    result[idx] = 0u;
                    continue;
                }

                uint c = (uint)colorPixels[idx];
                if (!deFringe)
                {
                    result[idx] = 0xFF000000u | (c & 0x00FFFFFFu);
                    continue;
                }

                int r = (int)((c >> 16) & 0xFF);
                int g = (int)((c >> 8) & 0xFF);
                int b = (int)(c & 0xFF);
                int lum = (r + g + b) / 3;
                int sat = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));

                if (sat <= 28 && lum >= 130)
                {
                    int frameXStart = (x / spec.BaseWidth) * spec.BaseWidth;
                    int frameXEnd = Math.Min(sheetW, frameXStart + spec.BaseWidth);

                    bool adjTrans = false;
                    bool adjDark = false;
                    bool adjPureWhite = false;

                    for (int k = 0; k < 4; k++)
                    {
                        int nx = k switch { 0 => x - 1, 1 => x + 1, _ => x };
                        int ny = k switch { 2 => y - 1, 3 => y + 1, _ => y };
                        if (nx < frameXStart || nx >= frameXEnd || ny < frameYStart || ny >= frameYEnd)
                        {
                            adjTrans = true;
                            continue;
                        }

                        int nIdx = ny * sheetW + nx;
                        if ((((uint)maskPixels[nIdx] >> 16) & 0xFF) >= 128)
                        {
                            adjTrans = true;
                            continue;
                        }

                        uint nc = (uint)colorPixels[nIdx];
                        int nr = (int)((nc >> 16) & 0xFF);
                        int ng = (int)((nc >> 8) & 0xFF);
                        int nb = (int)(nc & 0xFF);
                        int nl = (nr + ng + nb) / 3;

                        if (nl <= 115)
                        {
                            adjDark = true;
                        }

                        if (nr >= 205 && ng >= 205 && nb >= 205)
                        {
                            adjPureWhite = true;
                        }
                    }

                    // Remove light-gray/white halo baked around dark outlines on 1-bit Win98 BMP masks
                    // without touching pure-white interiors (belly, eye spots, Santa trim).
                    if (adjTrans && adjDark && !adjPureWhite)
                    {
                        double alpha = Math.Clamp((220.0 - lum) / 135.0, 0.0, 0.72);
                        uint a = (uint)Math.Round(alpha * 255.0);
                        if (a < 16)
                        {
                            result[idx] = 0u;
                        }
                        else
                        {
                            uint ch = (uint)Math.Round(18.0 * alpha);
                            result[idx] = (a << 24) | (ch << 16) | (ch << 8) | ch;
                        }
                        continue;
                    }
                }

                result[idx] = 0xFF000000u | (c & 0x00FFFFFFu);
            }
        }

        return result;
    }

    private static SpriteSheet BuildSheetAndFrames(
        AnimationSpec spec,
        int sheetW,
        int sheetH,
        bool reverseRightRow,
        double scale,
        Func<int, uint> samplePremulPixel)
    {
        int dstW = Math.Max(1, (int)Math.Round(spec.BaseWidth * scale));
        int dstH = Math.Max(1, (int)Math.Round(spec.BaseHeight * scale));

        int normalizedW = spec.FrameCount * spec.BaseWidth;
        int normalizedH = spec.DirectionCount * spec.BaseHeight;
        uint[] sheetPremul = new uint[normalizedW * normalizedH];
        var frames = new SpriteFrame[spec.DirectionCount, spec.FrameCount];

        for (int d = 0; d < spec.DirectionCount; d++)
        {
            bool reverse = reverseRightRow && spec.DirectionCount > 1 && d == 1;

            for (int f = 0; f < spec.FrameCount; f++)
            {
                int srcFrame = reverse ? (spec.FrameCount - 1 - f) : f;
                int srcOriginX = srcFrame * spec.BaseWidth;
                int srcOriginY = d * spec.BaseHeight;
                int dstSheetOriginX = f * spec.BaseWidth;

                for (int sy = 0; sy < spec.BaseHeight; sy++)
                {
                    int srcY = Math.Min(sheetH - 1, srcOriginY + sy);
                    int dstY = srcOriginY + sy;
                    int srcRow = srcY * sheetW;
                    int dstRow = dstY * normalizedW;

                    for (int sx = 0; sx < spec.BaseWidth; sx++)
                    {
                        int srcX = Math.Min(sheetW - 1, srcOriginX + sx);
                        sheetPremul[dstRow + dstSheetOriginX + sx] = samplePremulPixel(srcRow + srcX);
                    }
                }

                uint[] scaled = new uint[dstW * dstH];
                for (int y = 0; y < dstH; y++)
                {
                    int srcY = srcOriginY + (y * spec.BaseHeight) / dstH;
                    int rowOffset = srcY * normalizedW;
                    int dstRowOffset = y * dstW;

                    for (int x = 0; x < dstW; x++)
                    {
                        int srcX = dstSheetOriginX + (x * spec.BaseWidth) / dstW;
                        scaled[dstRowOffset + x] = sheetPremul[rowOffset + srcX];
                    }
                }

                frames[d, f] = new SpriteFrame(dstW, dstH, scaled);
            }
        }

        return new SpriteSheet(spec, normalizedW, normalizedH, sheetPremul, dstW, dstH, frames);
    }

    private static Bitmap To32bppArgbExact(Bitmap source)
    {
        var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        copy.SetResolution(source.HorizontalResolution, source.VerticalResolution);
        using Graphics g = Graphics.FromImage(copy);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(
            source,
            new Rectangle(0, 0, source.Width, source.Height),
            0,
            0,
            source.Width,
            source.Height,
            GraphicsUnit.Pixel);
        return copy;
    }

    private static int[] CopyPixels(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int count = bmp.Width * bmp.Height;
            int[] pixels = new int[count];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, count);
            return pixels;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
