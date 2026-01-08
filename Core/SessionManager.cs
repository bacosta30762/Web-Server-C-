using System;
using System.Collections.Generic;
using System.Linq;
using WebServer.Models;

namespace WebServer.Core
{
    public class SessionManager
    {
        private readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>();
        private readonly object lockObject = new object();
        private readonly int sessionTimeoutMinutes = 30;

        public Session? GetSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return null;
            }

            lock (lockObject)
            {
                if (sessions.TryGetValue(sessionId, out Session? session))
                {
                    if (session.IsExpired)
                    {
                        sessions.Remove(sessionId);
                        return null;
                    }

                    session.LastAccessed = DateTime.Now;
                    session.ExpiresAt = DateTime.Now.AddMinutes(sessionTimeoutMinutes);
                    return session;
                }
            }

            return null;
        }

        public Session CreateSession(string username)
        {
            string sessionId = GenerateSessionId();
            Session session = new Session
            {
                SessionId = sessionId,
                Username = username,
                CreatedAt = DateTime.Now,
                LastAccessed = DateTime.Now,
                ExpiresAt = DateTime.Now.AddMinutes(sessionTimeoutMinutes)
            };

            lock (lockObject)
            {
                sessions[sessionId] = session;
            }

            return session;
        }

        public void RemoveSession(string sessionId)
        {
            lock (lockObject)
            {
                sessions.Remove(sessionId);
            }
        }

        public void CleanupExpiredSessions()
        {
            lock (lockObject)
            {
                var expiredSessions = sessions.Values.Where(s => s.IsExpired).ToList();
                foreach (var session in expiredSessions)
                {
                    sessions.Remove(session.SessionId);
                }
            }
        }

        private string GenerateSessionId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}

