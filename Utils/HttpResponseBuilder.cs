using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using WebServer.Models;

namespace WebServer.Utils
{
    public static class HttpResponseBuilder
    {
        public static void SendResponse(NetworkStream stream, HttpResponse response)
        {
            StringBuilder responseBuilder = new StringBuilder();
            responseBuilder.Append($"HTTP/1.1 {response.StatusCode} {response.StatusMessage}\r\n");

            foreach (var header in response.Headers)
            {
                if (!header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    responseBuilder.Append($"{header.Key}: {header.Value}\r\n");
                }
            }

            foreach (string cookie in response.Cookies)
            {
                responseBuilder.Append($"Set-Cookie: {cookie}\r\n");
            }

            responseBuilder.Append("\r\n");
            string responseHeaders = responseBuilder.ToString();
            byte[] headerBytes = Encoding.UTF8.GetBytes(responseHeaders);

            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(response.Body, 0, response.Body.Length);
        }

        public static void SendErrorResponse(NetworkStream stream, int statusCode, string statusMessage)
        {
            HttpResponse response = new HttpResponse
            {
                StatusCode = statusCode,
                StatusMessage = statusMessage
            };

            string body = GenerateErrorPage(statusCode, statusMessage);
            response.Body = Encoding.UTF8.GetBytes(body);
            response.SetHeader("Content-Type", "text/html");
            response.SetHeader("Content-Length", response.Body.Length.ToString());

            SendResponse(stream, response);
        }

        private static string GenerateErrorPage(int statusCode, string statusMessage)
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

            return $@"<!DOCTYPE html>
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
        }
    }
}

