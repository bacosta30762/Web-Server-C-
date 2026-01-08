using System;
using System.Collections.Generic;

namespace WebServer.Models
{
    public class HttpResponse
    {
        public int StatusCode { get; set; } = 200;
        public string StatusMessage { get; set; } = "OK";
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> Cookies { get; set; } = new List<string>();
        public byte[] Body { get; set; } = Array.Empty<byte>();

        public void SetCookie(string name, string value, int maxAge = 3600, string path = "/", bool httpOnly = true)
        {
            string cookie = $"{name}={value}; Path={path}; Max-Age={maxAge}";
            if (httpOnly)
            {
                cookie += "; HttpOnly";
            }
            Cookies.Add(cookie);
        }

        public void SetHeader(string name, string value)
        {
            Headers[name] = value;
        }
    }
}

