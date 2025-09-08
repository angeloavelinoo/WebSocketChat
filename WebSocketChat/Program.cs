using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
using System.Text.Json;

int port = 5124;
var listener = new TcpListener(IPAddress.Any, port);
listener.Start();
Console.WriteLine($"[SERVIDOR] Ouvindo em 0.0.0.0:{port} (TCP)\n");

var clients = new ConcurrentDictionary<TcpClient, NetworkStream>();

_ = Task.Run(async () =>
{
    while (true)
    {
        var tcp = await listener.AcceptTcpClientAsync();
        var stream = tcp.GetStream();
        clients[tcp] = stream;
        Console.WriteLine($"[+] Cliente conectado de {tcp.Client.RemoteEndPoint}");

        _ = Task.Run(async () =>
        {
            try
            {
                while (tcp.Connected)
                {
                    string? json = await ReadFramedAsync(stream);
                    if (json == null) break;

                    var msg = JsonSerializer.Deserialize<ChatMessage>(json);
                    if (msg != null)
                    {
                        if (string.Equals(msg.Cipher, "rc4", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var bytes = ParseCsvBytes(msg.Payload);
                                Console.WriteLine($"[RC4 DECIMAL] {msg.Sender}: {string.Join(",", bytes)}");
                            }
                            catch
                            {
                                Console.WriteLine($"[RC4 DECIMAL] {msg.Sender}: (payload inválido) {msg.Payload}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[{msg.Cipher}] {msg.Sender}: {msg.Payload}");
                        }
                    }

                    foreach (var kv in clients)
                    {
                        if (kv.Key == tcp) continue;
                        try { await WriteFramedAsync(kv.Value, json); }
                        catch { /* ignora cliente com erro */ }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO CLIENTE] {ex.Message}");
            }
            finally
            {
                clients.TryRemove(tcp, out _);
                try { tcp.Close(); } catch { }
                Console.WriteLine("[-] Cliente desconectado");
            }
        });
    }
});

Console.WriteLine("Pressione ENTER para encerrar o servidor...");
Console.ReadLine();
listener.Stop();
    
static async Task<string?> ReadFramedAsync(NetworkStream stream)
{
    byte[] lenBuf = new byte[4];
    int read = await ReadExactAsync(stream, lenBuf, 0, 4);
    if (read == 0) return null;
    if (read < 4) throw new IOException("Frame incompleto");
    if (BitConverter.IsLittleEndian) Array.Reverse(lenBuf);
    int len = BitConverter.ToInt32(lenBuf, 0);
    if (len <= 0 || len > 10_000_000) throw new IOException("Tamanho inválido");
    byte[] data = new byte[len];
    read = await ReadExactAsync(stream, data, 0, len);
    if (read < len) throw new IOException("Payload incompleto");
    return Encoding.UTF8.GetString(data);
}

static async Task<int> ReadExactAsync(NetworkStream s, byte[] buf, int off, int count)
{
    int total = 0;
    while (total < count)
    {
        int r = await s.ReadAsync(buf, off + total, count - total);
        if (r == 0) break;
        total += r;
    }
    return total;
}

static async Task WriteFramedAsync(NetworkStream stream, string json)
{
    byte[] data = Encoding.UTF8.GetBytes(json);
    byte[] len = BitConverter.GetBytes(data.Length);
    if (BitConverter.IsLittleEndian) Array.Reverse(len);
    await stream.WriteAsync(len, 0, 4);
    await stream.WriteAsync(data, 0, data.Length);
    await stream.FlushAsync();
}

static byte[] ParseCsvBytes(string s)
{
    var parts = s.Split(new[] { ',', ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    var list = new List<byte>(parts.Length);
    foreach (var p in parts)
    {
        if (!byte.TryParse(p, out var b))
            throw new FormatException("Valor RC4 inválido (não é byte 0..255).");
        list.Add(b);
    }
    return list.ToArray();
}


public record ChatMessage(string Cipher, string Sender, string Payload);
