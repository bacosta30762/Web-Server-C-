using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

class Program
{
    static bool running = true;

    static void Main(string[] args)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "wwwroot");

        HttpListener listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:8080/");
        listener.Start();
        Console.WriteLine("Listening for connections on http://localhost:8080/");

        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            eventArgs.Cancel = true;
            running = false;
            listener.Stop();
        };

        while (running)
        {
            HttpListenerContext? context = null;

            try
            {
                context = listener.GetContext();
            }
            catch (HttpListenerException)
            {
                if (!running)
                {
                    break;
                }

                throw;
            }

            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            Console.WriteLine($"{DateTime.Now}: Received request for {request.Url}");

            try
            {
                if (TryServeFile(root, request, response))
                {
                    response.OutputStream.Close();
                    continue;
                }

                string responseString = "<html><body><h1>Welcome to the Simple Web Server</h1></body></html>";
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                Stream output = response.OutputStream;
                output.Write(buffer, 0, buffer.Length);
                output.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling request: {ex.Message}");

                if (!response.OutputStream.CanWrite)
                {
                    continue;
                }

                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes("<html><body><h1>500 - Internal Server Error</h1></body></html>");
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
        }

        listener.Close();
    }

    static bool TryServeFile(string root, HttpListenerRequest request, HttpListenerResponse response)
    {
        if (request.Url == null)
        {
            return false;
        }

        string localPath = request.Url.LocalPath;
        if (string.IsNullOrEmpty(localPath) || localPath == "/")
        {
            localPath = "/index.html";
        }

        string relativePath = localPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));

        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            return true;
        }

        if (!File.Exists(fullPath))
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            return true;
        }

        byte[] buffer = File.ReadAllBytes(fullPath);
        response.ContentLength64 = buffer.Length;
        response.ContentType = GetContentType(fullPath);
        response.OutputStream.Write(buffer, 0, buffer.Length);
        return true;
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
        string extension = Path.GetExtension(path) ?? string.Empty;

        if (ContentTypes.TryGetValue(extension, out string value))
        {
            return value;
        }

        return "application/octet-stream";
    }
}

