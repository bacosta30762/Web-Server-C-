using System;

namespace WebServer.Models
{
    public class Session
    {
        public string SessionId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime LastAccessed { get; set; }
        public DateTime ExpiresAt { get; set; }

        public bool IsExpired => DateTime.Now > ExpiresAt;
    }
}

