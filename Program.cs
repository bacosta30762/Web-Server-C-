using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class HttpRequest
{
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

class Server
{
    private readonly string rootDirectory;
    private readonly int port;
    private TcpListener? listener;
    private bool running;

    private static readonly Dictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "application/javascript",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".ico"] = "image/x-icon",
        [".txt"] = "text/plain"
    };

    public Server(string rootDirectory, int port = 8080)
    {
        this.rootDirectory = rootDirectory;
        this.port = port;
        this.running = false;
    }

    public void Start()
    {
        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        running = true;
        Console.WriteLine($"Listening for connections on port {port}...");

        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Stop();
        };

        while (running)
        {
            try
            {
                TcpClient client = listener.AcceptTcpClient();
                Console.WriteLine($"{DateTime.Now}: Client connected from {client.Client.RemoteEndPoint}");

                ThreadPool.QueueUserWorkItem(state => HandleClient(client));
            }
            catch (SocketException)
            {
                if (!running)
                {
                    break;
                }
                throw;
            }
        }

        listener.Stop();
    }

    public void Stop()
    {
        running = false;
        listener?.Stop();
    }

    private void HandleClient(TcpClient client)
    {
        try
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[4096];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            
            if (bytesRead == 0)
            {
                client.Close();
                return;
            }

            string requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            Console.WriteLine($"Received request:\n{requestText}");

            HttpRequest? httpRequest = ParseRequest(requestText);
            if (httpRequest == null)
            {
                SendErrorResponse(stream, 400, "Bad Request");
                stream.Close();
                client.Close();
                return;
            }

            Console.WriteLine($"{DateTime.Now}: {httpRequest.Method} {httpRequest.Path}");

            ProcessRequest(httpRequest, stream);
            stream.Close();
            client.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling client: {ex.Message}");
            client.Close();
        }
    }

    private void ProcessRequest(HttpRequest request, NetworkStream stream)
    {
        if (request.Method != "GET")
        {
            SendErrorResponse(stream, 405, "Method Not Allowed");
            return;
        }

        string localPath = request.Path;
        if (string.IsNullOrEmpty(localPath) || localPath == "/")
        {
            localPath = "/index.html";
        }

        string relativePath = localPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        string fullPath = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));

        if (!fullPath.StartsWith(Path.GetFullPath(rootDirectory), StringComparison.OrdinalIgnoreCase))
        {
            SendErrorResponse(stream, 403, "Forbidden");
            return;
        }

        if (!File.Exists(fullPath))
        {
            SendErrorResponse(stream, 404, "Not Found");
            return;
        }

        try
        {
            byte[] fileBytes = File.ReadAllBytes(fullPath);
            string contentType = GetContentType(fullPath);

            Dictionary<string, string> responseHeaders = new Dictionary<string, string>
            {
                ["Content-Type"] = contentType,
                ["Content-Length"] = fileBytes.Length.ToString()
            };

            SendResponse(stream, 200, "OK", responseHeaders, fileBytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading file: {ex.Message}");
            SendErrorResponse(stream, 500, "Internal Server Error");
        }
    }

    private static HttpRequest? ParseRequest(string requestText)
    {
        string[] lines = requestText.Split(new[] { "\r\n" }, StringSplitOptions.None);
        if (lines.Length == 0)
        {
            return null;
        }

        string[] requestLineParts = lines[0].Split(' ');
        if (requestLineParts.Length != 3)
        {
            return null;
        }

        HttpRequest request = new HttpRequest
        {
            Method = requestLineParts[0],
            Path = requestLineParts[1],
            Version = requestLineParts[2]
        };

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrEmpty(lines[i]))
            {
                break;
            }

            int colonIndex = lines[i].IndexOf(':');
            if (colonIndex > 0)
            {
                string headerName = lines[i].Substring(0, colonIndex).Trim();
                string headerValue = lines[i].Substring(colonIndex + 1).Trim();
                request.Headers[headerName] = headerValue;
            }
        }

        return request;
    }

    private static void SendResponse(NetworkStream stream, int statusCode, string statusMessage, Dictionary<string, string> headers, string body)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        SendResponse(stream, statusCode, statusMessage, headers, bodyBytes);
    }

    private static void SendResponse(NetworkStream stream, int statusCode, string statusMessage, Dictionary<string, string> headers, byte[] body)
    {
        StringBuilder responseBuilder = new StringBuilder();
        responseBuilder.Append($"HTTP/1.1 {statusCode} {statusMessage}\r\n");

        foreach (var header in headers)
        {
            responseBuilder.Append($"{header.Key}: {header.Value}\r\n");
        }

        responseBuilder.Append("\r\n");
        string responseHeaders = responseBuilder.ToString();
        byte[] headerBytes = Encoding.UTF8.GetBytes(responseHeaders);

        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Write(body, 0, body.Length);
    }

    private static void SendErrorResponse(NetworkStream stream, int statusCode, string statusMessage)
    {
        string body = $"<html><body><h1>{statusCode} - {statusMessage}</h1></body></html>";
        Dictionary<string, string> headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "text/html",
            ["Content-Length"] = Encoding.UTF8.GetByteCount(body).ToString()
        };
        SendResponse(stream, statusCode, statusMessage, headers, body);
    }

    private static string GetContentType(string path)
    {
        string? extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
        {
            return "application/octet-stream";
        }

        if (ContentTypes.TryGetValue(extension, out string? value) && value != null)
        {
            return value;
        }

        return "application/octet-stream";
    }
}

class Program
{
    static void Main(string[] args)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        Server server = new Server(root, 8080);
        server.Start();
    }
}

