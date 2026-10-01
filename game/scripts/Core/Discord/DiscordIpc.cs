using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PaxPixelia.Core.Discord;

/// <summary>
/// The Discord desktop client's local RPC (the pipe \\.\pipe\discord-ipc-N on Windows, the socket
/// $XDG_RUNTIME_DIR/discord-ipc-N elsewhere): handshake, then SET_ACTIVITY frames. One background task owns the
/// connection: it retries every <see cref="RetryMs"/> while Discord is closed, drains Discord's replies (an unread pipe
/// would fill up and block the writes) and sends the newest activity no faster than <see cref="MinGapMs"/> (Discord takes
/// five updates per 20 s). Nothing here touches Godot: <see cref="Post"/> is the only call from the main thread.
/// </summary>
public sealed class DiscordIpc : IDisposable
{
    const int OpHandshake = 0, OpFrame = 1, OpClose = 2;
    const int RetryMs = 20_000, MinGapMs = 15_000, MaxFrame = 64 * 1024;

    readonly string _clientId;
    readonly CancellationTokenSource _stop = new();
    readonly Task _loop;
    string _wanted;            // JSON of the activity to show ("null" clears it); written by Post, read by the loop

    /// <summary>A message for the log (connected, refused client id…); raised on the background task.</summary>
    public event Action<string> Log;

    public DiscordIpc(string clientId)
    {
        _clientId = clientId;
        _loop = Task.Run(() => Run(_stop.Token));
    }

    /// <summary>The activity to show (null clears it). Only the latest one counts; equal posts cost nothing.</summary>
    public void Post(JsonObject activity)
    {
        var json = activity?.ToJsonString() ?? "null";
        if (json == Volatile.Read(ref _wanted)) return;
        Volatile.Write(ref _wanted, json);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _loop.Wait(1000); } catch (AggregateException) { }
        _stop.Dispose();
    }

    async Task Run(CancellationToken ct)
    {
        bool refused = false;
        while (!ct.IsCancellationRequested)
        {
            Stream s = await Connect(ct);
            if (s != null)
            {
                try { refused = await Session(s, ct); }
                catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidDataException) { }
                catch (OperationCanceledException) { }
                finally { s.Dispose(); }
                if (!ct.IsCancellationRequested && !refused) Log?.Invoke("discord: disconnected");
            }
            // a refused client id will not get better by asking again every 20 s
            try { await Task.Delay(refused ? RetryMs * 15 : RetryMs, ct); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One connection: handshake, wait for READY, then keep sending the newest activity until Discord goes
    /// away. Returns true when Discord closed it on purpose (bad client id).</summary>
    async Task<bool> Session(Stream s, CancellationToken ct)
    {
        await Write(s, OpHandshake, new JsonObject { ["v"] = 1, ["client_id"] = _clientId }.ToJsonString(), ct);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = Drain(s, ready, ct);
        await Task.WhenAny(reader, ready.Task);
        string sent = null;
        long lastSend = -MinGapMs;
        int pid = Environment.ProcessId;
        while (!reader.IsCompleted)
        {
            var want = Volatile.Read(ref _wanted);
            long now = Environment.TickCount64;
            if (want != null && want != sent && now - lastSend >= MinGapMs)
            {
                var cmd = new JsonObject
                {
                    ["cmd"] = "SET_ACTIVITY",
                    ["args"] = new JsonObject { ["pid"] = pid, ["activity"] = JsonNode.Parse(want) },
                    ["nonce"] = Guid.NewGuid().ToString(),
                };
                await Write(s, OpFrame, cmd.ToJsonString(), ct);
                sent = want; lastSend = now;
            }
            int wait = want != sent ? (int)Math.Clamp(MinGapMs - (now - lastSend), 50, MinGapMs) : 1000;
            await Task.WhenAny(reader, Task.Delay(wait, ct));
        }
        return await reader;
    }

    /// <summary>Reads Discord's frames until the connection ends: READY completes <paramref name="ready"/>, errors go to
    /// the log, the rest is dropped. True when it ended with a CLOSE frame.</summary>
    async Task<bool> Drain(Stream s, TaskCompletionSource ready, CancellationToken ct)
    {
        var head = new byte[8];
        var body = new byte[4096];
        while (true)
        {
            await s.ReadExactlyAsync(head, ct);
            int op = BinaryPrimitives.ReadInt32LittleEndian(head);
            int len = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(4));
            if (len < 0 || len > MaxFrame) throw new InvalidDataException("discord: bad frame length");
            if (body.Length < len) body = new byte[len];
            await s.ReadExactlyAsync(body.AsMemory(0, len), ct);
            JsonNode msg = null;
            try { msg = JsonNode.Parse(body.AsSpan(0, len)); } catch (System.Text.Json.JsonException) { }
            if (op == OpClose)
            {
                Log?.Invoke($"discord: closed: {msg?["message"]}");
                return true;
            }
            string evt = msg?["evt"]?.GetValue<string>();
            if (evt == "READY" && ready.TrySetResult()) Log?.Invoke("discord: connected");
            else if (evt == "ERROR") Log?.Invoke($"discord: {msg["cmd"]} refused: {msg["data"]?["message"]}");
        }
    }

    static async Task Write(Stream s, int op, string json, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var frame = new byte[8 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), payload.Length);
        payload.CopyTo(frame, 8);
        await s.WriteAsync(frame, ct);
        await s.FlushAsync(ct);
    }

    /// <summary>The first of discord-ipc-0…9 that answers (Discord, PTB and Canary each take one), or null.</summary>
    static async Task<Stream> Connect(CancellationToken ct)
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                    try { await pipe.ConnectAsync(200, ct); return pipe; }
                    catch { pipe.Dispose(); throw; }
                }
                var path = Path.Combine(UnixDir(), $"discord-ipc-{i}");
                if (!File.Exists(path)) continue;
                var sock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try { await sock.ConnectAsync(new UnixDomainSocketEndPoint(path), ct); return new NetworkStream(sock, ownsSocket: true); }
                catch { sock.Dispose(); throw; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return null; }
            catch (Exception e) when (e is TimeoutException or IOException or SocketException or UnauthorizedAccessException) { }
        }
        return null;
    }

    static string UnixDir()
    {
        foreach (var v in new[] { "XDG_RUNTIME_DIR", "TMPDIR", "TMP", "TEMP" })
            if (Environment.GetEnvironmentVariable(v) is { Length: > 0 } dir) return dir;
        return "/tmp";
    }
}
