using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Recognition;
using Macshot.Windows.Rendering;
using Macshot.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using Windows.System;

namespace Macshot.Windows;

/// <summary>
/// The surface annotations are drawn on: the live preview, the sprite tools, and the
/// text tool's entry box.
/// </summary>
/// <remarks>
/// <para>
/// A control rather than markup inside one window for the same reason the toolbar is
/// one: the capture overlay and the editor window need identical behaviour here, and
/// the sprite half of it is subtle enough that a second copy would be a second set of
/// bugs — placements are queued because rasterizing is asynchronous, and committing a
/// label has to survive the focus change that removing its box raises.
/// </para>
/// <para>
/// It knows nothing about where it is. Everything that differs between a display-sized
/// overlay and an image in a scroll viewer is behind <see cref="IFramePlacement"/>, and
/// the host owns the pointer: it converts its own input to frame-space points and calls
/// in. That is the same division as the macOS <c>AnnotationCanvas</c> protocol.
/// </para>
/// </remarks>
public sealed partial class AnnotationCanvasView : UserControl
{
    private AnnotationEditor? _editor;
    private IFramePlacement _placement = new ImageFramePlacement();
    private Action<string> _reportHint = _ => { };
    private Func<double>? _rasterizationScale;

    private RasterAnnotationPreview? _preview;
    private CapturedFrame? _source;
    private CaptureRegion _region;

    private TextBox? _textEntry;
    private CapturePoint _textEntryOrigin;

    /// <summary>
    /// The sprite placement still rasterizing, if any. Producing a sprite is async, so
    /// finishing has to wait for it: without this, clicking Done right after typing
    /// would deliver an image missing the text that click committed.
    /// </summary>
    private Task _pendingSprite = Task.CompletedTask;

    public AnnotationCanvasView()
    {
        InitializeComponent();
    }

    /// <summary>The stamp tool's emoji, read from the toolbar when one is placed.</summary>
    public Func<string> StampEmoji { get; set; } = () => StampGlyph.Default;

    /// <summary>True while the text tool's entry box is open and owns the keyboard.</summary>
    public bool IsTyping => _textEntry is not null;

    /// <summary>The region the preview covers, in frame space.</summary>
    public CaptureRegion Region => _region;

    public void Bind(AnnotationEditor editor, Func<double> rasterizationScale, Action<string> reportHint)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(rasterizationScale);
        ArgumentNullException.ThrowIfNull(reportHint);

        _editor = editor;
        _rasterizationScale = rasterizationScale;
        _reportHint = reportHint;
    }

    /// <summary>
    /// Shows <paramref name="pixels"/> as the region being annotated, replacing whatever
    /// was there. Called again when the region changes, which is what makes an adjusted
    /// selection preview the pixels it actually covers.
    /// </summary>
    public void Present(CapturedFrame pixels, CaptureRegion region, IFramePlacement placement)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(placement);

        _source = pixels;
        _region = region;
        _placement = placement;

        _preview?.Detach();
        _preview = new RasterAnnotationPreview(AnnotationLayer, placement, pixels, region);
        Render();
    }

    /// <summary>The pixels on show, ready to be delivered.</summary>
    public CapturedFrame? ToFrame() => _preview?.ToFrame();

    public void Render() => _preview?.Render(_editor?.VisibleAnnotations ?? []);

    /// <summary>
    /// Whether <paramref name="tool"/> is placed with a click rather than dragged out.
    /// </summary>
    /// <remarks>
    /// Sprite tools take their size from the rasterized pixels, so there is nothing left
    /// for a drag to decide. The host must not tell the editor about such a press, which
    /// is what keeps its move and release handlers no-ops.
    /// </remarks>
    public static bool IsPlacedByClick(AnnotationTool tool) => Annotation.RequiresSprite(tool);

    /// <summary>
    /// Starts placing the active sprite tool's mark at <paramref name="point"/>. Text
    /// opens an entry box and commits later; the other two commit as soon as their
    /// glyphs are rasterized. See <c>docs/windows-port/architecture.md</c>, decision D7.
    /// </summary>
    public void PlaceSprite(CapturePoint point)
    {
        switch (_editor?.Tool)
        {
            case AnnotationTool.Text:
                QueueSprite(() => ReplaceTextEntryAsync(point));
                return;
            case AnnotationTool.Number:
                QueueSprite(() => PlaceNumberAsync(point));
                return;
            case AnnotationTool.Stamp:
                QueueSprite(() => PlaceStampAsync(point));
                return;
            default:
                return;
        }
    }

    /// <summary>
    /// Lands everything in flight, so the pixels handed over are the ones the user made.
    /// </summary>
    /// <remarks>
    /// Text still in the entry box is part of the image the moment the user finishes,
    /// and clicking Done is finishing. An in-flight mark is not: the preview still shows
    /// the draft, so it is dropped and the preview brought back into agreement before
    /// its pixels are taken.
    /// </remarks>
    public async Task FlushAsync()
    {
        QueueSprite(CommitTextEntryAsync);
        await _pendingSprite;

        if (_editor?.Cancel() == true)
        {
            Render();
        }
    }

    /// <summary>Discards what is being typed, which is what the first Escape means.</summary>
    public bool CancelTyping()
    {
        if (_textEntry is null)
        {
            return false;
        }

        RemoveTextEntry();
        return true;
    }

    /// <summary>
    /// Reads the text in the pixels on show.
    /// </summary>
    /// <remarks>
    /// From the preview's own source rather than from the host's screenshot: for a
    /// snapped window those are different images, and the one worth reading is the one
    /// the user is looking at. The line boxes come back in frame space, which is what
    /// the redaction rectangles are then placed in.
    /// </remarks>
    public async Task<IReadOnlyList<RecognizedLine>> RecognizeAsync()
    {
        if (_source is not { } source)
        {
            return [];
        }

        return await TextRecognizer.RecognizeAsync(source, _region.X, _region.Y);
    }

    /// <summary>
    /// Runs sprite work behind whatever is already in flight, and keeps the tail of the
    /// chain so finishing can wait for all of it.
    /// </summary>
    /// <remarks>
    /// Sprites are rasterized asynchronously, so two placements a moment apart would
    /// otherwise interleave: clicking away from a half-typed label would open the next
    /// entry box before the previous one had committed, and the first label would be
    /// lost. Ordering them is cheaper than making each one defend itself.
    /// </remarks>
    private void QueueSprite(Func<Task> work) => _pendingSprite = RunAfterAsync(_pendingSprite, work);

    private async Task RunAfterAsync(Task previous, Func<Task> work)
    {
        try
        {
            await previous;
            await work();
        }
        catch (Exception exception)
        {
            // Reported through the host's hint line rather than a dialog: an overlay is a
            // borderless always-on-top window covering the screen, so a dialog has
            // nowhere to go. Catching here also keeps one failed placement from poisoning
            // every later one that waits on it.
            _reportHint(exception.Message);
        }
    }

    /// <summary>
    /// Finishes whatever was being typed before starting the next label, so clicking
    /// elsewhere moves the text on rather than abandoning it.
    /// </summary>
    private async Task ReplaceTextEntryAsync(CapturePoint point)
    {
        await CommitTextEntryAsync();
        BeginTextEntry(point);
    }

    /// <summary>
    /// Places a numbered badge centred on <paramref name="point"/>. Rasterizing the
    /// digits is async, so the badge appears a frame or two after the click — which is
    /// exactly why a sprite is produced once when the annotation is committed and never
    /// from inside the draw path.
    /// </summary>
    private async Task PlaceNumberAsync(CapturePoint point)
    {
        if (_editor is not { } editor)
        {
            return;
        }

        var style = editor.Style;

        // The next number is read off the document rather than kept in a counter, so
        // undoing a badge frees its number instead of leaving a hole in the sequence.
        var value = editor.Document.Annotations.Count(existing => existing.Tool == AnnotationTool.Number) + 1;

        var badge = NumberBadge.Build(value, style, SpriteScale);
        var sprite = await GlyphSpriteFactory.RenderAsync(SpriteHost, badge);

        Commit(Annotation.CreateSprite(AnnotationTool.Number, Centred(point, sprite), sprite, style) with
        {
            // Kept alongside the pixels so the badge stays readable as data, not only
            // as an image.
            NumberValue = value,
        });
    }

    private async Task PlaceStampAsync(CapturePoint point)
    {
        if (_editor is not { } editor)
        {
            return;
        }

        var style = editor.Style;
        var emoji = StampEmoji();

        var glyph = StampGlyph.Build(emoji, style, SpriteScale);
        var sprite = await GlyphSpriteFactory.RenderAsync(SpriteHost, glyph);

        Commit(Annotation.CreateSprite(AnnotationTool.Stamp, Centred(point, sprite), sprite, style) with
        {
            Text = emoji,
        });
    }

    /// <summary>
    /// Opens the on-canvas entry box. The box is what makes the text tool feel like
    /// typing on the screenshot rather than filling in a dialog, and it uses the same
    /// font size the sprite will, so what is typed is what is committed.
    /// </summary>
    private void BeginTextEntry(CapturePoint point)
    {
        if (_editor is not { } editor)
        {
            return;
        }

        var position = _placement.ToLayout(point);
        var entry = new TextBox
        {
            MinWidth = 120,

            // No padding, so the first glyph sits at the click point rather than inset
            // from it by whatever the theme's padding happens to be.
            Padding = new Thickness(0),
            FontSize = TextGlyphs.FontSizeFor(editor.Style, SpriteScale),
            Foreground = new SolidColorBrush(GlyphSpriteFactory.ToBrushColor(editor.Style)),
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
        };

        entry.KeyDown += TextEntry_KeyDown;
        entry.LostFocus += TextEntry_LostFocus;
        Canvas.SetLeft(entry, position.X);
        Canvas.SetTop(entry, position.Y);
        TextEntryLayer.Children.Add(entry);
        _textEntry = entry;
        _textEntryOrigin = point;
        entry.Focus(FocusState.Programmatic);
        _reportHint("Type the label • Enter to place • Esc to discard it");
    }

    private void TextEntry_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                // Handled here, or it would bubble up and finish the whole capture.
                e.Handled = true;
                QueueSprite(CommitTextEntryAsync);
                return;

            case VirtualKey.Escape:
                // The first Escape discards the text being typed, the same way it
                // discards a half-drawn mark before it cancels the capture.
                e.Handled = true;
                RemoveTextEntry();
                return;

            default:
                return;
        }
    }

    /// <summary>
    /// Clicking anywhere else — another tool, the canvas, Done — means the text is
    /// finished, so it is committed rather than lost.
    /// </summary>
    private void TextEntry_LostFocus(object sender, RoutedEventArgs e) => QueueSprite(CommitTextEntryAsync);

    private async Task CommitTextEntryAsync()
    {
        if (_textEntry is not { } entry || _editor is not { } editor)
        {
            return;
        }

        var text = entry.Text.Trim();
        var origin = _textEntryOrigin;
        var style = editor.Style;

        // Torn down before the await, so the LostFocus that removing the box raises
        // cannot commit the same text a second time.
        RemoveTextEntry();
        if (text.Length == 0)
        {
            return;
        }

        var glyphs = TextGlyphs.Build(text, style, SpriteScale);
        var sprite = await GlyphSpriteFactory.RenderAsync(SpriteHost, glyphs);

        // Anchored at the click rather than centred on it: the user typed from there.
        Commit(Annotation.CreateSprite(AnnotationTool.Text, origin, sprite, style) with { Text = text });
    }

    private void RemoveTextEntry()
    {
        if (_textEntry is not { } entry)
        {
            return;
        }

        _textEntry = null;
        entry.KeyDown -= TextEntry_KeyDown;
        entry.LostFocus -= TextEntry_LostFocus;
        TextEntryLayer.Children.Remove(entry);

        // The keyboard belongs to the host again: Enter finishes, Ctrl+Z undoes. Which
        // element gets it back is the host's business, so it is told rather than asked.
        TypingEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raised when the entry box closes, so the host can take the keyboard back and put
    /// its standing instruction back in the hint line.
    /// </summary>
    public event EventHandler? TypingEnded;

    private void Commit(Annotation annotation)
    {
        _editor?.Document.Add(annotation);
        Render();
    }

    /// <summary>
    /// The scale XAML will actually rasterize at, which is what decides how many pixels
    /// a sprite comes out as. Named for what it is for rather than after
    /// <see cref="UIElement.RasterizationScale"/>, which this would otherwise hide —
    /// and which reads 0 until the element has been arranged.
    /// </summary>
    private double SpriteScale => _rasterizationScale?.Invoke() ?? 1;

    /// <summary>
    /// A mark aimed at a point belongs centred on it; anchoring its top-left there would
    /// drop it down and right of what the user aimed at.
    /// </summary>
    private static CapturePoint Centred(CapturePoint point, AnnotationSprite sprite) =>
        new(point.X - (sprite.Width / 2.0), point.Y - (sprite.Height / 2.0));
}
