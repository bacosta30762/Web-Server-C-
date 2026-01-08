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
            if (string.IsNullOrWhiteSpace(requestText))
            {
                return null;
            }

            if (requestText.Length > 8192)
            {
                return null;
            }

            string[] lines = requestText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0)
            {
                return null;
            }

            if (lines.Length > 100)
            {
                return null;
            }

            string[] requestLineParts = lines[0].Split(' ');
            if (requestLineParts.Length != 3)
            {
                return null;
            }

            string method = requestLineParts[0].Trim();
            string path = requestLineParts[1].Trim();
            string version = requestLineParts[2].Trim();

            if (string.IsNullOrEmpty(method) || string.IsNullOrEmpty(path) || string.IsNullOrEmpty(version))
            {
                return null;
            }

            if (path.Length > 2048)
            {
                return null;
            }

            HttpRequest request = new HttpRequest
            {
                Method = method,
                Path = path,
                Version = version
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
            if (string.IsNullOrEmpty(cookieHeader) || request == null)
            {
                return;
            }

            if (cookieHeader.Length > 4096)
            {
                return;
            }

            string[] cookies = cookieHeader.Split(';');
            foreach (string cookie in cookies)
            {
                if (string.IsNullOrWhiteSpace(cookie))
                {
                    continue;
                }

                string trimmed = cookie.Trim();
                int equalsIndex = trimmed.IndexOf('=');
                if (equalsIndex > 0 && equalsIndex < trimmed.Length - 1)
                {
                    string name = trimmed.Substring(0, equalsIndex).Trim();
                    string value = trimmed.Substring(equalsIndex + 1).Trim();
                    
                    if (!string.IsNullOrEmpty(name) && name.Length <= 100 && value.Length <= 4096)
                    {
                        request.Cookies[name] = value;
                    }
                }
            }
        }

        public static void ParseFormData(HttpRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Body))
            {
                return;
            }

            if (request.Body.Length > 10 * 1024 * 1024)
            {
                return;
            }

            string[] pairs = request.Body.Split('&');
            if (pairs.Length > 1000)
            {
                return;
            }

            foreach (string pair in pairs)
            {
                if (string.IsNullOrWhiteSpace(pair))
                {
                    continue;
                }

                int equalsIndex = pair.IndexOf('=');
                if (equalsIndex > 0 && equalsIndex < pair.Length - 1)
                {
                    try
                    {
                        string key = Uri.UnescapeDataString(pair.Substring(0, equalsIndex));
                        string value = Uri.UnescapeDataString(pair.Substring(equalsIndex + 1));
                        
                        if (!string.IsNullOrEmpty(key) && key.Length <= 256 && value.Length <= 8192)
                        {
                            request.FormData[key] = value;
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}

