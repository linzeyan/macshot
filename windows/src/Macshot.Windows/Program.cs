using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace Macshot.Windows;

/// <summary>
/// The entry point, written out rather than generated.
/// </summary>
/// <remarks>
/// <para>
/// WinUI's markup compiler emits this same <c>Main</c> into <c>App.g.i.cs</c>, behind
/// <c>#if !DISABLE_XAML_GENERATED_MAIN</c>. The body below is a copy of what it emits for
/// this project, and it is a copy on purpose: the sequence is
/// <c>InitializeComWrappers</c> then <c>Application.Start</c> with a synchronization
/// context set inside the callback, and getting any of that subtly wrong is a class of
/// failure that shows up as a window that never appears rather than as a compiler error.
/// If the SDK's version of it ever changes, this is the file that has to be re-copied —
/// the property in the csproj is what makes that a deliberate decision instead of a silent
/// divergence.
/// </para>
/// <para>
/// The reason to take it over is the line above it. <see cref="VelopackApp"/> has to run
/// before any UI exists, because a launch may be the updater re-invoking macshot to say a
/// version was installed or is about to be removed, and those launches do their work and
/// end rather than opening anything. There is nowhere earlier than <c>Main</c> to put it,
/// and the generated <c>Main</c> cannot be edited.
/// </para>
/// <para>
/// It runs in every build, packaged or not. Outside a Velopack layout it finds no manifest,
/// does nothing and returns — so an unpackaged macshot started from a folder behaves
/// exactly as it did before this file existed.
/// </para>
/// </remarks>
public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
