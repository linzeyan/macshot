using Macshot.Windows.Core.Annotations;
using Macshot.Windows.Core.Capture;
using Macshot.Windows.Core.Imaging;
using Macshot.Windows.Core.Recognition;
using Macshot.Windows.Rendering;
using Macshot.Windows.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

// Imported rather than written out at each use site: inside namespace Macshot.Windows
// the name "Windows" binds to Macshot.Windows, so a qualified Point resolves to
// Macshot.Point and does not compile.
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

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
    /// <summary>
    /// How big a grab handle is drawn, in layout units. Smaller than
    /// <see cref="AnnotationHandles.GrabRadius"/> is generous with, because a handle drawn
    /// as large as its catchment would cover the mark it belongs to.
    /// </summary>
    private const double HandleSize = 9;

    private readonly Brush _chromeStroke = new SolidColorBrush(Color.FromArgb(255, 76, 194, 255));
    private readonly Brush _handleFill = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));

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

        // The grab handles are the one part of the editor measured in what a hand can
        // aim at rather than in capture pixels, and only the placement knows the two
        // apart. Set here rather than in Bind, because an overlay binds once and is then
        // presented onto whichever display's region was chosen.
        if (_editor is { } editor)
        {
            editor.Scale = placement.Scale;
        }

        _preview?.Detach();
        _preview = new RasterAnnotationPreview(AnnotationLayer, placement, pixels, region);
        Render();
    }

    /// <summary>The pixels on show, ready to be delivered.</summary>
    public CapturedFrame? ToFrame() => _preview?.ToFrame();

    /// <summary>
    /// The same capture in the two pieces it was made from, for archiving it in a form
    /// that can be edited again. Null before anything is being previewed.
    /// </summary>
    /// <remarks>
    /// The whole document rather than <c>VisibleAnnotations</c>: what is hidden is
    /// hidden for the length of a drag, and a mark left out here would be lost from the
    /// archive rather than merely not drawn.
    /// </remarks>
    public EditableCapture? ToEditable() =>
        _preview?.ToEditable(_editor?.Document.Annotations ?? []);

    public void Render()
    {
        _preview?.Render(_editor?.VisibleAnnotations ?? []);
        DrawSelectionChrome();

        // A ruler is worth nothing until it says a number, and the number it will say
        // once it is let go is worth having during the drag. It goes in the hint line
        // rather than beside the pointer: a figure that jitters along with the mouse is
        // harder to read than one that sits still.
        if (_editor?.Draft is { Tool: AnnotationTool.Measure } ruler)
        {
            _reportHint(MeasureReading.Format(ruler.Span));
        }
    }

    /// <summary>
    /// Gives every ruler that has been drawn but not yet labelled its reading.
    /// </summary>
    /// <remarks>
    /// Called by the host once a gesture ends, because the reading cannot be produced
    /// during one: rasterizing digits is asynchronous, and the number is not known until
    /// the drag stops. The label is amended onto the annotation rather than replacing it,
    /// so the ruler and its reading are one undo step — the drag the user made.
    /// </remarks>
    public void LabelRulers()
    {
        if (_editor is not { } editor)
        {
            return;
        }

        foreach (var ruler in editor.Document.Annotations
            .Where(annotation => annotation.Tool == AnnotationTool.Measure && annotation.Sprite is null)
            .ToArray())
        {
            QueueSprite(() => LabelRulerAsync(ruler));
        }
    }

    private async Task LabelRulerAsync(Annotation ruler)
    {
        var reading = MeasureReading.Build(ruler.Style, ruler.Span, SpriteScale);
        var sprite = await GlyphSpriteFactory.RenderAsync(SpriteHost, reading);

        // Amend, not add: undone or reshaped while the sprite was rendering, the ruler
        // this belongs to may be gone, and a reading for a ruler that is not there would
        // be a number floating on the screenshot.
        _editor?.Document.Amend(ruler with { Sprite = sprite });
        Render();
    }

    /// <summary>
    /// Redraws the outline and handles around the selected mark.
    /// </summary>
    /// <remarks>
    /// Rebuilt from scratch on every render rather than kept and moved. A selection is one
    /// small shape and at most five handles, and the alternative is a cache that has to
    /// know when a rotation, a reshape or an undo invalidated it — which is every render
    /// this is called from anyway.
    /// </remarks>
    private void DrawSelectionChrome()
    {
        SelectionLayer.Children.Clear();

        if (_editor?.SelectionShown is not { } shown)
        {
            return;
        }

        var outline = AnnotationHandles.Outline(shown).Select(_placement.ToLayout).ToArray();
        var border = new Polygon
        {
            Stroke = _chromeStroke,
            StrokeThickness = 1,

            // Dashed, so an outline drawn around a rectangle the user drew cannot be
            // mistaken for a second rectangle they did not.
            StrokeDashArray = new DoubleCollection { 4, 3 },
            IsHitTestVisible = false,
        };

        // Added to the collection the shape already owns rather than assigning a new one,
        // because the XAML collection types are not all constructible from code.
        foreach (var corner in outline)
        {
            border.Points.Add(corner);
        }

        SelectionLayer.Children.Add(border);

        foreach (var handle in _editor.Handles)
        {
            AddHandle(handle, outline);
        }
    }

    private void AddHandle(AnnotationHandle handle, IReadOnlyList<Point> outline)
    {
        var at = _placement.ToLayout(handle.Position);

        if (handle.Kind == AnnotationHandleKind.Rotate)
        {
            // Tethered to the edge it swings, so a circle floating clear of the shape is
            // legibly part of it rather than a stray mark.
            var top = new Point((outline[0].X + outline[1].X) / 2, (outline[0].Y + outline[1].Y) / 2);
            SelectionLayer.Children.Add(new Line
            {
                X1 = top.X,
                Y1 = top.Y,
                X2 = at.X,
                Y2 = at.Y,
                Stroke = _chromeStroke,
                StrokeThickness = 1,
                IsHitTestVisible = false,
            });
        }

        var round = handle.Kind is AnnotationHandleKind.Rotate or AnnotationHandleKind.Bend;
        var shape = new Rectangle
        {
            Width = HandleSize,
            Height = HandleSize,

            // Round for the two handles that change something other than a position, so
            // the shape of the grab point says what letting go will do.
            RadiusX = round ? HandleSize / 2 : 1,
            RadiusY = round ? HandleSize / 2 : 1,
            Fill = _handleFill,
            Stroke = _chromeStroke,
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(shape, at.X - (HandleSize / 2));
        Canvas.SetTop(shape, at.Y - (HandleSize / 2));
        SelectionLayer.Children.Add(shape);
    }

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
    /// Lays each translation over the words it replaces, and answers how many were
    /// placed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The boxes are painted the average colour of the pixels underneath, sampled from
    /// the preview's own source: a translation on a grey panel wants that grey behind it,
    /// and a white box would announce itself as a patch.
    /// </para>
    /// <para>
    /// One <c>AddRange</c> under one group, so a single Ctrl+Z takes the whole page of
    /// translations back off. Put in one at a time they would need one undo each, and a
    /// half-undone page is worse than either state.
    /// </para>
    /// </remarks>
    public async Task<int> LayTranslationsOverAsync(IReadOnlyList<TranslatedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (_editor is not { } editor || _source is not { } source)
        {
            return 0;
        }

        var group = Guid.NewGuid();
        var placed = new List<Annotation>(lines.Count);
        foreach (var line in lines)
        {
            // The line boxes came back in frame space; the pixels are the crop, which
            // starts at the region's own corner.
            var underneath = new CaptureRegion(
                line.Bounds.X - _region.X,
                line.Bounds.Y - _region.Y,
                line.Bounds.Width,
                line.Bounds.Height);

            var average = PixelEffects.AverageColor(source.BgraPixels, source.Width, source.Height, underneath);
            var background = Color.FromArgb(byte.MaxValue, average.Red, average.Green, average.Blue);

            var box = TranslationGlyphs.Build(line, background, SpriteScale);
            var sprite = await GlyphSpriteFactory.RenderAsync(SpriteHost, box);

            placed.Add(Annotation.CreateSprite(
                AnnotationTool.Text,
                new CapturePoint(line.Bounds.X, line.Bounds.Y),
                sprite,
                editor.Style) with
            {
                // Kept alongside the pixels so the translation is still readable as
                // text — by the history, and by whatever reads a capture back.
                Text = line.Text,
                GroupId = group,
            });
        }

        editor.Document.AddRange(placed);
        Render();
        return placed.Count;
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
            // Return has to be accepted for Shift+Enter to be able to insert one; plain
            // Enter is taken below before the box ever sees it.
            AcceptsReturn = true,
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
        _reportHint("Type the label • Enter to place • Shift+Enter for a new line • Esc to discard it");
    }

    private void TextEntry_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter when IsShiftDown():
                // Left for the box to insert. A label that has to say two things needs
                // two lines, and the alternative — placing a second label underneath —
                // means lining them up by hand.
                return;

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

    /// <summary>
    /// Read from the keyboard rather than from the event, because a
    /// <see cref="KeyRoutedEventArgs"/> carries the key that was pressed and not the
    /// modifiers held with it.
    /// </summary>
    private static bool IsShiftDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

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
