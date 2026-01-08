using System;
using System.Collections.Generic;
using System.Text;
using WebServer.Models;

namespace WebServer.Utils
{
    public static class HttpParser
    {
        public static HttpRequest? ParseRequest(string requestText)
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

                    if (headerName.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseCookies(request, headerValue);
                    }
                }
            }

            return request;
        }

        public static void ParseCookies(HttpRequest request, string cookieHeader)
        {
            string[] cookies = cookieHeader.Split(';');
            foreach (string cookie in cookies)
            {
                string trimmed = cookie.Trim();
                int equalsIndex = trimmed.IndexOf('=');
                if (equalsIndex > 0)
                {
                    string name = trimmed.Substring(0, equalsIndex).Trim();
                    string value = trimmed.Substring(equalsIndex + 1).Trim();
                    request.Cookies[name] = value;
                }
            }
        }

        public static void ParseFormData(HttpRequest request)
        {
            if (string.IsNullOrEmpty(request.Body))
            {
                return;
            }

            string[] pairs = request.Body.Split('&');
            foreach (string pair in pairs)
            {
                int equalsIndex = pair.IndexOf('=');
                if (equalsIndex > 0)
                {
                    string key = Uri.UnescapeDataString(pair.Substring(0, equalsIndex));
                    string value = Uri.UnescapeDataString(pair.Substring(equalsIndex + 1));
                    request.FormData[key] = value;
                }
            }
        }
    }
}

