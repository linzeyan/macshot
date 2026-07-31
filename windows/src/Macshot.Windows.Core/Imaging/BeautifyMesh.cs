namespace Macshot.Windows.Core.Imaging;

using Macshot.Windows.Core.Annotations;

/// <summary>
/// A 3 × 3 mesh gradient: nine control points inside the unit square, each with a
/// colour, with the colour of every other point interpolated across the four patches
/// they form.
/// </summary>
/// <remarks>
/// <para>
/// This is what macshot's first eighteen Beautify styles actually are — SwiftUI
/// <c>MeshGradient</c>, macOS 15 and later. Until now they were drawn here as the
/// three-stop linear gradient macshot itself falls back to on macOS 14, which is the
/// right colours in the wrong arrangement: a mesh's whole character is that the colours
/// bulge and swirl rather than running in a straight line.
/// </para>
/// <para>
/// Two deliberate approximations. The patches are bilinear rather than the Bézier
/// surface SwiftUI builds, and the colour is eased across each patch with a smoothstep
/// rather than by cubic interpolation across the whole grid. The first is invisible at
/// these displacements; the second is what makes the colour's slope match at a patch
/// boundary, which is the only place a seam could show.
/// </para>
/// </remarks>
/// <param name="Points">
/// Nine control points as eighteen numbers — x, y, row-major — in the unit square. The
/// border points must sit on the border, which is what makes the mesh cover the whole
/// background; macshot's all do, and the extraction script refuses any that do not.
/// </param>
/// <param name="Colors">The nine colours, in the same order.</param>
public sealed record BeautifyMesh(double[] Points, AnnotationColor[] Colors)
{
    /// <summary>The colour at a point in the unit square.</summary>
    /// <remarks>
    /// For a swatch, a test, or anything else that wants one point. Rendering a whole
    /// background makes one <see cref="CreateSampler"/> and asks it for every pixel,
    /// which saves the allocation and nothing else: the answers are the same.
    /// </remarks>
    public AnnotationColor Sample(double u, double v) => CreateSampler().Sample(u, v);

    /// <summary>
    /// A sampler over this mesh, for a caller with a whole image to colour.
    /// </summary>
    /// <remarks>
    /// It holds the working values of one inversion, so it is not thread-safe: one
    /// sampler belongs to one scan of one image.
    /// </remarks>
    public BeautifyMeshSampler CreateSampler() => new(this);

    /// <summary>Whether this is a mesh at all, rather than nine of nothing.</summary>
    internal bool IsUsable => Points.Length == 18 && Colors.Length == 9;
}

/// <summary>Walks one image over one <see cref="BeautifyMesh"/>. See its remarks.</summary>
public sealed class BeautifyMeshSampler
{
    /// <summary>
    /// How far outside a patch still counts as inside it. Patches share their edges, so
    /// a point on a seam has to belong to one of them rather than to neither, and the
    /// arithmetic that places it carries rounding of its own.
    /// </summary>
    private const double Slack = 1e-9;

    /// <summary>Below this a coefficient is treated as zero rather than divided by.</summary>
    private const double Negligible = 1e-12;

    private readonly BeautifyMesh _mesh;

    /// <summary>Where the point being placed landed: which patch, and where inside it.</summary>
    private int _row;
    private int _column;
    private double _s = 0.5;
    private double _t = 0.5;

    internal BeautifyMeshSampler(BeautifyMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        _mesh = mesh;
    }

    /// <summary>The colour at a point in the unit square.</summary>
    /// <remarks>
    /// The patches are always tried in the same order, and the first to claim the point
    /// wins. That matters because a mesh this warped has patches that <em>overlap</em> —
    /// around a control point pulled far off centre, two of them cover the same ground —
    /// so more than one can answer for a point, with answers that differ by a shade.
    /// A fixed order makes the picture a function of the mesh alone. Trying whichever
    /// patch the previous pixel was in would be quicker and would make the background
    /// depend on the order its pixels happened to be visited in.
    /// </remarks>
    public AnnotationColor Sample(double u, double v)
    {
        if (!_mesh.IsUsable)
        {
            return new AnnotationColor(0, 0, 0);
        }

        u = Math.Clamp(u, 0, 1);
        v = Math.Clamp(v, 0, 1);

        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < 2; column++)
            {
                if (Solve(row, column, u, v))
                {
                    return ColorAt(_row, _column, Smoothstep(_s), Smoothstep(_t));
                }
            }
        }

        // Nothing claimed it, which the mesh covering the whole unit square says cannot
        // happen. Answering from the last patch tried keeps a colour coming out of a
        // background that has to have one.
        return ColorAt(_row, _column, Smoothstep(Math.Clamp(_s, 0, 1)), Smoothstep(Math.Clamp(_t, 0, 1)));
    }

    /// <summary>
    /// Tries to place the point inside one patch, leaving <see cref="_s"/> and
    /// <see cref="_t"/> where it landed. False when the point is outside that patch.
    /// </summary>
    private bool Solve(int row, int column, double u, double v)
    {
        // A bilinear patch is Q(s,t) = A + Es + Ft + Gst. Eliminating t leaves a
        // quadratic in s, so the inverse is solved outright rather than iterated
        // towards. It was Newton's method to begin with, which is shorter to write and
        // wrong: two of these patches have control points close enough to make the
        // Jacobian nearly singular, and an iteration that steps through one of those
        // flies off the patch and never comes back. A background with a wedge of the
        // wrong colour in it is the sort of fault nobody can point at.
        var (ax, ay) = Point(row, column);
        var (bx, by) = Point(row, column + 1);
        var (cx, cy) = Point(row + 1, column);
        var (dx, dy) = Point(row + 1, column + 1);

        var ex = bx - ax;
        var ey = by - ay;
        var fx = cx - ax;
        var fy = cy - ay;
        var gx = ax - bx - cx + dx;
        var gy = ay - by - cy + dy;

        var qx = u - ax;
        var qy = v - ay;

        var quadratic = (ey * gx) - (gy * ex);
        var linear = (ey * fx) - (fy * ex) + (gy * qx) - (qy * gx);
        var constant = (fy * qx) - (qy * fx);

        if (Math.Abs(quadratic) < Negligible)
        {
            // A patch whose opposite edges are parallel — the ordinary case, and the
            // one where the quadratic degenerates into a straight line.
            return Math.Abs(linear) >= Negligible && Accept(row, column, -constant / linear);
        }

        var discriminant = (linear * linear) - (4 * quadratic * constant);
        if (discriminant < 0)
        {
            return false;
        }

        var square = Math.Sqrt(discriminant);

        // Both roots are tried. One of them is the point's place inside this patch and
        // the other is the place the same surface would put it outside the patch, and
        // which is which depends on the shape rather than on the sign.
        return Accept(row, column, (-linear + square) / (2 * quadratic))
            || Accept(row, column, (-linear - square) / (2 * quadratic));

        // Takes one candidate s, recovers the t that goes with it, and keeps the pair
        // if both land inside the patch.
        bool Accept(int patchRow, int patchColumn, double s)
        {
            if (double.IsNaN(s) || s < -Slack || s > 1 + Slack)
            {
                return false;
            }

            // Whichever axis has the stronger denominator: at a patch corner one of the
            // two is zero, and dividing by it would answer with an infinity.
            var alongX = fx + (gx * s);
            var alongY = fy + (gy * s);
            var t = Math.Abs(alongX) > Math.Abs(alongY)
                ? (qx - (ex * s)) / alongX
                : (qy - (ey * s)) / alongY;

            if (double.IsNaN(t) || t < -Slack || t > 1 + Slack)
            {
                return false;
            }

            _row = patchRow;
            _column = patchColumn;
            _s = Math.Clamp(s, 0, 1);
            _t = Math.Clamp(t, 0, 1);
            return true;
        }
    }

    private AnnotationColor ColorAt(int row, int column, double s, double t)
    {
        var topLeft = _mesh.Colors[(row * 3) + column];
        var topRight = _mesh.Colors[(row * 3) + column + 1];
        var bottomLeft = _mesh.Colors[((row + 1) * 3) + column];
        var bottomRight = _mesh.Colors[((row + 1) * 3) + column + 1];

        return new AnnotationColor(
            Mix(topLeft.Red, topRight.Red, bottomLeft.Red, bottomRight.Red, s, t),
            Mix(topLeft.Green, topRight.Green, bottomLeft.Green, bottomRight.Green, s, t),
            Mix(topLeft.Blue, topRight.Blue, bottomLeft.Blue, bottomRight.Blue, s, t));
    }

    private (double X, double Y) Point(int row, int column)
    {
        var offset = ((row * 3) + column) * 2;
        return (_mesh.Points[offset], _mesh.Points[offset + 1]);
    }

    private static byte Mix(byte topLeft, byte topRight, byte bottomLeft, byte bottomRight, double s, double t)
    {
        var top = topLeft + ((topRight - topLeft) * s);
        var bottom = bottomLeft + ((bottomRight - bottomLeft) * s);
        return (byte)Math.Clamp(Math.Round(top + ((bottom - top) * t)), 0, byte.MaxValue);
    }

    /// <summary>
    /// Eases the colour across a patch. The point of it is the slope at each end: zero
    /// there means two patches meeting at a seam agree about how fast the colour is
    /// changing, and not only about what it is.
    /// </summary>
    private static double Smoothstep(double value) => value * value * (3 - (2 * value));
}
