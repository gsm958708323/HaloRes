using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace GameFramework.Asset.Tests.PlayMode
{
    internal sealed class LocalContentServer : IDisposable
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly string _root;
        private bool _disposed;
        private readonly ConcurrentDictionary<string, int> _requests = new ConcurrentDictionary<string, int>();
        internal string RootUrl { get; }
        internal int ResponseDelayMilliseconds;

        internal LocalContentServer(string root)
        {
            _root = Path.GetFullPath(root);
            _listener.Start();
            RootUrl = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptAsync();
        }

        internal int Count(string path) => _requests.TryGetValue(path, out var count) ? count : 0;

        private async Task AcceptAsync()
        {
            while (!_disposed)
            {
                try { var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); _ = ReplyAsync(client); }
                catch (Exception) when (_disposed) { return; }
            }
        }

        private async Task ReplyAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                    {
                        var line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null) return;
                        var requestPath = line.Split(' ')[1].Split('?')[0];
                        while (!string.IsNullOrEmpty(await reader.ReadLineAsync().ConfigureAwait(false))) { }
                        _requests.AddOrUpdate(requestPath, 1, (_, count) => count + 1);
                        if (ResponseDelayMilliseconds > 0) await Task.Delay(ResponseDelayMilliseconds).ConfigureAwait(false);
                        if (_disposed) return;
                        var path = Path.GetFullPath(Path.Combine(_root, Uri.UnescapeDataString(requestPath.TrimStart('/'))));
                        var found = path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(path);
                        var bytes = found ? File.ReadAllBytes(path) : Array.Empty<byte>();
                        var header = Encoding.ASCII.GetBytes("HTTP/1.1 " + (found ? "200 OK" : "404 Not Found") + "\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
                        await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            }
        }

        public void Dispose() { _disposed = true; _listener.Stop(); }
    }
}
