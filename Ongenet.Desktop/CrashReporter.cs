using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Ongenet.App.Services;
using Ongenet.App.Views.Windows;
using SharedApp = Ongenet.App.App;

namespace Ongenet.Desktop;

/// <summary>
/// Local-only crash handling for the desktop head.
/// Covers managed unhandled exceptions (startup try/catch, AppDomain, UI dispatcher).
/// Writes a text dump under <c>&lt;config&gt;/crashes</c> and shows <see cref="CrashDialog"/>.
/// Nothing is uploaded. Native AVs / hard process kills may still bypass this path.
/// </summary>
internal static class CrashReporter
{
    private static int _reporting; // 0 = idle, 1 = in progress
    private static int _dispatcherAttached;

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        // Dispatcher attach happens once Avalonia's UI thread exists (classic desktop only).
        SharedApp.OnClassicDesktopFrameworkInit = AttachDispatcher;
    }

    public static void AttachDispatcher()
    {
        if (Interlocked.Exchange(ref _dispatcherAttached, 1) != 0)
            return;

        try
        {
            Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        }
        catch
        {
            // Ignore if dispatcher is unavailable.
        }
    }

    public static void ReportFatal(Exception ex)
    {
        if (Interlocked.Exchange(ref _reporting, 1) != 0)
            return;

        string? logPath = null;
        string dump;
        try
        {
            dump = BuildDump(ex);
            logPath = TryWriteLog(dump);
        }
        catch
        {
            dump = ex.ToString();
        }

        try
        {
            CrashDialog.ShowBlocking(dump, logPath);
        }
        catch
        {
            // Log file is the fallback.
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            ReportFatal(ex);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportFatal(e.Exception);
        // Do not resume a half-dead session; mark handled so Avalonia does not double-fault,
        // then exit after the dialog.
        e.Handled = true;
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown(1);
            else
                Environment.Exit(1);
        }
        catch
        {
            Environment.Exit(1);
        }
    }

    private static string BuildDump(Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ongenet crash report");
        sb.AppendLine("(local file only — not uploaded)");
        sb.AppendLine();
        sb.AppendLine($"Time (UTC):  {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
        sb.AppendLine($"Version:     {Ongenet.App.AppInfo.Version}");
        sb.AppendLine($"OS:          {RuntimeInformation.OSDescription}");
        sb.AppendLine($"Arch:        {RuntimeInformation.OSArchitecture} / process {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"Framework:   {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine();
        sb.AppendLine(ex.ToString());
        return sb.ToString();
    }

    private static string? TryWriteLog(string dump)
    {
        try
        {
            var dir = AppPaths.CrashesDirectory();
            var name = $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt";
            var path = Path.Combine(dir, name);
            File.WriteAllText(path, dump);
            return path;
        }
        catch
        {
            return null;
        }
    }
}
