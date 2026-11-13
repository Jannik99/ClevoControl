using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ClevoControl.Core.Services;

/// <summary>
/// Minimal HTTP/1.1 server bound to the loopback interface only. Used for game state
/// integration and the local scripting API. Avoids HttpListener's URL-ACL/admin requirements.
/// </summary>
public sealed class MiniHttpServer : IDisposable
{
    public delegate (int Status, string Body) Handler(string method, string path, string body);

    private readonly TcpListener _listener;
    private readonly Handler _handler;
    private readonly CancellationTokenSource _cts = new();

    public MiniHttpServer(int port, Handler handler)
    {
        Port = port;
        _handler = handler;
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        _ = AcceptLoop();
    }

    public int Port { get; }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch { return; }
            _ = Task.Run(() => Serve(client));
        }
    }

    private async Task Serve(TcpClient client)
    {
        using var _ = client;
        try
        {
            client.ReceiveTimeout = 3000;
            var stream = client.GetStream();
            var header = new StringBuilder();
            var buf = new byte[1];
            // Read headers byte-wise until CRLFCRLF (requests are tiny).
            while (header.Length < 16384)
            {
                var n = await stream.ReadAsync(buf);
                if (n == 0) return;
                header.Append((char)buf[0]);
                if (header.Length >= 4 && header[^1] == '\n' && header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal)) break;
            }
            var lines = header.ToString().Split("\r\n");
            var request = lines[0].Split(' ');
            if (request.Length < 2) return;
            var length = 0;
            foreach (var l in lines)
            {
                if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(l[15..].Trim(), out length);
            }
            length = Math.Clamp(length, 0, 1 << 20);
            var body = new byte[length];
            var read = 0;
            while (read < length)
            {
                var n = await stream.ReadAsync(body.AsMemory(read));
                if (n == 0) break;
                read += n;
            }
            var (status, text) = _handler(request[0], request[1], Encoding.UTF8.GetString(body, 0, read));
            var payload = Encoding.UTF8.GetBytes(text);
            var resp = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(resp));
            await stream.WriteAsync(payload);
        }
        catch
        {
            // ignore broken clients
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }
}
