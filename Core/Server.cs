using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using WebServer.Core;
using WebServer.Models;
using WebServer.Utils;

namespace WebServer.Core
{
    public class Server
    {
        private readonly string rootDirectory;
        private readonly int port;
        private TcpListener? listener;
        private bool running;
        private readonly RequestHandler requestHandler;
        private readonly SessionManager sessionManager;
        private readonly AuthenticationService authService;

        public Server(string rootDirectory, int port = 8080)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("Root directory cannot be null or empty", nameof(rootDirectory));
            }

            if (!Directory.Exists(rootDirectory))
            {
                throw new DirectoryNotFoundException($"Root directory does not exist: {rootDirectory}");
            }

            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535");
            }

            this.rootDirectory = Path.GetFullPath(rootDirectory);
            this.port = port;
            this.running = false;
            this.sessionManager = new SessionManager();
            this.authService = new AuthenticationService();
            this.requestHandler = new RequestHandler(this.rootDirectory, sessionManager, authService);
        }

        public void Start()
        {
            if (running)
            {
                throw new InvalidOperationException("Server is already running");
            }

            try
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
                        if (client != null && client.Connected)
                        {
                            Console.WriteLine($"{DateTime.Now}: Client connected from {client.Client.RemoteEndPoint}");
                            ThreadPool.QueueUserWorkItem(state => HandleClient(client));
                        }
                    }
                    catch (SocketException ex) when (!running)
                    {
                        break;
                    }
                    catch (SocketException ex)
                    {
                        Console.WriteLine($"Socket error: {ex.Message}");
                        throw;
                    }
                }
            }
            finally
            {
                listener?.Stop();
            }
        }

        public void Stop()
        {
            running = false;
            listener?.Stop();
        }

        private void HandleClient(TcpClient client)
        {
            if (client == null || !client.Connected)
            {
                return;
            }

            NetworkStream? stream = null;
            try
            {
                stream = client.GetStream();
                if (stream == null || !stream.CanRead)
                {
                    return;
                }

                byte[] buffer = new byte[4096];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);

                if (bytesRead == 0)
                {
                    return;
                }

                if (bytesRead > 8192)
                {
                    HttpResponseBuilder.SendErrorResponse(stream, 413, "Request Entity Too Large");
                    return;
                }

                string requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Console.WriteLine($"Received request:\n{requestText}");

                HttpRequest? httpRequest = HttpParser.ParseRequest(requestText);
                if (httpRequest == null)
                {
                    HttpResponseBuilder.SendErrorResponse(stream, 400, "Bad Request");
                    return;
                }

                ReadRequestBody(httpRequest, stream, buffer, bytesRead);

                if (!string.IsNullOrEmpty(httpRequest.Body) &&
                    httpRequest.Headers.TryGetValue("Content-Type", out string? contentType) &&
                    !string.IsNullOrEmpty(contentType) &&
                    contentType.Contains("application/x-www-form-urlencoded"))
                {
                    HttpParser.ParseFormData(httpRequest);
                }

                Console.WriteLine($"{DateTime.Now}: {httpRequest.Method} {httpRequest.Path}");

                requestHandler.HandleRequest(httpRequest, stream);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"IO error handling client: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling client: {ex.Message}");
                try
                {
                    if (stream != null && stream.CanWrite)
                    {
                        HttpResponseBuilder.SendErrorResponse(stream, 500, "Internal Server Error");
                    }
                }
                catch
                {
                }
            }
            finally
            {
                try
                {
                    stream?.Close();
                    client?.Close();
                }
                catch
                {
                }
            }
        }

        private void ReadRequestBody(HttpRequest httpRequest, NetworkStream stream, byte[] buffer, int bytesRead)
        {
            if (httpRequest.Headers.TryGetValue("Content-Length", out string? contentLengthStr) &&
                int.TryParse(contentLengthStr, out int contentLength) && contentLength > 0)
            {
                const int maxBodySize = 10 * 1024 * 1024;
                if (contentLength > maxBodySize)
                {
                    throw new InvalidOperationException($"Request body too large: {contentLength} bytes");
                }

                int bodyStart = Encoding.UTF8.GetString(buffer, 0, bytesRead).IndexOf("\r\n\r\n");
                if (bodyStart >= 0)
                {
                    int headerLength = bodyStart + 4;
                    int bodyBytesRead = bytesRead - headerLength;

                    if (bodyBytesRead < contentLength)
                    {
                        byte[] bodyBuffer = new byte[contentLength];
                        Array.Copy(buffer, headerLength, bodyBuffer, 0, bodyBytesRead);

                        int remaining = contentLength - bodyBytesRead;
                        int timeout = 0;
                        const int maxTimeout = 30;
                        while (remaining > 0 && timeout < maxTimeout)
                        {
                            int read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                            if (read == 0)
                            {
                                timeout++;
                                Thread.Sleep(100);
                                continue;
                            }
                            Array.Copy(buffer, 0, bodyBuffer, bodyBytesRead, read);
                            bodyBytesRead += read;
                            remaining -= read;
                            timeout = 0;
                        }

                        if (remaining > 0)
                        {
                            throw new TimeoutException("Timeout reading request body");
                        }

                        httpRequest.Body = Encoding.UTF8.GetString(bodyBuffer, 0, contentLength);
                    }
                    else
                    {
                        httpRequest.Body = Encoding.UTF8.GetString(buffer, headerLength, bodyBytesRead);
                    }
                }
            }
        }
    }
}

