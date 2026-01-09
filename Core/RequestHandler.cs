using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using WebServer.Models;
using WebServer.Utils;

namespace WebServer.Core
{
    public class RequestHandler
    {
        private readonly string rootDirectory;
        private readonly SessionManager sessionManager;
        private readonly AuthenticationService authService;
        private readonly DateTime startTime;
        private long totalRequests;
        private readonly object statsLock = new object();

        public RequestHandler(string rootDirectory, SessionManager sessionManager, AuthenticationService authService)
        {
            this.rootDirectory = rootDirectory;
            this.sessionManager = sessionManager;
            this.authService = authService;
            this.startTime = DateTime.Now;
            this.totalRequests = 0;
        }

        public void HandleRequest(HttpRequest request, NetworkStream stream)
        {
            if (request == null)
            {
                HttpResponseBuilder.SendErrorResponse(stream, 400, "Bad Request");
                return;
            }

            if (stream == null || !stream.CanWrite)
            {
                return;
            }

            try
            {
                if (request.Method != "GET" && request.Method != "POST")
                {
                    HttpResponseBuilder.SendErrorResponse(stream, 405, "Method Not Allowed");
                    return;
                }

                IncrementRequestCount();

                string localPath = request.Path ?? string.Empty;

                if (localPath.Equals("/login", StringComparison.OrdinalIgnoreCase))
                {
                    HandleLogin(request, stream);
                    return;
                }

                if (localPath.Equals("/logout", StringComparison.OrdinalIgnoreCase))
                {
                    HandleLogout(request, stream);
                    return;
                }

                if (localPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    HandleApiRequest(localPath, request, stream);
                    return;
                }

                Session? session = GetSessionFromRequest(request);
                if (session == null && !IsPublicPath(localPath))
                {
                    RedirectToLogin(stream);
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
                    HttpResponseBuilder.SendErrorResponse(stream, 403, "Forbidden");
                    return;
                }

                if (Directory.Exists(fullPath))
                {
                    SendDirectoryListing(fullPath, localPath, stream);
                    return;
                }

                if (!File.Exists(fullPath))
                {
                    HttpResponseBuilder.SendErrorResponse(stream, 404, "Not Found");
                    return;
                }

                ServeFile(fullPath, stream);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling request: {ex.Message}");
                HttpResponseBuilder.SendErrorResponse(stream, 500, "Internal Server Error");
            }
        }

        private void ServeFile(string fullPath, NetworkStream stream)
        {
            const long maxFileSize = 100 * 1024 * 1024;

            try
            {
                FileInfo fileInfo = new FileInfo(fullPath);
                if (fileInfo.Length > maxFileSize)
                {
                    HttpResponseBuilder.SendErrorResponse(stream, 413, "File Too Large");
                    return;
                }

                byte[] fileBytes = File.ReadAllBytes(fullPath);
                string contentType = MimeTypeHelper.GetContentType(fullPath);

                HttpResponse response = new HttpResponse
                {
                    StatusCode = 200,
                    StatusMessage = "OK"
                };
                response.SetHeader("Content-Type", contentType);
                response.SetHeader("Content-Length", fileBytes.Length.ToString());
                response.Body = fileBytes;

                HttpResponseBuilder.SendResponse(stream, response);
            }
            catch (UnauthorizedAccessException)
            {
                HttpResponseBuilder.SendErrorResponse(stream, 403, "Forbidden");
            }
            catch (FileNotFoundException)
            {
                HttpResponseBuilder.SendErrorResponse(stream, 404, "Not Found");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading file: {ex.Message}");
                HttpResponseBuilder.SendErrorResponse(stream, 500, "Internal Server Error");
            }
        }

        private void HandleApiRequest(string path, HttpRequest request, NetworkStream stream)
        {
            if (path.Equals("/api/stats", StringComparison.OrdinalIgnoreCase))
            {
                HandleStatsRequest(stream);
            }
            else if (path.Equals("/api/files", StringComparison.OrdinalIgnoreCase) || 
                     path.StartsWith("/api/files/", StringComparison.OrdinalIgnoreCase))
            {
                HandleFilesApiRequest(path, stream);
            }
            else
            {
                HttpResponseBuilder.SendErrorResponse(stream, 404, "API Endpoint Not Found");
            }
        }

        private void HandleStatsRequest(NetworkStream stream)
        {
            TimeSpan uptime = DateTime.Now - startTime;
            lock (statsLock)
            {
                string json = $@"{{
    ""uptime_seconds"": {(int)uptime.TotalSeconds},
    ""uptime_formatted"": ""{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s"",
    ""total_requests"": {totalRequests},
    ""start_time"": ""{startTime:yyyy-MM-dd HH:mm:ss}"",
    ""root_directory"": ""{rootDirectory}""
}}";

                HttpResponse response = new HttpResponse
                {
                    StatusCode = 200,
                    StatusMessage = "OK"
                };
                response.SetHeader("Content-Type", "application/json");
                byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
                response.Body = jsonBytes;
                response.SetHeader("Content-Length", jsonBytes.Length.ToString());

                HttpResponseBuilder.SendResponse(stream, response);
            }
        }

        private void HandleFilesApiRequest(string path, NetworkStream stream)
        {
            string subPath = path.Substring("/api/files".Length);
            if (string.IsNullOrEmpty(subPath)) subPath = "/";

            string relativePath = subPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));

            if (!fullPath.StartsWith(Path.GetFullPath(rootDirectory), StringComparison.OrdinalIgnoreCase) || 
                !Directory.Exists(fullPath))
            {
                HttpResponseBuilder.SendErrorResponse(stream, 404, "Directory Not Found");
                return;
            }

            var directories = Directory.GetDirectories(fullPath);
            var fileInfos = Directory.GetFiles(fullPath);

            StringBuilder jsonBuilder = new StringBuilder();
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

            HttpResponse response = new HttpResponse
            {
                StatusCode = 200,
                StatusMessage = "OK"
            };
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            response.Body = jsonBytes;
            response.SetHeader("Content-Type", "application/json");
            response.SetHeader("Content-Length", jsonBytes.Length.ToString());

            HttpResponseBuilder.SendResponse(stream, response);
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

            if (requestPath != "/")
            {
                string parentPath = requestPath.TrimEnd('/');
                int lastSlash = parentPath.LastIndexOf('/');
                if (lastSlash > 0) parentPath = parentPath.Substring(0, lastSlash);
                else parentPath = "/";
                html.Append($"<tr><td><span class='dir-icon'>📁</span><a href='{parentPath}'>..</a></td><td>-</td><td>-</td></tr>");
            }

            foreach (var dir in Directory.GetDirectories(fullPath))
            {
                var dirInfo = new DirectoryInfo(dir);
                string dirName = dirInfo.Name;
                string dirPath = requestPath.TrimEnd('/') + "/" + dirName;
                html.Append($"<tr><td><span class='dir-icon'>📁</span><a href='{dirPath}'>{dirName}</a></td><td>-</td><td>{dirInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}</td></tr>");
            }

            foreach (var file in Directory.GetFiles(fullPath))
            {
                var fileInfo = new FileInfo(file);
                string fileName = fileInfo.Name;
                string filePath = requestPath.TrimEnd('/') + "/" + fileName;
                html.Append($"<tr><td><span class='file-icon'>📄</span><a href='{filePath}'>{fileName}</a></td><td>{FormatFileSize(fileInfo.Length)}</td><td>{fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}</td></tr>");
            }

            html.Append("</tbody></table></div></body></html>");

            string htmlContent = html.ToString();
            HttpResponse response = new HttpResponse
            {
                StatusCode = 200,
                StatusMessage = "OK"
            };
            byte[] htmlBytes = Encoding.UTF8.GetBytes(htmlContent);
            response.Body = htmlBytes;
            response.SetHeader("Content-Type", "text/html");
            response.SetHeader("Content-Length", htmlBytes.Length.ToString());

            HttpResponseBuilder.SendResponse(stream, response);
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

        private void IncrementRequestCount()
        {
            lock (statsLock)
            {
                totalRequests++;
            }
        }

        private Session? GetSessionFromRequest(HttpRequest request)
        {
            if (request.Cookies.TryGetValue("sessionId", out string? sessionId))
            {
                return sessionManager.GetSession(sessionId);
            }
            return null;
        }

        private bool IsPublicPath(string path)
        {
            string[] publicPaths = { "/login", "/login.html" };
            foreach (string publicPath in publicPaths)
            {
                if (path.Equals(publicPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            string extension = Path.GetExtension(path).ToLowerInvariant();
            string[] publicExtensions = { ".css", ".js", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".svg", ".woff", ".woff2", ".ttf", ".eot" };
            foreach (string ext in publicExtensions)
            {
                if (extension == ext)
                {
                    return true;
                }
            }

            return false;
        }

        private void HandleLogin(HttpRequest request, NetworkStream stream)
        {
            if (request.Method == "GET")
            {
                string loginPage = GenerateLoginPage();
                HttpResponse response = new HttpResponse
                {
                    StatusCode = 200,
                    StatusMessage = "OK"
                };
                byte[] htmlBytes = Encoding.UTF8.GetBytes(loginPage);
                response.Body = htmlBytes;
                response.SetHeader("Content-Type", "text/html");
                response.SetHeader("Content-Length", htmlBytes.Length.ToString());
                HttpResponseBuilder.SendResponse(stream, response);
            }
            else if (request.Method == "POST")
            {
                string username = request.FormData.ContainsKey("username") ? request.FormData["username"] : string.Empty;
                string password = request.FormData.ContainsKey("password") ? request.FormData["password"] : string.Empty;

                if (authService.ValidateCredentials(username, password))
                {
                    Session session = sessionManager.CreateSession(username);
                    HttpResponse response = new HttpResponse
                    {
                        StatusCode = 302,
                        StatusMessage = "Found"
                    };
                    response.SetHeader("Location", "/");
                    response.SetCookie("sessionId", session.SessionId, 1800);
                    HttpResponseBuilder.SendResponse(stream, response);
                }
                else
                {
                    string loginPage = GenerateLoginPage("Invalid username or password");
                    HttpResponse response = new HttpResponse
                    {
                        StatusCode = 200,
                        StatusMessage = "OK"
                    };
                    byte[] htmlBytes = Encoding.UTF8.GetBytes(loginPage);
                    response.Body = htmlBytes;
                    response.SetHeader("Content-Type", "text/html");
                    response.SetHeader("Content-Length", htmlBytes.Length.ToString());
                    HttpResponseBuilder.SendResponse(stream, response);
                }
            }
        }

        private void HandleLogout(HttpRequest request, NetworkStream stream)
        {
            if (request.Cookies.TryGetValue("sessionId", out string? sessionId))
            {
                sessionManager.RemoveSession(sessionId);
            }

            HttpResponse response = new HttpResponse
            {
                StatusCode = 302,
                StatusMessage = "Found"
            };
            response.SetHeader("Location", "/login");
            response.SetCookie("sessionId", "", 0);
            HttpResponseBuilder.SendResponse(stream, response);
        }

        private void RedirectToLogin(NetworkStream stream)
        {
            HttpResponse response = new HttpResponse
            {
                StatusCode = 302,
                StatusMessage = "Found"
            };
            response.SetHeader("Location", "/login");
            HttpResponseBuilder.SendResponse(stream, response);
        }

        private string GenerateLoginPage(string errorMessage = "")
        {
            StringBuilder html = new StringBuilder();
            html.Append("<!DOCTYPE html>");
            html.Append("<html><head>");
            html.Append("<meta charset='UTF-8'>");
            html.Append("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            html.Append("<title>Login - Web Server</title>");
            html.Append("<style>");
            html.Append("body{font-family:system-ui,-apple-system,sans-serif;display:flex;justify-content:center;align-items:center;min-height:100vh;margin:0;background:linear-gradient(135deg,#667eea 0%,#764ba2 100%);}");
            html.Append(".login-container{background:white;padding:40px;border-radius:12px;box-shadow:0 10px 40px rgba(0,0,0,0.2);max-width:400px;width:100%;}");
            html.Append("h1{color:#333;margin-bottom:30px;text-align:center;font-size:28px;}");
            html.Append(".form-group{margin-bottom:20px;}");
            html.Append("label{display:block;margin-bottom:8px;color:#555;font-weight:500;}");
            html.Append("input[type='text'],input[type='password']{width:100%;padding:12px;border:2px solid #ddd;border-radius:6px;font-size:16px;box-sizing:border-box;transition:border-color 0.3s;}");
            html.Append("input[type='text']:focus,input[type='password']:focus{outline:none;border-color:#667eea;}");
            html.Append("button{width:100%;padding:12px;background:#667eea;color:white;border:none;border-radius:6px;font-size:16px;font-weight:600;cursor:pointer;transition:background 0.3s;}");
            html.Append("button:hover{background:#5568d3;}");
            html.Append(".error{color:#e74c3c;background:#fdf2f2;padding:12px;border-radius:6px;margin-bottom:20px;border-left:4px solid #e74c3c;}");
            html.Append(".info{color:#666;font-size:14px;margin-top:20px;text-align:center;}");
            html.Append("</style></head><body>");
            html.Append("<div class='login-container'>");
            html.Append("<h1>🔐 Login</h1>");
            
            if (!string.IsNullOrEmpty(errorMessage))
            {
                html.Append($"<div class='error'>{errorMessage}</div>");
            }

            html.Append("<form method='POST' action='/login'>");
            html.Append("<div class='form-group'>");
            html.Append("<label for='username'>Username:</label>");
            html.Append("<input type='text' id='username' name='username' required autofocus>");
            html.Append("</div>");
            html.Append("<div class='form-group'>");
            html.Append("<label for='password'>Password:</label>");
            html.Append("<input type='password' id='password' name='password' required>");
            html.Append("</div>");
            html.Append("<button type='submit'>Login</button>");
            html.Append("</form>");
            html.Append("<div class='info'>Default users: admin/admin123, user/password123, test/test123</div>");
            html.Append("</div></body></html>");

            return html.ToString();
        }
    }
}

