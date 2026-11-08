using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace NetworkShares;

/// <summary>Everything that needs administrator rights. The window itself runs as a normal app, so that drag and drop
/// from Explorer works (Windows blocks drops into elevated windows); this work happens in an elevated helper instead.</summary>
interface IBackend : IDisposable
{
    Snapshot Snapshot();
    Health Health();
    ShareResult Create(ShareSpec spec);
    ShareResult Update(UpdateSpec spec);
    List<string> Remove(RemoveSpec spec);
    void Fix();
    List<string> UndoFixes();
    List<string> CleanUp(bool deleteUsers);
    string ResetGuest();
}

/// <summary>Does the work in this process: used by the helper, and when the app was started as administrator anyway.</summary>
sealed class LocalBackend : IBackend
{
    public Snapshot Snapshot() => Sharing.Snapshot();
    public Health Health() => Fixes.Check();
    public ShareResult Create(ShareSpec spec) => Sharing.Create(spec);
    public ShareResult Update(UpdateSpec spec) => Sharing.Update(spec);
    public List<string> Remove(RemoveSpec spec) => Sharing.Remove(spec);
    public void Fix() => Fixes.Apply(Fixes.Check());
    public List<string> UndoFixes() => Fixes.Undo();
    public List<string> CleanUp(bool deleteUsers) => Sharing.CleanUp(deleteUsers);
    public string ResetGuest() => Sharing.ResetGuestPassword();
    public void Dispose() { }

    public static bool Elevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
}

sealed record Request(string Op, JsonElement? Arg);
sealed record Response(bool Ok, string? Error, JsonElement? Result);

/// <summary>
/// The window's side of the helper. The window opens a pipe with a random name that only this user can open, starts the
/// helper elevated (the one UAC prompt), and accepts exactly one connection: from that helper's process. The helper in
/// turn checks that the pipe belongs to the window that started it. One JSON request and response per line.
/// </summary>
sealed class RemoteBackend : IBackend
{
    readonly NamedPipeServerStream pipe;
    readonly StreamReader reader;
    readonly StreamWriter writer;
    readonly Lock gate = new();

    RemoteBackend(NamedPipeServerStream pipe)
    {
        this.pipe = pipe;
        reader = new StreamReader(pipe);
        writer = new StreamWriter(pipe) { AutoFlush = true };
    }

    /// <summary>Starts the helper. Throws <see cref="OperationCanceledException"/> if the UAC prompt was declined.</summary>
    public static async Task<RemoteBackend> StartAsync()
    {
        var name = "NetworkShares-" + Guid.NewGuid().ToString("N");
        using var me = WindowsIdentity.GetCurrent();
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(me.User!, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        var pipe = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
        try
        {
            Process helper;
            try
            {
                helper = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--helper {name} {Environment.ProcessId}")
                {
                    UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
                })!;
            }
            catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)   // ERROR_CANCELLED: UAC declined
            {
                throw new OperationCanceledException("Administrator rights were declined.");
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var connect = pipe.WaitForConnectionAsync(cts.Token);
            while (!connect.IsCompleted)
            {
                await Task.WhenAny(connect, Task.Delay(250));
                if (!connect.IsCompleted && helper.HasExited) throw new InvalidOperationException("The administrator helper stopped before it connected.");
            }
            await connect;
            if (!Native.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint pid) || pid != helper.Id)
                throw new InvalidOperationException("An unexpected process connected to the helper pipe.");
            return new RemoteBackend(pipe);
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    T Call<T>(string op, object? arg = null)
    {
        lock (gate)
        {
            var request = new Request(op, arg == null ? null : JsonSerializer.SerializeToElement(arg));
            writer.WriteLine(JsonSerializer.Serialize(request));
            var line = reader.ReadLine() ?? throw new InvalidOperationException("The administrator helper stopped. Restart the app.");
            var response = JsonSerializer.Deserialize<Response>(line)!;
            if (!response.Ok) throw new InvalidOperationException(response.Error);
            return response.Result is { ValueKind: not JsonValueKind.Null } r ? r.Deserialize<T>()! : default!;
        }
    }

    public Snapshot Snapshot() => Call<Snapshot>("snapshot");
    public Health Health() => Call<Health>("health");
    public ShareResult Create(ShareSpec spec) => Call<ShareResult>("create", spec);
    public ShareResult Update(UpdateSpec spec) => Call<ShareResult>("update", spec);
    public List<string> Remove(RemoveSpec spec) => Call<List<string>>("remove", spec);
    public void Fix() => Call<object>("fix");
    public List<string> UndoFixes() => Call<List<string>>("undo-fixes");
    public List<string> CleanUp(bool deleteUsers) => Call<List<string>>("clean-up", deleteUsers);
    public string ResetGuest() => Call<string>("reset-guest");

    public void Dispose() => pipe.Dispose();   // the helper sees the pipe close and exits
}

/// <summary>The elevated helper: no window; serves the pipe of the window that started it, and exits with it.</summary>
static class Helper
{
    public static int Run(string pipeName, int parentPid)
    {
        if (!pipeName.StartsWith("NetworkShares-", StringComparison.Ordinal)) return 2;
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        try
        {
            pipe.Connect(10_000);
        }
        catch (TimeoutException)
        {
            return 3;
        }
        if (!Native.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint server) || server != parentPid) return 4;

        var backend = new LocalBackend();
        using var reader = new StreamReader(pipe);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };
        while (reader.ReadLine() is { } line)
        {
            Response response;
            try
            {
                var request = JsonSerializer.Deserialize<Request>(line)!;
                object? result = request.Op switch
                {
                    "snapshot" => backend.Snapshot(),
                    "health" => backend.Health(),
                    "create" => backend.Create(Arg<ShareSpec>(request)),
                    "update" => backend.Update(Arg<UpdateSpec>(request)),
                    "remove" => backend.Remove(Arg<RemoveSpec>(request)),
                    "fix" => Done(backend.Fix),
                    "undo-fixes" => backend.UndoFixes(),
                    "clean-up" => backend.CleanUp(Arg<bool>(request)),
                    "reset-guest" => backend.ResetGuest(),
                    _ => throw new InvalidOperationException($"Unknown request \"{request.Op}\"."),
                };
                response = new Response(true, null, result == null ? null : JsonSerializer.SerializeToElement(result, result.GetType()));
            }
            catch (Exception e)
            {
                response = new Response(false, e.Message, null);
            }
            try
            {
                writer.WriteLine(JsonSerializer.Serialize(response));
            }
            catch (IOException)
            {
                break;   // the window closed
            }
        }
        return 0;
    }

    static T Arg<T>(Request r) => r.Arg is { } a ? a.Deserialize<T>()! : throw new InvalidOperationException("Missing argument.");

    static object? Done(Action a)
    {
        a();
        return null;
    }
}
