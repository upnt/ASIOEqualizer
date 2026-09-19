using System;
using System.Configuration;
using System.Data;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace VbEqualizer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    // Fixed, app-specific names so a second launch can find this instance's mutex/event.
    private const string SingleInstanceMutexName = @"Local\VbEqualizer-SingleInstance-8f2a1c3e-9b4d-4e7a-9c1f-2d6a7b3e5f10";
    private const string ShowRequestEventName = @"Local\VbEqualizer-ShowRequest-8f2a1c3e-9b4d-4e7a-9c1f-2d6a7b3e5f10";

    private Mutex? _singleInstanceMutex;

    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            LogCrash(e.Exception);
            e.Handled = true;
            Shutdown();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            LogCrash(e.ExceptionObject as Exception);
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance already owns the mutex -- ask it to bring itself to the
            // foreground instead of opening a second one (which would fight over the same
            // ASIO device), then exit before the StartupUri window ever gets created.
            try
            {
                using var showEvent = EventWaitHandle.OpenExisting(ShowRequestEventName);
                showEvent.Set();
            }
            catch { /* the running instance hasn't created its event yet -- nothing more we can do */ }

            Environment.Exit(0);
            return;
        }

        var showRequestEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowRequestEventName);
        var listener = new Thread(() =>
        {
            while (true)
            {
                showRequestEvent.WaitOne();
                Dispatcher.Invoke(() =>
                {
                    if (MainWindow is MainWindow mainWindow)
                        mainWindow.ShowFromTray();
                });
            }
        })
        {
            IsBackground = true,
        };
        listener.Start();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch { /* not owned, e.g. if we never became the primary instance */ }
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }

    private static void LogCrash(Exception? ex)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "crash.log"),
                ex?.ToString() ?? "unknown error");
        }
        catch { /* best effort */ }
    }
}

