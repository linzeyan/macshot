using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Imaging;

/// <summary>
/// The pixels a part of a capture is actually showing — the capture with the marks drawn
/// into it — and the part of the capture they cover.
/// </summary>
/// <remarks>
/// <para>
/// What the colour sampler reads. macshot samples the composited canvas rather than the
/// bare screenshot (<c>OverlayView.swift:sampleCanvasColor</c>), so a colour already used
/// in a mark can be picked back up. That is most of what the tool is for once anything has
/// been drawn: matching a second arrow to the first one is otherwise guesswork.
/// </para>
/// <para>
/// A part rather than a second copy of the whole capture, because a part is all that ever
/// carries marks. The preview covers the selection and the delivered image is the
/// selection, so a point outside it falls back to the capture, where by construction there
/// is nothing drawn.
/// </para>
/// <para>
/// <see cref="Covers"/> is kept apart from <see cref="Width"/> and <see cref="Height"/>
/// because the two need not agree. A snapped window is previewed from the window's own
/// pixels, which can be a different size from the rectangle it occupies on the desktop, so
/// a lookup that merely subtracted the origin would read the wrong part of it.
/// </para>
/// </remarks>
public readonly record struct RenderedRegion(byte[] Pixels, int Width, int Height, CaptureRegion Covers);
