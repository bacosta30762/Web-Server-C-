using System;
using System.IO;
using WebServer.Core;

namespace WebServer
{
    class Program
    {
        static void Main(string[] args)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "wwwroot");

            if (!Directory.Exists(root))
            {
                string? projectRoot = Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.FullName;
                if (projectRoot != null)
                {
                    string alternateRoot = Path.Combine(projectRoot, "wwwroot");
                    if (Directory.Exists(alternateRoot))
                    {
                        root = alternateRoot;
                    }
                }
            }

            if (!Directory.Exists(root))
            {
                Console.WriteLine($"ERROR: wwwroot directory not found!");
                Console.WriteLine($"Searched in: {Path.Combine(AppContext.BaseDirectory, "wwwroot")}");
                return;
            }

            Console.WriteLine($"Serving files from: {root}");
            Server server = new Server(root, 8080);
            server.Start();
        }
    }
}
