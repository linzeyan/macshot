namespace Macshot.Windows.Core.Output;

/// <summary>
/// What Save means — macshot's <c>SaveActionPreference</c>.
/// </summary>
/// <remarks>
/// The folder by default, because a capture tool that opens a dialog for the twentieth
/// screenshot of the morning is a capture tool nobody uses. Asking is for the person
/// filing each capture where it belongs as they take it, for whom a folder full of
/// timestamps is a second job.
/// </remarks>
public enum SaveAction
{
    SaveToFolder,

    AskWhereToSave,
}
