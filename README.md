## Simple C# Web Server

This project is a minimal web server written in C#, inspired by the article *“Writing a Web Server from Scratch”* on CodeProject. It uses `HttpListener` to accept HTTP requests and serves static files from a local folder.

The goal is to show, step by step, how an HTTP server works under the hood without relying on a full web framework.

---

### 1. Project structure

- **Executable project**
  - `WebServer.csproj`: .NET console application targeting `net8.0`.
  - `Program.cs`: Contains the entire server implementation.
- **Static content**
  - `wwwroot/`: Root folder for all files that can be served.
    - `index.html`: Default page returned for `/`.

You can add more files under `wwwroot` (for example CSS, JavaScript, images) and they will be served automatically.

---

### 2. Starting the server

The entry point is `Main` in `Program.cs`.

1. **Configure the web root**
   - The server uses a folder named `wwwroot` under the application base directory:
     - `root = AppContext.BaseDirectory + "wwwroot"`.

2. **Create and start the listener**
   - An `HttpListener` instance is created.
   - It is configured to listen on `http://localhost:8080/`.
   - `listener.Start()` begins accepting HTTP connections.

3. **Attach shutdown handler**
   - `Console.CancelKeyPress` is handled so that pressing `Ctrl+C`:
     - Sets a `running` flag to `false`.
     - Calls `listener.Stop()` to break out of the accept loop.

4. **Main loop**
   - While `running` is `true`, the server:
     - Calls `listener.GetContext()` to wait for the next incoming request.
     - For each request, it delegates processing to the same loop body:
       - Logs the request.
       - Tries to serve a static file.
       - If no file is found, returns a simple fallback HTML page.

---

### 3. Accepting and logging requests

Each iteration of the loop performs these steps:

1. **Accept a connection**
   - `GetContext()` blocks until a client sends a request.
   - The result is an `HttpListenerContext` containing:
     - `Request`: all information about the HTTP request.
     - `Response`: the HTTP response to be sent back.

2. **Handle listener shutdown**
   - If `listener.Stop()` is called (for example via `Ctrl+C`), `GetContext()` throws `HttpListenerException`.
   - When that happens and `running` is `false`, the loop breaks and the server shuts down cleanly.

3. **Log the request**
   - For each request, the server writes a line like:
     - `2026-01-08T12:34:56: Received request for http://localhost:8080/path`
   - This shows both the time and the requested URL.

---

### 4. Static file routing

Static file handling is implemented in `TryServeFile`.

1. **Handle missing URL**
   - If `request.Url` is `null`, the method returns `false` and the caller sends the fallback HTML.

2. **Resolve the requested path**
   - The server reads `request.Url.LocalPath`.
   - If the path is empty or `/`, the server treats it as `/index.html`.
   - For example:
     - `/` → `/index.html`
     - `/styles/site.css` stays `/styles/site.css`.

3. **Map to the file system**
   - The server:
     - Removes the leading `/`.
     - Replaces `/` with the platform directory separator.
     - Combines the result with the `wwwroot` folder.
     - Normalizes the path with `Path.GetFullPath`.

4. **Security check**
   - After building the full path, the server checks that it still starts with the `wwwroot` path.
   - If it does not, the request is rejected with `404 Not Found`.
   - This prevents directory traversal attempts such as `/../secret.txt`.

5. **Existence check**
   - If the target file does not exist under `wwwroot`, the server sets `404 Not Found` and returns.

6. **Serving the file**
   - If the file exists:
     - The file is read fully into a byte array.
     - The `Content-Type` header is determined from the file extension.
     - `ContentLength64` is set to the file size.
     - The bytes are written to the response output stream.
   - Returning `true` signals to the caller that the response was produced by the static file handler.

---

### 5. Default document (`/` → `index.html`)

The server maps the root URL (`/`) to `index.html` automatically.

- When `LocalPath` is empty or exactly `/`, it is replaced with `/index.html`.
- This means:
  - `http://localhost:8080/` serves `wwwroot/index.html`.
  - `http://localhost:8080/index.html` also serves the same file.

This behavior mirrors common web server configurations.

---

### 6. MIME type handling

To let browsers interpret files correctly, the server sets `Content-Type` based on file extension.

1. **MIME type table**
   - A dictionary maps extensions to MIME types:
     - `.html`, `.htm` → `text/html`
     - `.css` → `text/css`
     - `.js` → `application/javascript`
     - `.png` → `image/png`
     - `.jpg`, `.jpeg` → `image/jpeg`
     - `.gif` → `image/gif`
     - `.ico` → `image/x-icon`
     - `.txt` → `text/plain`

2. **Lookup logic**
   - When serving a file:
     - The server extracts `Path.GetExtension(path)`.
     - It looks up that extension in the dictionary.
     - If found, it uses the mapped MIME type.
     - If not found or the extension is empty, it falls back to `application/octet-stream`.

This ensures that browsers correctly handle HTML, CSS, images, and other common static assets.

---

### 7. Fallback HTML response

If `TryServeFile` does not handle the request (for example, the path is outside `wwwroot` or no file exists), the server falls back to a simple inline HTML page:

- A minimal `<html><body><h1>Welcome to the Simple Web Server</h1></body></html>` string.
- Encoded as UTF‑8.
- Written directly to the response output stream with the proper content length.

This guarantees that every valid request to the listener receives some response, even if no physical file is found.

---

### 8. Error handling

Request processing is wrapped in a `try/catch` block.

1. **Normal path**
   - The server tries to:
     - Serve a static file.
     - Or write the fallback HTML.

2. **Exception path**
   - If an exception occurs while processing the request:
     - A message describing the error is written to the console.
     - If the output stream is still writable:
       - The server sets status `500 Internal Server Error`.
       - Sends a simple HTML page: `500 - Internal Server Error`.

This keeps the server from crashing on unexpected errors and provides a clear HTTP status back to the client.

---

### 9. Graceful shutdown

The server is designed to shut down cleanly when you press `Ctrl+C` in the console.

1. **Cancel key handler**
   - When `Console.CancelKeyPress` fires:
     - The event is marked as handled (`Cancel = true`).
     - The `running` flag is set to `false`.
     - `listener.Stop()` is called to unblock `GetContext`.

2. **Breaking the loop**
   - If `GetContext` throws `HttpListenerException` while `running` is `false`, the loop breaks.
   - After the loop, `listener.Close()` is called to release resources.

This ensures the process exits predictably without leaving the listener open.

---

### 10. How to run and test

1. **Restore and build**
   - From the project directory:
     - `dotnet restore`
     - `dotnet build`

2. **Run the server**
   - `dotnet run`
   - The console should display:
     - `Listening for connections on http://localhost:8080/`

3. **Test in a browser**
   - Open `http://localhost:8080/`:
     - You should see the `index.html` content from `wwwroot`.
   - Add more files:
     - Example: `wwwroot/styles/site.css` and link it from `index.html`.
     - Browse to `http://localhost:8080/styles/site.css` to verify.

4. **Stop the server**
   - Press `Ctrl+C` in the terminal.
   - The server stops listening and exits cleanly.

---

### 11. Extending the server

Some ideas to extend this simple server:

- Add support for more MIME types.
- Add routing rules to generate dynamic content for certain paths.
- Implement basic logging to a file instead of only to the console.
- Add configuration for:
  - Port number.
  - Root folder.
  - Default document name.

These changes can be built incrementally on top of the current structure.


