namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// Rasterized pixels an annotation carries with it: glyphs, digits, or emoji that
/// the CPU rasterizer has no font engine to draw.
/// </summary>
/// <remarks>
/// <para>
/// A sprite is bytes rather than a platform image so Core stays platform neutral,
/// and so the compositing stays testable without a font engine attached: a
/// synthetic sprite asserts the blend. Producing a real one belongs to the UI
/// layer, through <c>RenderTargetBitmap</c>, because that is DirectWrite and
/// therefore the only path where font fallback is right and colour emoji come out
/// in colour. See <c>docs/windows-port/architecture.md</c>, decision D7.
/// </para>
/// <para>
/// The buffer is <b>premultiplied</b> BGRA, top-down, which is what
/// <c>RenderTargetBitmap</c> hands back. Compositing it as straight alpha would
/// halo every glyph edge.
/// </para>
/// <para>
/// Pixels are capture resolution, not layout units, because the rasterizer
/// composites one to one. Whatever produces a sprite has to size its text in frame
/// pixels or the text comes out the wrong size on a scaled display.
/// </para>
/// <para>
/// Identity is by reference on purpose. <see cref="Annotation"/> is a record, so
/// value equality here would walk the whole buffer every time two annotations are
/// compared, and two separately rasterized sprites are never meaningfully "the
/// same" anyway.
/// </para>
/// </remarks>
public sealed class AnnotationSprite
{
    private readonly byte[] _pixels;

    public AnnotationSprite(int width, int height, ReadOnlySpan<byte> premultipliedBgra)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var expectedLength = checked(width * height * 4);
        if (premultipliedBgra.Length != expectedLength)
        {
            throw new ArgumentException(
                "The pixel buffer does not match the sprite dimensions.",
                nameof(premultipliedBgra));
        }

        Width = width;
        Height = height;

        // Copied so the sprite is as immutable as the annotation holding it: the
        // caller's buffer comes from a bitmap it is free to reuse.
        _pixels = premultipliedBgra.ToArray();
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlySpan<byte> Pixels => _pixels;
}
