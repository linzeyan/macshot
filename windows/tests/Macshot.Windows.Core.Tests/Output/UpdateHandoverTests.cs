using Macshot.Windows.Core.Output;

namespace Macshot.Windows.Core.Tests.Output;

[TestClass]
public sealed class UpdateHandoverTests
{
    /// <summary>
    /// The one argument that is certain to contain spaces is the target directory —
    /// <c>C:\Program Files\macshot</c>, or any user whose name has a space in it. Building
    /// and reading the arguments as a list is what keeps that from needing quoting rules
    /// that only fail on someone else's machine.
    /// </summary>
    [TestMethod]
    public void AHandoverSurvivesADirectoryWithSpacesInIt()
    {
        var handover = new UpdateHandover(@"C:\Users\Ricky Chen\Apps\macshot 1.0", 4321);

        Assert.AreEqual(handover, UpdateHandover.Parse(handover.Arguments));
    }

    /// <summary>
    /// An ordinary launch — a hotkey, the shell, a macshot:// URL — must not be read as an
    /// update being applied, because that path replaces a folder and then restarts.
    /// </summary>
    [TestMethod]
    public void AnOrdinaryLaunchIsNotAHandover()
    {
        Assert.IsNull(UpdateHandover.Parse(null));
        Assert.IsNull(UpdateHandover.Parse([]));
        Assert.IsNull(UpdateHandover.Parse([@"C:\macshot\Macshot.Windows.exe"]));
        Assert.IsNull(UpdateHandover.Parse([@"C:\macshot\Macshot.Windows.exe", "macshot://capture"]));
    }

    /// <summary>
    /// A handover missing either half cannot be carried out, and answering null starts
    /// macshot normally. Refusing to run instead would leave someone whose update went
    /// wrong with a program that will not open at all.
    /// </summary>
    [TestMethod]
    public void AHandoverThatCannotBeReadStartsMacshotInstead()
    {
        Assert.IsNull(UpdateHandover.Parse(["--apply-update"]));
        Assert.IsNull(UpdateHandover.Parse(["--apply-update", "--target", @"C:\macshot"]));
        Assert.IsNull(UpdateHandover.Parse(["--apply-update", "--wait", "42"]));
        Assert.IsNull(UpdateHandover.Parse(["--apply-update", "--target", @"C:\macshot", "--wait", "soon"]));

        // The switch present but its value missing, which is what a truncated command line
        // looks like: --target is last, so there is nothing after it to read.
        Assert.IsNull(UpdateHandover.Parse(["--apply-update", "--wait", "42", "--target"]));
    }

    /// <summary>
    /// The tag is the only part of the staging path macshot does not choose — it comes off
    /// the network — and it is used to name a folder that later gets deleted.
    /// </summary>
    [TestMethod]
    public void AReleaseTagCannotNameAFolderOutsideTheStagingArea()
    {
        var root = UpdateStaging.Root(@"C:\Users\ricky\AppData\Local");

        foreach (var tag in new[] { "..", ".", "../../Windows", @"..\..\Windows", "v1.0.0" })
        {
            var folder = UpdateStaging.ForRelease(@"C:\Users\ricky\AppData\Local", tag);

            Assert.AreEqual(root, Path.GetDirectoryName(folder), tag);
            Assert.IsFalse(Path.GetFileName(folder) is "." or "..", tag);
        }
    }

    /// <summary>
    /// The zip and the unpacked build sit inside the release's own folder, so removing
    /// that folder is the whole of clearing an update up.
    /// </summary>
    [TestMethod]
    public void EverythingDownloadedForAReleaseIsUnderOneFolder()
    {
        const string Local = @"C:\Users\ricky\AppData\Local";

        var folder = UpdateStaging.ForRelease(Local, "v1.0.0");

        Assert.AreEqual(folder, Path.GetDirectoryName(UpdateStaging.Archive(Local, "v1.0.0")));
        Assert.AreEqual(folder, Path.GetDirectoryName(UpdateStaging.Payload(Local, "v1.0.0")));
    }
}
