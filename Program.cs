using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

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

                HandleClient(client, root);
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

            string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            Console.WriteLine($"Received request:\n{request}");

            string responseString = "<html><body><h1>Welcome to the Simple Web Server</h1></body></html>";
            string httpResponse = $"HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: {Encoding.UTF8.GetByteCount(responseString)}\r\n\r\n{responseString}";
            byte[] responseBytes = Encoding.UTF8.GetBytes(httpResponse);
            stream.Write(responseBytes, 0, responseBytes.Length);
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

