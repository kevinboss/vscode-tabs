using System.Collections.Concurrent;
using System.IO.Pipes;

namespace VsCodeTabs;

internal static class CommandPipe
{
    const int NewWindowTimeoutMilliseconds = 1000;

    static readonly string Name = $"vscode-tabs-{Environment.UserName}";
    static readonly ConcurrentDictionary<NamedPipeServerStream, StreamWriter> Listeners = new();
    static readonly ConcurrentDictionary<int, TaskCompletionSource> Requests = new();
    static int lastRequest;

    public static async Task Listen(TabManager manager)
    {
        var ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Listen must start on the UI thread.");
        while (true)
        {
            var server = new NamedPipeServerStream(Name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await server.WaitForConnectionAsync().ConfigureAwait(false);
            _ = Serve(server, manager, ui);
        }
    }

    static async Task Serve(NamedPipeServerStream server, TabManager manager, SynchronizationContext ui)
    {
        using (server)
        using (var reader = new StreamReader(server, leaveOpen: true))
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false) ?? "";
            if (line != "listen")
            {
                ui.Post(_ => Run(line, manager), null);
                return;
            }

            Listeners[server] = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
            try
            {
                while (await reader.ReadLineAsync().ConfigureAwait(false) is { } reply)
                    Accept(reply);
            }
            catch (IOException)
            {
            }
            finally
            {
                Listeners.TryRemove(server, out _);
            }
        }
    }

    static void Accept(string reply)
    {
        const string prefix = "accepted ";
        if (reply.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(reply[prefix.Length..], out var id)
            && Requests.TryRemove(id, out var request))
            request.TrySetResult();
    }

    public static async Task<bool> RequestNewWindow(string folder)
    {
        if (Listeners.IsEmpty)
            return false;

        var id = Interlocked.Increment(ref lastRequest);
        var request = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Requests[id] = request;
        foreach (var (server, writer) in Listeners)
        {
            try
            {
                lock (writer)
                    writer.WriteLine($"new-window {id} {folder}");
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                Listeners.TryRemove(server, out _);
            }
        }

        var accepted = await Task.WhenAny(request.Task, Task.Delay(NewWindowTimeoutMilliseconds)) == request.Task;
        Requests.TryRemove(id, out _);
        return accepted;
    }

    static void Run(string line, TabManager manager)
    {
        switch (line)
        {
            case "next":
                manager.Cycle(1);
                break;
            case "previous":
                manager.Cycle(-1);
                break;
            case "handover":
                manager.HandOver();
                break;
            case "theme":
                manager.ThemeChanged();
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
