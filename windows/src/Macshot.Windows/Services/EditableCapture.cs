using Macshot.Windows.Core.Annotations;

namespace Macshot.Windows.Services;

/// <summary>
/// A finished capture taken apart again: the pixels the marks were drawn on, and the
/// marks, in that image's own coordinates.
/// </summary>
/// <remarks>
/// <para>
/// The pair exists so that a capture can be archived in a form it can be reopened
/// <em>and edited</em> from. A flat image reopens as pixels — the arrow in it can be
/// drawn over but never moved, restyled or removed — and moving an arrow is most of what
/// reopening a capture is for.
/// </para>
/// <para>
/// <see cref="Raw"/> is the pixels as the preview had them, which is to say with any
/// adjustment or inversion already applied and nothing drawn on top. That is what makes
/// rasterizing <see cref="Annotations"/> onto it reproduce the delivered image exactly
/// rather than approximately — and it is why a capture the two cannot reproduce is
/// handed over as null instead, rather than as a pair that would reopen as a different
/// picture.
/// </para>
/// </remarks>
public sealed record EditableCapture(CapturedFrame Raw, IReadOnlyList<Annotation> Annotations);
