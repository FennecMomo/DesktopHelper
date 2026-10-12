using System.IO;
using System.Threading;
using System.Windows;
namespace DesktopHelper;
public partial class App : System.Windows.Application
{
    private Mutex? mutex;
    private EventWaitHandle? signal;
    private RegisteredWaitHandle? waiter;
    private EventWaitHandle? exitSignal;
    private RegisteredWaitHandle? exitWaiter;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--self-test")) { Shutdown(SelfTests.Run()); return; }
        if (e.Args.Contains("--integration-test"))
        {
            _ = IntegrationTests.RunAsync(this);
            return;
        }
        if (e.Args.Contains("--quit"))
        {
            if (EventWaitHandle.TryOpenExisting(@"Local\FennecMomo.DesktopHelper.Exit.v1", out var existing)) { existing.Set(); existing.Dispose(); }
            Shutdown(); return;
        }
        mutex = new Mutex(true, @"Local\FennecMomo.DesktopHelper.v1", out var first);
        signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\FennecMomo.DesktopHelper.Show.v1");
        if (!first) { signal.Set(); Shutdown(); return; }
        var store = new StateStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopHelper"));
        MainWindow window;
        try { window = new MainWindow(store, e.Args.Contains("--arrange")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            System.Windows.MessageBox.Show(ex.Message, "DesktopHelper 无法读取布局", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1); return;
        }
        MainWindow = window;
        waiter = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => Dispatcher.BeginInvoke(window.Reveal), null, -1, false);
        exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\FennecMomo.DesktopHelper.Exit.v1");
        exitWaiter = ThreadPool.RegisterWaitForSingleObject(exitSignal, (_, _) => Dispatcher.BeginInvoke(window.Quit), null, -1, false);
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        waiter?.Unregister(null); exitWaiter?.Unregister(null); exitSignal?.Dispose(); signal?.Dispose(); mutex?.Dispose(); base.OnExit(e);
    }
}
