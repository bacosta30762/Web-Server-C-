using System.Collections.Generic;

namespace WebServer.Models
{
    public class HttpRequest
    {
        public string Method { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        public string Body { get; set; } = string.Empty;
        public Dictionary<string, string> FormData { get; set; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Cookies { get; set; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    }
}

