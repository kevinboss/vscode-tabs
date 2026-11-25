using System.IO.Pipes;

namespace VsCodeTabs;

internal static class CommandPipe
{
    static readonly string Name = $"vscode-tabs-{Environment.UserName}";

    public static async Task Listen(TabManager manager)
    {
        while (true)
        {
            var server = new NamedPipeServerStream(Name, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await server.WaitForConnectionAsync();
            _ = Serve(server, manager);
        }
    }

    static async Task Serve(NamedPipeServerStream server, TabManager manager)
    {
        using var reader = new StreamReader(server);
        var line = await reader.ReadLineAsync() ?? "";
        var space = line.IndexOf(' ');
        var command = space < 0 ? line : line[..space];
        var argument = space < 0 ? "" : line[(space + 1)..];
        switch (command)
        {
            case "next":
                manager.Cycle(1);
                break;
            case "previous":
                manager.Cycle(-1);
                break;
            case "closing":
                manager.Closing(argument);
                break;
            case "handover":
                manager.HandOver();
                break;
        }
    }

    public static bool Send(string command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(1000);
            using var writer = new StreamWriter(client);
            writer.WriteLine(command);
            return true;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
