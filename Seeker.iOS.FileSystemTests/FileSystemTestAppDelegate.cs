using Foundation;
using UIKit;

namespace AnimaSeek.iOS.FileSystemTests;

/// <summary>Runs the filesystem semantics suite once the simulator process has launched.</summary>
[Register("FileSystemTestAppDelegate")]
public sealed class FileSystemTestAppDelegate : UIApplicationDelegate
{
    /// <summary>
    /// Starts the headless test run and terminates the dedicated harness process with its result code.
    /// </summary>
    /// <param name="application">The simulator application instance.</param>
    /// <param name="launchOptions">Optional launch metadata supplied by iOS.</param>
    /// <returns><see langword="true"/> because launch initialization completed.</returns>
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        _ = Task.Run(static async () =>
        {
            int exitCode = FileSystemSemanticsRunner.Run();
            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            Environment.Exit(exitCode);
        });

        return true;
    }
}
