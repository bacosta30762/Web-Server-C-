using System;
using System.IO;
using System.Net;

class Program
{
    static void Main(string[] args)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "wwwroot");

        HttpListener listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:8080/");
        listener.Start();
        Console.WriteLine("Listening for connections on http://localhost:8080/");

        while (true)
        {
            HttpListenerContext context = listener.GetContext();
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            Console.WriteLine($"{DateTime.Now}: Received request for {request.Url}");

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
    }

    static bool TryServeFile(string root, HttpListenerRequest request, HttpListenerResponse response)
    {
        if (request.Url == null || request.Url.LocalPath == "/")
        {
            return false;
        }

        string relativePath = request.Url.LocalPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
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
        response.ContentType = "text/html";
        response.OutputStream.Write(buffer, 0, buffer.Length);
        return true;
    }
}

