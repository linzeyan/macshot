using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Imaging;

namespace Macshot.Windows.Services;

/// <summary>
/// A finished capture taken apart again: the pixels the marks were drawn on, the marks,
/// and the adjustment they were seen through.
/// </summary>
/// <remarks>
/// <para>
/// The three exist so that a capture can be archived in a form it can be reopened
/// <em>and edited</em> from. A flat image reopens as pixels — the arrow in it can be
/// drawn over but never moved, restyled or removed — and moving an arrow is most of what
/// reopening a capture is for.
/// </para>
/// <para>
/// <see cref="Raw"/> is the pixels before the adjustment rather than after it, which is
/// the difference between an adjustment that can be taken back off and one that has
/// become the capture. <see cref="State"/> says what was applied, so rasterizing
/// <see cref="Annotations"/> onto <see cref="Raw"/> seen through it reproduces the
/// delivered image exactly rather than approximately — and it is why a capture the three
/// cannot reproduce is handed over as null instead, rather than as a set that would
/// reopen as a different picture.
/// </para>
/// </remarks>
public sealed record EditableCapture(
    CapturedFrame Raw,
    IReadOnlyList<Annotation> Annotations,
    CaptureEditState State);
