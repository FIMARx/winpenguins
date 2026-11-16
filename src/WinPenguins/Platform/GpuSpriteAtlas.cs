using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinPenguins.Assets;
using WinPenguins.Engine;

namespace WinPenguins.Platform;

public sealed unsafe class GpuSpriteAtlas : IDisposable
{
    public const int FramePadding = 1;

    private readonly Dictionary<PenguinAnimationId, ID2D1Bitmap1> _bitmaps = [];
    private readonly IReadOnlyDictionary<PenguinAnimationId, AnimationSpec> _specs;

    public GpuSpriteAtlas(ID2D1DeviceContext d2dContext, SpriteCatalog catalog)
    {
        _specs = catalog.GetSpecsMap();
        var props = new BitmapProperties1(
            new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            96f,
            96f);

        foreach ((PenguinAnimationId id, SpriteSheet sheet) in catalog.Sheets)
        {
            uint[] padded = BuildPaddedAtlasPixels(sheet, out int paddedW, out int paddedH);
            fixed (uint* ptr = padded)
            {
                ID2D1Bitmap1 bmp = d2dContext.CreateBitmap(
                    new SizeI(paddedW, paddedH),
                    (IntPtr)ptr,
                    (uint)(paddedW * sizeof(uint)),
                    props);
                _bitmaps[id] = bmp;
            }
        }
    }

    public ID2D1Bitmap1 GetSheetBitmap(PenguinAnimationId id) => _bitmaps[id];

    public AnimationSpec GetSpec(PenguinAnimationId id) =>
        _specs.TryGetValue(id, out AnimationSpec? spec) ? spec : AnimationCatalogSpecs.Get(id);

    public RawRectF GetFrameSourceRect(PenguinAnimationId id, int directionRow, int frameIndex)
    {
        AnimationSpec spec = GetSpec(id);
        int dir = spec.DirectionCount > 1 ? Math.Clamp(directionRow, 0, spec.DirectionCount - 1) : 0;
        int frm = Math.Clamp(frameIndex, 0, spec.FrameCount - 1);
        int cellW = spec.BaseWidth + FramePadding * 2;
        int cellH = spec.BaseHeight + FramePadding * 2;
        float srcX = frm * cellW + FramePadding;
        float srcY = dir * cellH + FramePadding;
        return new RawRectF(srcX, srcY, srcX + spec.BaseWidth, srcY + spec.BaseHeight);
    }

    /// <summary>
    /// Surrounds every sprite frame with a 1px transparent gutter so GPU sampling
    /// never bleeds adjacent frames or direction rows during fractional positioning or scaling.
    /// </summary>
    internal static uint[] BuildPaddedAtlasPixels(SpriteSheet sheet, out int paddedWidth, out int paddedHeight)
    {
        int frames = sheet.Spec.FrameCount;
        int dirs = sheet.Spec.DirectionCount;
        int fw = sheet.Spec.BaseWidth;
        int fh = sheet.Spec.BaseHeight;
        int cellW = fw + FramePadding * 2;
        int cellH = fh + FramePadding * 2;

        paddedWidth = frames * cellW;
        paddedHeight = dirs * cellH;
        var padded = new uint[paddedWidth * paddedHeight];

        for (int d = 0; d < dirs; d++)
        {
            for (int f = 0; f < frames; f++)
            {
                int dstBaseX = f * cellW + FramePadding;
                int dstBaseY = d * cellH + FramePadding;
                int srcBaseX = f * fw;
                int srcBaseY = d * fh;

                for (int y = 0; y < fh; y++)
                {
                    int srcOffset = (srcBaseY + y) * sheet.SheetWidth + srcBaseX;
                    int dstOffset = (dstBaseY + y) * paddedWidth + dstBaseX;
                    Array.Copy(sheet.SheetPixels, srcOffset, padded, dstOffset, fw);
                }
            }
        }

        return padded;
    }

    public void Dispose()
    {
        foreach (ID2D1Bitmap1 bmp in _bitmaps.Values)
        {
            bmp.Dispose();
        }

        _bitmaps.Clear();
    }
}
