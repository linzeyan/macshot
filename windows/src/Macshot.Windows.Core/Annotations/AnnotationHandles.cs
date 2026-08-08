using Macshot.Windows.Core.Capture;

namespace Macshot.Windows.Core.Annotations;

/// <summary>What one grab point on a selected annotation does.</summary>
public enum AnnotationHandleKind
{
    /// <summary>Moves the end a linear mark was drawn from.</summary>
    Start,

    /// <summary>Moves the end a linear mark was drawn to.</summary>
    End,

    /// <summary>Moves the control point that bows a line or arrow.</summary>
    Bend,

    /// <summary>Moves one of the intermediate anchors a mark is bent through.</summary>
    Waypoint,

    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,

    /// <summary>Turns an area shape about the centre of its upright bounds.</summary>
    Rotate,
}

/// <summary>One grab point, in frame space, already turned with the shape it belongs to.</summary>
/// <param name="Index">
/// Which of <see cref="Annotation.Waypoints"/> a <see cref="AnnotationHandleKind.Waypoint"/>
/// grabs, and zero for every other kind. An index rather than one kind per anchor, because
/// nothing bounds how many a mark can be given — macshot identifies them by array position
/// for the same reason (<c>OverlayView.swift:4352-4353</c>).
/// </param>
public readonly record struct AnnotationHandle(
    AnnotationHandleKind Kind,
    CapturePoint Position,
    int Index = 0);

/// <summary>
/// The grab points that let an annotation already drawn be reshaped: its ends moved, its
/// corners dragged, its body turned, its line bowed.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="Capture.SelectionHandles"/>, which does the same job for
/// the capture region. Kept out of the UI layer for the same reason: which handles a tool
/// offers and where a drag puts them is the behaviour worth testing, and none of it needs
/// a display.
/// </para>
/// <para>
/// Every position comes back turned by the annotation's own <see cref="Annotation.Rotation"/>
/// so a handle sits on the shape as drawn, and every drag is answered in the shape's own
/// upright frame. That pairing is what keeps a rotated rectangle's corners draggable
/// without the rotation and the resize fighting each other.
/// </para>
/// </remarks>
public static class AnnotationHandles
{
    /// <summary>
    /// How near a press has to be to count as grabbing a handle, in layout units.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Larger than the drawn square, because the handle is a target rather than a
    /// decoration, and a mark thin enough to need reshaping is one whose handles are hard
    /// to hit exactly.
    /// </para>
    /// <para>
    /// Layout units rather than frame pixels, for the reason
    /// <see cref="Capture.SelectionHandles.Size"/> gives: it is a distance a hand aims
    /// over, so ten frame pixels would be half the slack on a 200% display that it is on
    /// a 100% one. Everything here works in frame pixels, so each method takes the
    /// surface's scale and multiplies this by it.
    /// </para>
    /// </remarks>
    public const double GrabRadius = 10;

    /// <summary>
    /// How far outside the top edge the rotation handle floats, in layout units.
    /// </summary>
    /// <remarks>
    /// Off the shape rather than on it, so it cannot be confused with the corner beside
    /// it, and far enough that the first pixel of the drag already describes an angle.
    /// Scaled for the same reason <see cref="GrabRadius"/> is: unscaled it would sit
    /// half as far from the shape at 200% as at 100%, and the tether drawn to it would
    /// shorten with it.
    /// </remarks>
    public const double RotateReach = 24;

    /// <summary>
    /// The handles <paramref name="annotation"/> offers, or nothing for a mark that only
    /// moves.
    /// </summary>
    /// <remarks>
    /// A freeform stroke has no two points that describe it, so dragging one would have to
    /// distort every sample by some rule the user never chose. A mark that <em>is</em> a
    /// sprite — a label, a badge, a stamp — is composited one pixel to one pixel, so a
    /// resize would either scale glyphs into mush or leave the bounds disagreeing with what
    /// is drawn. Both move and nothing more. A ruler carries a sprite too, but only as its
    /// reading: the mark itself is the line, and the line is reshapable.
    /// </remarks>
    /// <param name="scale">Frame pixels to the layout unit on the surface it is drawn on.</param>
    public static IReadOnlyList<AnnotationHandle> For(Annotation annotation, double scale = 1)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        if (!annotation.IsMovable || annotation.Points.Count > 0 || Annotation.RequiresSprite(annotation.Tool))
        {
            return [];
        }

        return IsLinear(annotation.Tool) ? LinearHandles(annotation) : AreaHandles(annotation, scale);
    }

    /// <summary>
    /// The handle <paramref name="point"/> grabs, or null when it grabs none. The nearest
    /// one wins, so handles that overlap on a mark too small to separate them still each
    /// have a side of the shape that reaches them.
    /// </summary>
    /// <param name="scale">Frame pixels to the layout unit on the surface it is drawn on.</param>
    /// <param name="radius">
    /// How near counts as grabbing, in layout units. Scaled along with the handle
    /// positions, or the same-looking handle would be easier to hit on a 100% display
    /// than on a 200% one.
    /// </param>
    public static AnnotationHandle? At(
        Annotation annotation,
        CapturePoint point,
        double scale = 1,
        double radius = GrabRadius)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);

        AnnotationHandle? nearest = null;
        var nearestDistance = double.MaxValue;
        var reach = radius * scale;

        foreach (var handle in For(annotation, scale))
        {
            var distance = Distance(handle.Position, point);
            if (distance <= reach && distance < nearestDistance)
            {
                nearest = handle;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    /// <summary>
    /// The annotation as dragging <paramref name="kind"/> to <paramref name="point"/>
    /// leaves it. A handle this annotation does not offer leaves it alone rather than
    /// throwing: a drag that outlived the shape it started on is a UI mistake, and losing
    /// the mark over it would be a worse one.
    /// </summary>
    /// <param name="index">
    /// Which anchor is being dragged, for <see cref="AnnotationHandleKind.Waypoint"/>.
    /// Ignored by every other kind, which is why it comes last and defaults.
    /// </param>
    public static Annotation Drag(
        Annotation annotation,
        AnnotationHandleKind kind,
        CapturePoint point,
        EditorModifiers modifiers = EditorModifiers.None,
        int index = 0)
    {
        ArgumentNullException.ThrowIfNull(annotation);

        if (!Offers(annotation, kind, index))
        {
            return annotation;
        }

        // The rotation handle is the one drag that reads the pointer where it really is;
        // every other one works in the shape's upright frame, which is where Start and End
        // live.
        if (kind == AnnotationHandleKind.Rotate)
        {
            return annotation with { Rotation = AngleTo(annotation, point, modifiers) };
        }

        var upright = Turn(point, Centre(annotation), -annotation.Rotation);

        return kind switch
        {
            AnnotationHandleKind.Start => Restretched(annotation) with
            {
                Start = Constrain(annotation.End, upright, modifiers),
            },
            AnnotationHandleKind.End => Restretched(annotation) with
            {
                End = Constrain(annotation.Start, upright, modifiers),
            },
            AnnotationHandleKind.Bend => BentTo(annotation, upright),
            AnnotationHandleKind.Waypoint => MovedAnchor(annotation, index, upright),
            AnnotationHandleKind.TopLeft
                or AnnotationHandleKind.TopRight
                or AnnotationHandleKind.BottomLeft
                or AnnotationHandleKind.BottomRight => ResizeCorner(annotation, kind, upright, modifiers),
            _ => annotation,
        };
    }

    /// <summary>
    /// The four corners of the annotation's bounds, turned with it, clockwise from the top
    /// left: what a canvas outlines a selection with.
    /// </summary>
    /// <remarks>
    /// Offered even for the marks that have no handles, because a selected sprite or
    /// stroke still has to look selected — otherwise a click that hit nothing and a click
    /// that selected something are the same picture.
    /// </remarks>
    public static IReadOnlyList<CapturePoint> Outline(Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);

        var bounds = annotation.BoundingRect;
        var centre = Centre(annotation);

        return
        [
            Turn(new CapturePoint(bounds.X, bounds.Y), centre, annotation.Rotation),
            Turn(new CapturePoint(bounds.Right, bounds.Y), centre, annotation.Rotation),
            Turn(new CapturePoint(bounds.Right, bounds.Bottom), centre, annotation.Rotation),
            Turn(new CapturePoint(bounds.X, bounds.Bottom), centre, annotation.Rotation),
        ];
    }

    /// <summary>
    /// Drops a reading that is about to become wrong. A ruler's sprite says how long the
    /// span was, so moving an end — or an anchor the span now runs through — has to take
    /// the old number with it; the UI renders a new one once the drag is over.
    /// </summary>
    private static Annotation Restretched(Annotation annotation) =>
        annotation.Tool == AnnotationTool.Measure ? annotation with { Sprite = null } : annotation;

    private static bool Offers(Annotation annotation, AnnotationHandleKind kind, int index)
    {
        foreach (var handle in For(annotation))
        {
            // The index only distinguishes anchors from each other; every other kind
            // appears once, and comparing an index it never carries would refuse them all.
            if (handle.Kind == kind && (kind != AnnotationHandleKind.Waypoint || handle.Index == index))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether two annotations differ in anything a handle drag can change, which is what
    /// tells a released drag from a click on a handle.
    /// </summary>
    public static bool Differ(Annotation left, Annotation right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return left.Start != right.Start
            || left.End != right.End
            || left.Rotation != right.Rotation
            || left.Bend != right.Bend
            || left.BendAlong != right.BendAlong
            || !left.Waypoints.SequenceEqual(right.Waypoints);
    }

    /// <summary>
    /// Tools drawn as a path from one point to another, rather than filling the rectangle
    /// between them.
    /// </summary>
    private static bool IsLinear(AnnotationTool tool) => tool
        is AnnotationTool.Line
        or AnnotationTool.Arrow
        or AnnotationTool.Marker
        or AnnotationTool.Measure;

    private static IReadOnlyList<AnnotationHandle> LinearHandles(Annotation annotation)
    {
        var centre = Centre(annotation);
        var handles = new List<AnnotationHandle>(3)
        {
            new(AnnotationHandleKind.Start, Turn(annotation.Start, centre, annotation.Rotation)),
            new(AnnotationHandleKind.End, Turn(annotation.End, centre, annotation.Rotation)),
        };

        // Anchors instead of the bend, never both. They are two ways of describing the
        // same shape and only the anchors are drawn once a mark has them, so a bend grip
        // offered here would follow the pointer and change nothing on screen.
        if (annotation.HasWaypoints)
        {
            for (var index = 0; index < annotation.Waypoints.Count; index++)
            {
                handles.Add(new AnnotationHandle(
                    AnnotationHandleKind.Waypoint,
                    Turn(annotation.Waypoints[index], centre, annotation.Rotation),
                    index));
            }

            return handles;
        }

        // Only where the rasterizer can draw the curve. A ruler bowed off its own reading
        // would be measuring a distance it no longer spans.
        if (annotation.Tool is AnnotationTool.Line or AnnotationTool.Arrow)
        {
            handles.Add(new AnnotationHandle(
                AnnotationHandleKind.Bend,
                Turn(BendGrip(annotation), centre, annotation.Rotation)));
        }

        return handles;
    }

    /// <summary>
    /// The mark with one of its anchors moved to <paramref name="point"/>.
    /// </summary>
    /// <remarks>
    /// Nothing is constrained. Shift on an end snaps the whole mark to 45 degrees off the
    /// other end, which is a statement about the mark as a whole; an anchor in the middle
    /// of a chain has no such reference, and snapping it to an angle off whichever
    /// neighbour was chosen would put it somewhere the user cannot predict. macshot leaves
    /// this drag unconstrained too (<c>OverlayView.swift:6001-6008</c>).
    /// </remarks>
    private static Annotation MovedAnchor(Annotation annotation, int index, CapturePoint point)
    {
        var anchors = annotation.Waypoints.ToArray();
        anchors[index] = point;
        return Restretched(annotation) with { Waypoints = anchors };
    }

    private static IReadOnlyList<AnnotationHandle> AreaHandles(Annotation annotation, double scale)
    {
        var bounds = annotation.BoundingRect;
        var centre = Centre(annotation);

        var handles = new List<AnnotationHandle>(5)
        {
            new(AnnotationHandleKind.TopLeft, Turn(new CapturePoint(bounds.X, bounds.Y), centre, annotation.Rotation)),
            new(AnnotationHandleKind.TopRight, Turn(new CapturePoint(bounds.Right, bounds.Y), centre, annotation.Rotation)),
            new(AnnotationHandleKind.BottomLeft, Turn(new CapturePoint(bounds.X, bounds.Bottom), centre, annotation.Rotation)),
            new(AnnotationHandleKind.BottomRight, Turn(new CapturePoint(bounds.Right, bounds.Bottom), centre, annotation.Rotation)),
        };

        // A spotlight is left unturnable, as macshot leaves it: the region it lights is
        // punched out of the frame's own rows and columns, so a rotation would swing the
        // ring away from the bright rectangle it is supposed to be the edge of.
        if (annotation.Tool != AnnotationTool.Highlight)
        {
            handles.Add(new AnnotationHandle(
                AnnotationHandleKind.Rotate,
                Turn(new CapturePoint(centre.X, bounds.Y - (RotateReach * scale)), centre, annotation.Rotation)));
        }

        return handles;
    }

    /// <summary>
    /// Where the bend handle sits: on the control point itself, which for an unbent line
    /// is the middle of it.
    /// </summary>
    /// <remarks>
    /// Beside the curve rather than on it, which is macshot's own arrangement
    /// (<c>OverlayView.swift:4370-4378</c> puts the grip at <c>controlPoint</c>, falling
    /// back to the midpoint of the two ends). This port used to sit it on the curve, on
    /// the reasoning that a handle should be a point of the line it bends — but that only
    /// works while the drag is confined to one axis, and macshot's is not. A grip that is
    /// the control point follows the pointer exactly wherever it goes, which is worth more
    /// than being on the line, and it is what the dashed arms drawn out to it explain.
    /// </remarks>
    private static CapturePoint BendGrip(Annotation annotation)
    {
        var (mid, alongX, alongY, acrossX, acrossY) = Frame(annotation);
        var along = annotation.BendAlong;

        return new CapturePoint(
            mid.X + (alongX * along) + (acrossX * annotation.Bend),
            mid.Y + (alongY * along) + (acrossY * annotation.Bend));
    }

    /// <summary>
    /// The mark as dragging its control point to <paramref name="point"/> leaves it.
    /// </summary>
    /// <remarks>
    /// Both components are taken, not just the sideways one: macshot stores the pointer
    /// where it is (<c>OverlayView.swift:6011</c>), so sliding the grip towards an end
    /// moves the bulge towards that end instead of doing nothing. And nothing is clamped,
    /// because the reason this port clamped — that past a certain bow the handle stopped
    /// following the pointer and the drag read as broken — cannot arise once the handle is
    /// the very point being set.
    /// </remarks>
    private static Annotation BentTo(Annotation annotation, CapturePoint point)
    {
        var (mid, alongX, alongY, acrossX, acrossY) = Frame(annotation);
        var lengthSquared = (alongX * alongX) + (alongY * alongY);
        if (lengthSquared == 0)
        {
            return annotation;
        }

        var offsetX = point.X - mid.X;
        var offsetY = point.Y - mid.Y;

        return annotation with
        {
            Bend = ((offsetX * acrossX) + (offsetY * acrossY)) / lengthSquared,
            BendAlong = ((offsetX * alongX) + (offsetY * alongY)) / lengthSquared,
        };
    }

    /// <summary>
    /// The middle of a linear mark and the two directions a bend is measured in: along it
    /// and across it. Both vectors are exactly as long as the mark, so a bend fraction
    /// times either is that fraction of its length — which is what lets a bow survive the
    /// mark being dragged longer.
    /// </summary>
    private static (CapturePoint Mid, double AlongX, double AlongY, double AcrossX, double AcrossY) Frame(
        Annotation annotation)
    {
        var deltaX = annotation.End.X - annotation.Start.X;
        var deltaY = annotation.End.Y - annotation.Start.Y;
        var mid = new CapturePoint(
            (annotation.Start.X + annotation.End.X) / 2,
            (annotation.Start.Y + annotation.End.Y) / 2);

        return (mid, deltaX, deltaY, -deltaY, deltaX);
    }

    private static Annotation ResizeCorner(
        Annotation annotation,
        AnnotationHandleKind kind,
        CapturePoint point,
        EditorModifiers modifiers)
    {
        var bounds = annotation.BoundingRect;

        // The corner across the shape from the one being dragged stays put, which is what
        // makes a resize feel anchored rather than a move with a size change.
        var anchor = kind switch
        {
            AnnotationHandleKind.TopLeft => new CapturePoint(bounds.Right, bounds.Bottom),
            AnnotationHandleKind.TopRight => new CapturePoint(bounds.X, bounds.Bottom),
            AnnotationHandleKind.BottomLeft => new CapturePoint(bounds.Right, bounds.Y),
            _ => new CapturePoint(bounds.X, bounds.Y),
        };

        // Start and End are rewritten to the anchor and the pointer rather than kept in
        // the order they were drawn, because an area shape is its bounding rectangle and
        // the rasterizer reads nothing else from them.
        return annotation with
        {
            Start = anchor,
            End = Square(anchor, point, modifiers),
        };
    }

    private static CapturePoint Constrain(CapturePoint anchor, CapturePoint point, EditorModifiers modifiers)
    {
        if (!modifiers.HasFlag(EditorModifiers.Constrain))
        {
            return point;
        }

        var deltaX = point.X - anchor.X;
        var deltaY = point.Y - anchor.Y;
        var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (length == 0)
        {
            return point;
        }

        // The same 45 degree snap a shift-drag draws with, so reshaping a line and drawing
        // it land on the same angles.
        var step = Math.PI / 4;
        var angle = Math.Round(Math.Atan2(deltaY, deltaX) / step) * step;
        return new CapturePoint(anchor.X + (length * Math.Cos(angle)), anchor.Y + (length * Math.Sin(angle)));
    }

    private static CapturePoint Square(CapturePoint anchor, CapturePoint point, EditorModifiers modifiers)
    {
        if (!modifiers.HasFlag(EditorModifiers.Constrain))
        {
            return point;
        }

        var deltaX = point.X - anchor.X;
        var deltaY = point.Y - anchor.Y;
        var size = Math.Max(Math.Abs(deltaX), Math.Abs(deltaY));
        return new CapturePoint(
            anchor.X + Math.CopySign(size, deltaX),
            anchor.Y + Math.CopySign(size, deltaY));
    }

    /// <summary>
    /// The angle the rotation handle is being dragged to, measured so that leaving the
    /// handle where it started leaves the shape upright.
    /// </summary>
    private static double AngleTo(Annotation annotation, CapturePoint point, EditorModifiers modifiers)
    {
        var centre = Centre(annotation);
        var deltaX = point.X - centre.X;
        var deltaY = point.Y - centre.Y;
        if (deltaX == 0 && deltaY == 0)
        {
            return annotation.Rotation;
        }

        // The handle floats above the shape, so straight up is no rotation at all.
        var angle = Math.Atan2(deltaY, deltaX) + (Math.PI / 2);
        if (!modifiers.HasFlag(EditorModifiers.Constrain))
        {
            return angle;
        }

        // 45 degrees rather than the 90 macOS snaps to: a shape built from upright bounds
        // is barely changed by a quarter turn, so the diagonals are the orientations a
        // snap can actually give the user that dragging the corners cannot.
        var step = Math.PI / 4;
        return Math.Round(angle / step) * step;
    }

    private static CapturePoint Centre(Annotation annotation)
    {
        var bounds = annotation.BoundingRect;
        return new CapturePoint(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }

    /// <summary>
    /// Turns a point about a centre, the same way the rasterizer turns the paths it draws.
    /// </summary>
    private static CapturePoint Turn(CapturePoint point, CapturePoint centre, double rotation)
    {
        if (rotation == 0)
        {
            return point;
        }

        var sin = Math.Sin(rotation);
        var cos = Math.Cos(rotation);
        var offsetX = point.X - centre.X;
        var offsetY = point.Y - centre.Y;

        return new CapturePoint(
            centre.X + (offsetX * cos) - (offsetY * sin),
            centre.Y + (offsetX * sin) + (offsetY * cos));
    }

    private static double Distance(CapturePoint left, CapturePoint right)
    {
        var deltaX = left.X - right.X;
        var deltaY = left.Y - right.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }
}
