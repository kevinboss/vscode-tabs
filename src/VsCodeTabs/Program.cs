namespace VsCodeTabs;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Local\vscode-tabs", out var created);
        if (!created)
            return;

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        using var group = new TabGroup();
        Application.ThreadException += (_, e) => Fail(group, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Fail(group, e.ExceptionObject as Exception);

        using var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
        using var tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "VS Code Tabs",
            ContextMenuStrip = menu,
            Visible = true,
        };

        group.Start();
        _ = CommandPipe.Listen(group);
        Application.Run();
        tray.Visible = false;
    }

    static void Fail(TabGroup group, Exception? exception)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vscode-tabs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "error.log"), $"{DateTime.Now:O} {exception}{Environment.NewLine}");
        }
        finally
        {
            group.RestoreAll();
            Application.Exit();
        }
    }
}
