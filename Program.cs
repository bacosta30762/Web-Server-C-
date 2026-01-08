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

class Program
{
    static bool running = true;

    static void Main(string[] args)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "wwwroot");

        TcpListener listener = new TcpListener(IPAddress.Any, 8080);
        listener.Start();
        Console.WriteLine("Listening for connections on port 8080...");

        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            eventArgs.Cancel = true;
            running = false;
            listener.Stop();
        };

        while (running)
        {
            try
            {
                TcpClient client = listener.AcceptTcpClient();
                Console.WriteLine($"{DateTime.Now}: Client connected from {client.Client.RemoteEndPoint}");

                ThreadPool.QueueUserWorkItem(state => HandleClient(client, root));
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

    static void HandleClient(TcpClient client, string root)
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

            string responseBody = "<html><body><h1>Welcome to the Simple Web Server</h1></body></html>";
            Dictionary<string, string> responseHeaders = new Dictionary<string, string>
            {
                ["Content-Type"] = "text/html",
                ["Content-Length"] = Encoding.UTF8.GetByteCount(responseBody).ToString()
            };

            SendResponse(stream, 200, "OK", responseHeaders, responseBody);
            stream.Close();
            client.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling client: {ex.Message}");
            client.Close();
        }
    }

    static readonly Dictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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

    static HttpRequest? ParseRequest(string requestText)
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

    static void SendResponse(NetworkStream stream, int statusCode, string statusMessage, Dictionary<string, string> headers, string body)
    {
        StringBuilder responseBuilder = new StringBuilder();
        responseBuilder.Append($"HTTP/1.1 {statusCode} {statusMessage}\r\n");

        foreach (var header in headers)
        {
            responseBuilder.Append($"{header.Key}: {header.Value}\r\n");
        }

        responseBuilder.Append("\r\n");
        responseBuilder.Append(body);

        string response = responseBuilder.ToString();
        byte[] responseBytes = Encoding.UTF8.GetBytes(response);
        stream.Write(responseBytes, 0, responseBytes.Length);
    }

    static void SendErrorResponse(NetworkStream stream, int statusCode, string statusMessage)
    {
        string body = $"<html><body><h1>{statusCode} - {statusMessage}</h1></body></html>";
        Dictionary<string, string> headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "text/html",
            ["Content-Length"] = Encoding.UTF8.GetByteCount(body).ToString()
        };
        SendResponse(stream, statusCode, statusMessage, headers, body);
    }

    static string GetContentType(string path)
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

