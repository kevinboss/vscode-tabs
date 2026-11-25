using System.ComponentModel;
using System.Diagnostics;

namespace VsCodeTabs;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Local\vscode-tabs", out var created);
        if (!created && !TakeOver(mutex))
            return;

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        using var manager = new TabManager();
        Application.ThreadException += (_, e) => Fail(manager, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Fail(manager, e.ExceptionObject as Exception);

        using var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
        using var icon = LoadIcon();
        using var tray = new NotifyIcon
        {
            Icon = icon,
            Text = "VS Code Tabs",
            ContextMenuStrip = menu,
            Visible = true,
        };

        manager.Start();
        _ = CommandPipe.Listen(manager);
        Application.Run();
        tray.Visible = false;
    }

    static bool TakeOver(Mutex mutex)
    {
        using var self = Process.GetCurrentProcess();
        var own = VersionOf(self);
        var running = Process.GetProcessesByName(self.ProcessName)
            .Where(process => process.Id != self.Id && process.SessionId == self.SessionId)
            .ToList();
        try
        {
            if (own is null || running.Count == 0 || running.Any(process => !(VersionOf(process) < own)))
                return false;
            if (!CommandPipe.Send("handover"))
                return false;
            try
            {
                return mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                return true;
            }
        }
        finally
        {
            foreach (var process in running)
                process.Dispose();
        }
    }

    static Version? VersionOf(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            if (path is null)
                return null;
            var info = FileVersionInfo.GetVersionInfo(path);
            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    static Icon LoadIcon()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("app.ico");
        return stream is null ? (Icon)SystemIcons.Application.Clone() : new Icon(stream, SystemInformation.SmallIconSize);
    }

    static void Fail(TabManager manager, Exception? exception)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vscode-tabs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "error.log"), $"{DateTime.Now:O} {exception}{Environment.NewLine}");
        }
        finally
        {
            manager.RestoreAll();
            Application.Exit();
        }
    }
}
