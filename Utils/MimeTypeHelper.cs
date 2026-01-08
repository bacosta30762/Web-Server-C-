using System.Collections.Generic;

namespace WebServer.Utils
{
    public static class MimeTypeHelper
    {
        private static readonly Dictionary<string, string> ContentTypes = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
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

        public static string GetContentType(string path)
        {
            string? extension = System.IO.Path.GetExtension(path);
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
}

