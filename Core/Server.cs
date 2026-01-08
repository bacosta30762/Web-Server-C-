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

        public Server(string rootDirectory, int port = 8080)
        {
            this.rootDirectory = rootDirectory;
            this.port = port;
            this.running = false;
            this.sessionManager = new SessionManager();
            this.requestHandler = new RequestHandler(rootDirectory, sessionManager);
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

                HttpRequest? httpRequest = HttpParser.ParseRequest(requestText);
                if (httpRequest == null)
                {
                    HttpResponseBuilder.SendErrorResponse(stream, 400, "Bad Request");
                    stream.Close();
                    client.Close();
                    return;
                }

                ReadRequestBody(httpRequest, stream, buffer, bytesRead);

                if (!string.IsNullOrEmpty(httpRequest.Body) &&
                    httpRequest.Headers.TryGetValue("Content-Type", out string? contentType) &&
                    contentType.Contains("application/x-www-form-urlencoded"))
                {
                    HttpParser.ParseFormData(httpRequest);
                }

                Console.WriteLine($"{DateTime.Now}: {httpRequest.Method} {httpRequest.Path}");

                requestHandler.HandleRequest(httpRequest, stream);
                stream.Close();
                client.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling client: {ex.Message}");
                client.Close();
            }
        }

        private void ReadRequestBody(HttpRequest httpRequest, NetworkStream stream, byte[] buffer, int bytesRead)
        {
            if (httpRequest.Headers.TryGetValue("Content-Length", out string? contentLengthStr) &&
                int.TryParse(contentLengthStr, out int contentLength) && contentLength > 0)
            {
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
                        while (remaining > 0)
                        {
                            int read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                            if (read == 0) break;
                            Array.Copy(buffer, 0, bodyBuffer, bodyBytesRead, read);
                            bodyBytesRead += read;
                            remaining -= read;
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

