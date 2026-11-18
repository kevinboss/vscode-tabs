using System.IO.Pipes;

namespace VsCodeTabs;

internal static class CommandPipe
{
    static readonly string Name = $"vscode-tabs-{Environment.UserName}";

    public static async Task Listen(TabGroup group)
    {
        while (true)
        {
            var server = new NamedPipeServerStream(Name, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await server.WaitForConnectionAsync();
            _ = Serve(server, group);
        }
    }

    static async Task Serve(NamedPipeServerStream server, TabGroup group)
    {
        using var reader = new StreamReader(server);
        switch (await reader.ReadLineAsync())
        {
            case "next":
                group.Cycle(1);
                break;
            case "previous":
                group.Cycle(-1);
                break;
        }
    }
}
