## Simple C# Web Server

This project is a minimal web server written in C#, following the approach described in the article *"Writing a Web Server from Scratch"* on CodeProject. It implements HTTP from scratch using `TcpListener` for raw socket handling and manual parsing of HTTP requests and responses.

The goal is to show, step by step, how an HTTP server works under the hood without relying on high-level abstractions like `HttpListener` or web frameworks.

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

The entry point is `Main` in `Program.cs`, which creates a `Server` instance and starts it.

1. **Configure the web root**
   - The server uses a folder named `wwwroot` under the application base directory:
     - `root = AppContext.BaseDirectory + "wwwroot"`.

2. **Create and start the server**
   - A `Server` instance is created with the root directory and port (default: 8080).
   - `server.Start()` begins the main server loop.

3. **Server initialization**
   - Inside `Start()`, a `TcpListener` is created to listen on `IPAddress.Any` and the specified port.
   - `listener.Start()` begins accepting TCP connections.

4. **Attach shutdown handler**
   - `Console.CancelKeyPress` is handled so that pressing `Ctrl+C`:
     - Sets a `running` flag to `false`.
     - Calls `listener.Stop()` to break out of the accept loop.

5. **Main loop**
   - While `running` is `true`, the server:
     - Calls `listener.AcceptTcpClient()` to wait for the next TCP connection.
     - Each client connection is handled in a separate thread via `ThreadPool.QueueUserWorkItem`.
     - The client handler reads the raw HTTP request, parses it, and sends a response.

---

### 3. Accepting connections and handling clients

Each client connection is handled in a separate thread:

1. **Accept a TCP connection**
   - `AcceptTcpClient()` blocks until a client connects.
   - The result is a `TcpClient` representing the raw TCP connection.

2. **Handle listener shutdown**
   - If `listener.Stop()` is called (for example via `Ctrl+C`), `AcceptTcpClient()` throws `SocketException`.
   - When that happens and `running` is `false`, the loop breaks and the server shuts down cleanly.

3. **Thread pool processing**
   - Each client is processed asynchronously using `ThreadPool.QueueUserWorkItem`.
   - This allows the server to handle multiple concurrent connections.

4. **Read raw HTTP request**
   - The server reads bytes from the `NetworkStream` into a buffer.
   - The raw bytes are converted to a UTF-8 string for parsing.

5. **Log the request**
   - For each request, the server writes the raw HTTP request to the console.
   - After parsing, it logs: `2026-01-08T12:34:56: GET /path`

---

### 4. Manual HTTP request parsing

HTTP request parsing is implemented in `ParseRequest()`:

1. **Split request into lines**
   - The raw HTTP request string is split by `\r\n` to separate lines.

2. **Parse request line**
   - The first line contains: `METHOD PATH HTTP/VERSION`
   - Example: `GET /index.html HTTP/1.1`
   - Split by spaces to extract method, path, and HTTP version.
   - These are stored in an `HttpRequest` object.

3. **Parse headers**
   - Subsequent lines until an empty line are HTTP headers.
   - Each header is in the format: `HeaderName: HeaderValue`
   - Headers are parsed and stored in a dictionary (case-insensitive).

4. **Request object**
   - The parsed data is stored in an `HttpRequest` class with:
     - `Method`: HTTP method (GET, POST, etc.)
     - `Path`: Requested path
     - `Version`: HTTP version
     - `Headers`: Dictionary of header name-value pairs

### 5. Static file routing

Static file handling is implemented in `ProcessRequest()`:

1. **Method validation**
   - Currently only `GET` requests are supported.
   - Other methods return `405 Method Not Allowed`.

2. **Resolve the requested path**
   - The server reads `request.Path`.
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
   - If it does not, the request is rejected with `403 Forbidden`.
   - This prevents directory traversal attempts such as `/../secret.txt`.

5. **Existence check**
   - If the target file does not exist under `wwwroot`, the server returns `404 Not Found`.

6. **Serving the file**
   - If the file exists:
     - The file is read fully into a byte array.
     - The `Content-Type` header is determined from the file extension.
     - The file bytes are sent using `SendResponse()`.

---

### 6. Manual HTTP response building

HTTP responses are built manually in `SendResponse()`:

1. **Status line**
   - Format: `HTTP/1.1 STATUS_CODE STATUS_MESSAGE\r\n`
   - Example: `HTTP/1.1 200 OK\r\n`

2. **Headers**
   - Each header is formatted as: `HeaderName: HeaderValue\r\n`
   - Common headers include:
     - `Content-Type`: MIME type of the response body
     - `Content-Length`: Size of the response body in bytes

3. **Empty line**
   - A blank line (`\r\n`) separates headers from the body.

4. **Body**
   - The response body follows the empty line.
   - For text content, it's sent as UTF-8 encoded bytes.
   - For binary content (images, etc.), raw bytes are sent directly.

5. **Writing to stream**
   - The complete response is written to the `NetworkStream`.
   - Header bytes and body bytes are written separately to handle binary content correctly.

### 7. Default document (`/` → `index.html`)

The server maps the root URL (`/`) to `index.html` automatically.

- When `LocalPath` is empty or exactly `/`, it is replaced with `/index.html`.
- This means:
  - `http://localhost:8080/` serves `wwwroot/index.html`.
  - `http://localhost:8080/index.html` also serves the same file.

This behavior mirrors common web server configurations.

---

### 8. MIME type handling

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

### 9. Error responses

The server handles various error conditions:

1. **400 Bad Request**
   - Returned when the HTTP request cannot be parsed.

2. **403 Forbidden**
   - Returned when a path traversal attempt is detected.

3. **404 Not Found**
   - Returned when the requested file does not exist.

4. **405 Method Not Allowed**
   - Returned for non-GET requests (currently only GET is supported).

5. **500 Internal Server Error**
   - Returned when an exception occurs while reading or serving a file.

All error responses include an HTML body with the error code and message.

---

### 10. Error handling

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

### 11. Graceful shutdown

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

### 12. How to run and test

1. **Restore and build**
   - From the project directory:
     - `dotnet restore`
     - `dotnet build`

2. **Run the server**
   - `dotnet run`
   - The console should display:
     - `Listening for connections on port 8080...`

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

### 13. Architecture overview

The server follows a "from scratch" approach:

1. **Raw TCP sockets**: Uses `TcpListener` instead of `HttpListener` to handle connections at the TCP level.

2. **Manual HTTP parsing**: Parses HTTP requests manually by:
   - Splitting the request string into lines
   - Extracting method, path, and version from the request line
   - Parsing headers into key-value pairs

3. **Manual HTTP response building**: Constructs HTTP responses by:
   - Building the status line
   - Adding headers
   - Writing the body (handling both text and binary content)

4. **Threading**: Uses `ThreadPool` to handle multiple concurrent connections.

5. **Class structure**: Organized into:
   - `HttpRequest`: Represents a parsed HTTP request
   - `Server`: Encapsulates server logic and state
   - `Program`: Simple entry point

This approach provides a deep understanding of how HTTP works at the protocol level, without relying on high-level abstractions.

### 14. Extending the server

Some ideas to extend this simple server:

- Add support for more MIME types.
- Add routing rules to generate dynamic content for certain paths.
- Implement basic logging to a file instead of only to the console.
- Add configuration for:
  - Port number.
  - Root folder.
  - Default document name.

These changes can be built incrementally on top of the current structure.


