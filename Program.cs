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
    private readonly DateTime startTime;
    private long totalRequests;
    private readonly object statsLock = new object();

    private static readonly Dictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "application/javascript",
        [".json"] = "application/json",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".ico"] = "image/x-icon",
        [".txt"] = "text/plain",
        [".svg"] = "image/svg+xml",
        [".xml"] = "application/xml"
    };

    public Server(string rootDirectory, int port = 8080)
    {
        this.rootDirectory = rootDirectory;
        this.port = port;
        this.running = false;
        this.startTime = DateTime.Now;
        this.totalRequests = 0;
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
            
            lock (statsLock)
            {
                totalRequests++;
            }

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
        
        // Handle API endpoints
        if (localPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            HandleApiRequest(localPath, stream);
            return;
        }

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

        // Handle directory listing
        if (Directory.Exists(fullPath))
        {
            SendDirectoryListing(fullPath, localPath, stream);
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

    private void HandleApiRequest(string path, NetworkStream stream)
    {
        if (path.Equals("/api/stats", StringComparison.OrdinalIgnoreCase))
        {
            TimeSpan uptime = DateTime.Now - startTime;
            lock (statsLock)
            {
                string json = $@"{{
    ""uptime_seconds"": {(int)uptime.TotalSeconds},
    ""uptime_formatted"": ""{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s"",
    ""total_requests"": {totalRequests},
    ""start_time"": ""{startTime:yyyy-MM-dd HH:mm:ss}"",
    ""port"": {port},
    ""root_directory"": ""{rootDirectory}""
}}";
                Dictionary<string, string> headers = new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/json",
                    ["Content-Length"] = Encoding.UTF8.GetByteCount(json).ToString()
                };
                SendResponse(stream, 200, "OK", headers, json);
            }
        }
        else if (path.Equals("/api/files", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/files/", StringComparison.OrdinalIgnoreCase))
        {
            string subPath = path.Substring("/api/files".Length);
            if (string.IsNullOrEmpty(subPath)) subPath = "/";
            
            string relativePath = subPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
            
            if (!fullPath.StartsWith(Path.GetFullPath(rootDirectory), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(fullPath))
            {
                SendErrorResponse(stream, 404, "Directory Not Found");
                return;
            }
            
            var files = new List<object>();
            var directories = Directory.GetDirectories(fullPath);
            var fileInfos = Directory.GetFiles(fullPath);
            
            foreach (var dir in directories)
            {
                var dirInfo = new DirectoryInfo(dir);
                files.Add(new
                {
                    name = dirInfo.Name,
                    type = "directory",
                    path = Path.Combine(subPath, dirInfo.Name).Replace(Path.DirectorySeparatorChar, '/'),
                    modified = dirInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            
            foreach (var file in fileInfos)
            {
                var fileInfo = new FileInfo(file);
                files.Add(new
                {
                    name = fileInfo.Name,
                    type = "file",
                    size = fileInfo.Length,
                    size_formatted = FormatFileSize(fileInfo.Length),
                    path = Path.Combine(subPath, fileInfo.Name).Replace(Path.DirectorySeparatorChar, '/'),
                    modified = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    extension = fileInfo.Extension
                });
            }
            
            // Simple JSON serialization
            StringBuilder jsonBuilder = new StringBuilder("{\"path\":\"" + subPath + "\",\"items\":[");
            foreach (var item in files)
            {
                // This is a simplified JSON generation - in production you'd use a proper JSON library
                jsonBuilder.Append("{");
                if (item is FileInfo fileItem)
                {
                    jsonBuilder.Append($"\"name\":\"{fileItem.Name}\",\"type\":\"file\",\"size\":{fileItem.Length},");
                }
                jsonBuilder.Append("},");
            }
            if (files.Count > 0) jsonBuilder.Length--; // Remove trailing comma
            jsonBuilder.Append("]}");
            
            // Better approach: manual JSON building
            jsonBuilder.Clear();
            jsonBuilder.Append($"{{\"path\":\"{subPath.Replace("\\", "\\\\").Replace("\"", "\\\"")}\",\"items\":[");
            bool first = true;
            foreach (var dir in directories)
            {
                if (!first) jsonBuilder.Append(",");
                var dirInfo = new DirectoryInfo(dir);
                string dirPath = Path.Combine(subPath, dirInfo.Name).Replace("\\", "/");
                jsonBuilder.Append($"{{\"name\":\"{dirInfo.Name.Replace("\"", "\\\"")}\",\"type\":\"directory\",\"path\":\"{dirPath.Replace("\\", "\\\\").Replace("\"", "\\\"")}\",\"modified\":\"{dirInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}\"}}");
                first = false;
            }
            foreach (var file in fileInfos)
            {
                if (!first) jsonBuilder.Append(",");
                var fileInfo = new FileInfo(file);
                string filePath = Path.Combine(subPath, fileInfo.Name).Replace("\\", "/");
                jsonBuilder.Append($"{{\"name\":\"{fileInfo.Name.Replace("\"", "\\\"")}\",\"type\":\"file\",\"size\":{fileInfo.Length},\"size_formatted\":\"{FormatFileSize(fileInfo.Length)}\",\"path\":\"{filePath.Replace("\\", "\\\\").Replace("\"", "\\\"")}\",\"modified\":\"{fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}\",\"extension\":\"{fileInfo.Extension.Replace("\"", "\\\"")}\"}}");
                first = false;
            }
            jsonBuilder.Append("]}");
            
            string json = jsonBuilder.ToString();
            Dictionary<string, string> headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["Content-Length"] = Encoding.UTF8.GetByteCount(json).ToString()
            };
            SendResponse(stream, 200, "OK", headers, json);
        }
        else
        {
            SendErrorResponse(stream, 404, "API Endpoint Not Found");
        }
    }

    private void SendDirectoryListing(string fullPath, string requestPath, NetworkStream stream)
    {
        StringBuilder html = new StringBuilder();
        html.Append("<!DOCTYPE html><html><head><meta charset='UTF-8'><title>Directory Listing</title>");
        html.Append("<style>");
        html.Append("body{font-family:system-ui,-apple-system,sans-serif;margin:40px;background:#f5f5f5;}");
        html.Append(".container{max-width:900px;margin:0 auto;background:white;padding:30px;border-radius:8px;box-shadow:0 2px 4px rgba(0,0,0,0.1);}");
        html.Append("h1{color:#333;border-bottom:2px solid #4CAF50;padding-bottom:10px;}");
        html.Append(".breadcrumb{margin:20px 0;color:#666;}");
        html.Append(".breadcrumb a{color:#4CAF50;text-decoration:none;}");
        html.Append(".breadcrumb a:hover{text-decoration:underline;}");
        html.Append("table{width:100%;border-collapse:collapse;margin-top:20px;}");
        html.Append("th{text-align:left;padding:12px;background:#f9f9f9;border-bottom:2px solid #ddd;}");
        html.Append("td{padding:10px 12px;border-bottom:1px solid #eee;}");
        html.Append("tr:hover{background:#f9f9f9;}");
        html.Append(".file-icon{color:#2196F3;margin-right:8px;}");
        html.Append(".dir-icon{color:#FF9800;margin-right:8px;}");
        html.Append("a{color:#333;text-decoration:none;}");
        html.Append("a:hover{text-decoration:underline;}");
        html.Append("</style></head><body><div class='container'>");
        html.Append($"<h1>📁 Directory Listing</h1>");
        html.Append($"<div class='breadcrumb'>");
        
        // Breadcrumb navigation
        string[] pathParts = requestPath.Trim('/').Split('/');
        html.Append("<a href='/'>Home</a>");
        string currentPath = "";
        foreach (var part in pathParts)
        {
            if (!string.IsNullOrEmpty(part))
            {
                currentPath += "/" + part;
                html.Append($" / <a href='{currentPath}'>{part}</a>");
            }
        }
        html.Append("</div>");
        
        html.Append("<table><thead><tr><th>Name</th><th>Size</th><th>Modified</th></tr></thead><tbody>");
        
        // Parent directory link
        if (requestPath != "/")
        {
            string parentPath = requestPath.TrimEnd('/');
            int lastSlash = parentPath.LastIndexOf('/');
            if (lastSlash > 0) parentPath = parentPath.Substring(0, lastSlash);
            else parentPath = "/";
            html.Append($"<tr><td><span class='dir-icon'>📁</span><a href='{parentPath}'>..</a></td><td>-</td><td>-</td></tr>");
        }
        
        // Directories
        foreach (var dir in Directory.GetDirectories(fullPath))
        {
            var dirInfo = new DirectoryInfo(dir);
            string dirName = dirInfo.Name;
            string dirPath = requestPath.TrimEnd('/') + "/" + dirName;
            html.Append($"<tr><td><span class='dir-icon'>📁</span><a href='{dirPath}'>{dirName}</a></td><td>-</td><td>{dirInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}</td></tr>");
        }
        
        // Files
        foreach (var file in Directory.GetFiles(fullPath))
        {
            var fileInfo = new FileInfo(file);
            string fileName = fileInfo.Name;
            string filePath = requestPath.TrimEnd('/') + "/" + fileName;
            html.Append($"<tr><td><span class='file-icon'>📄</span><a href='{filePath}'>{fileName}</a></td><td>{FormatFileSize(fileInfo.Length)}</td><td>{fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}</td></tr>");
        }
        
        html.Append("</tbody></table></div></body></html>");
        
        string htmlContent = html.ToString();
        Dictionary<string, string> headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "text/html",
            ["Content-Length"] = Encoding.UTF8.GetByteCount(htmlContent).ToString()
        };
        SendResponse(stream, 200, "OK", headers, htmlContent);
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
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
        string emoji = statusCode switch
        {
            400 => "⚠️",
            403 => "🔒",
            404 => "🔍",
            405 => "❌",
            500 => "💥",
            _ => "❓"
        };
        
        string body = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>{statusCode} - {statusMessage}</title>
    <style>
        body {{
            font-family: system-ui, -apple-system, sans-serif;
            display: flex;
            justify-content: center;
            align-items: center;
            min-height: 100vh;
            margin: 0;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: #333;
        }}
        .error-container {{
            background: white;
            padding: 40px;
            border-radius: 12px;
            box-shadow: 0 10px 40px rgba(0,0,0,0.2);
            text-align: center;
            max-width: 500px;
        }}
        .error-code {{
            font-size: 72px;
            font-weight: bold;
            color: #667eea;
            margin: 0;
        }}
        .error-message {{
            font-size: 24px;
            margin: 20px 0;
            color: #555;
        }}
        .error-emoji {{
            font-size: 64px;
            margin: 20px 0;
        }}
        a {{
            display: inline-block;
            margin-top: 30px;
            padding: 12px 24px;
            background: #667eea;
            color: white;
            text-decoration: none;
            border-radius: 6px;
            transition: background 0.3s;
        }}
        a:hover {{
            background: #5568d3;
        }}
    </style>
</head>
<body>
    <div class='error-container'>
        <div class='error-emoji'>{emoji}</div>
        <h1 class='error-code'>{statusCode}</h1>
        <p class='error-message'>{statusMessage}</p>
        <a href='/'>← Go Home</a>
    </div>
</body>
</html>";
        
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
        // Try to find wwwroot relative to the executable directory
        string root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        
        // If not found, try going up to the project root (useful during development)
        if (!Directory.Exists(root))
        {
            string? projectRoot = Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.FullName;
            if (projectRoot != null)
            {
                string alternateRoot = Path.Combine(projectRoot, "wwwroot");
                if (Directory.Exists(alternateRoot))
                {
                    root = alternateRoot;
                }
            }
        }
        
        if (!Directory.Exists(root))
        {
            Console.WriteLine($"ERROR: wwwroot directory not found!");
            Console.WriteLine($"Searched in: {Path.Combine(AppContext.BaseDirectory, "wwwroot")}");
            return;
        }
        
        Console.WriteLine($"Serving files from: {root}");
        Server server = new Server(root, 8080);
        server.Start();
    }
}

