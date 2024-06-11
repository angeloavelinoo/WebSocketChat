using System.Net; 
using System.Net.WebSockets; 
using System.Text; 
using System.Collections.Concurrent; 

var builder = WebApplication.CreateBuilder(args); // Cria um construtor para a aplica��o

var app = builder.Build(); // Constr�i a aplica��o

app.UseWebSockets(); // Habilita o suporte a WebSockets

// Cria um dicion�rio concorrente para gerenciar os clientes conectados
var clients = new ConcurrentDictionary<WebSocket, WebSocket>();

// Mapeia a rota raiz "/" para o tratamento de requisi��es
app.MapGet("/", async context =>
{
    // Verifica se a requisi��o � do tipo WebSocket
    if (!context.WebSockets.IsWebSocketRequest)
    {
        // Se n�o for uma requisi��o WebSocket, responde com status 400 Bad Request
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        return;
    }

    // Aceita a requisi��o WebSocket e obt�m o objeto WebSocket
    var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    // Adiciona o WebSocket ao dicion�rio de clientes
    clients.TryAdd(webSocket, webSocket);
    Console.WriteLine("Usuario Conectado");

    // Cria um buffer para receber mensagens
    var buffer = new byte[1024 * 4];
    try
    {
        // Loop para manter a conex�o aberta enquanto o estado do WebSocket for Open
        while (webSocket.State == WebSocketState.Open)
        {
            // Recebe uma mensagem do cliente
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                // Se a mensagem for de fechamento, fecha o WebSocket
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
            }
            else
            {
                // Converte o buffer recebido em uma string
                var message = Encoding.ASCII.GetString(buffer, 0, result.Count);
                Console.WriteLine($"Mensagem recebida de: {message}");

                // Prepara a mensagem para ser enviada aos outros clientes
                var data = Encoding.ASCII.GetBytes(message);
                foreach (var client in clients.Keys)
                {
                    // Envia a mensagem para todos os clientes exceto o remetente original
                    if (client != webSocket && client.State == WebSocketState.Open)
                    {
                        await client.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
            }
        }
    }
    catch (WebSocketException e)
    {
        // Trata erros de WebSocket
        Console.WriteLine($"WebSocket erro: {e.Message}");
    }
    finally
    {
        // Remove o WebSocket do dicion�rio de clientes e fecha a conex�o
        clients.TryRemove(webSocket, out _);
        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
        webSocket.Dispose();
        Console.WriteLine("Usuario saiu");
    }
});

// Configura o servidor para escutar em todas as interfaces de rede (0.0.0.0) na porta 5124
await app.RunAsync("http://0.0.0.0:5124");
