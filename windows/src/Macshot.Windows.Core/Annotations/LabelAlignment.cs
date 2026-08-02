namespace Macshot.Windows.Core.Annotations;

/// <summary>
/// Which edge a label's lines are hung from. macshot's three —
/// <c>ToolOptionsRowView.swift:947</c> — in macshot's order, because the toolbar picks
/// one by position.
/// </summary>
/// <remarks>
/// <para>
/// Only a label of more than one line shows the difference, since a single line is
/// exactly as wide as itself and lands in the same place whichever edge it is hung from.
/// That is not a reason to leave the control out: a callout that has to say two things is
/// what it exists for, and lining two typed lines up by eye is the one thing nobody can
/// do afterwards.
/// </para>
/// <para>
/// Named for the label rather than for the text because <c>TextAlignment</c> is taken —
/// WinUI has a type of that name, and a second one here would be ambiguous in every file
/// that draws a label.
/// </para>
/// </remarks>
public enum LabelAlignment
{
    /// <summary>Every line starts at the same left edge. What typing gives you.</summary>
    Left,

    /// <summary>Every line is centred on the widest one.</summary>
    Centre,

    /// <summary>Every line ends at the same right edge.</summary>
    Right,
}
