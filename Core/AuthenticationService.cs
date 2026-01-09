using System;
using System.Collections.Generic;
using WebServer.Models;

namespace WebServer.Core
{
    public class AuthenticationService
    {
        private readonly Dictionary<string, string> users;

        public AuthenticationService()
        {
            users = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "admin", "admin123" },
                { "user", "password123" },
                { "test", "test123" }
            };
        }

        public bool ValidateCredentials(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            if (users.TryGetValue(username, out string? storedPassword))
            {
                return storedPassword == password;
            }

            return false;
        }

        public bool UserExists(string username)
        {
            return !string.IsNullOrWhiteSpace(username) && users.ContainsKey(username);
        }
    }
}

