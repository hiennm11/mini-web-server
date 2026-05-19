# Static File Server Milestone 3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Use the parsed request path to serve files from `wwwroot`.

**Architecture:** Keep `Program.cs` as raw socket host. Add pure response/file-serving types testable without sockets. Use `HttpResponse` to format status line, headers, body bytes. Use `StaticFileResponder` to map request path to safe file path under `wwwroot`.

**Tech Stack:** .NET 10, C#, raw `Socket`, `File.ReadAllBytes`, manual HTTP response bytes, console-based tests.

---

## Tasks

### Task 1: Static File Tests

- Add tests for root path serving `index.html`.
- Add tests for missing file returning 404.
- Add tests for path traversal returning 404.
- Add tests for `.html` content type.
- Verify tests fail because responder does not exist.

### Task 2: Static File Implementation

- Add `HttpResponse`.
- Add `StaticFileResponder`.
- Serve `wwwroot/index.html` for `/`.
- Serve files under `wwwroot`.
- Prevent paths escaping `wwwroot`.
- Return 404 for missing/unsafe paths.

### Task 3: Host Integration

- Add `wwwroot/index.html`.
- Replace fixed `Hello World!` response with `StaticFileResponder.CreateResponse(parsedRequest, "wwwroot").ToBytes()`.
- Log status and served path.

### Task 4: Verification

- Run parser/static tests.
- Run solution build.
- Run server.
- Verify `/` returns `index.html`.
- Verify `/missing.txt` returns 404.
- Verify `/../CONTEXT.md` does not expose repo files.

### Task 5: Learning Note And Context

- Add milestone 3 learning note.
- Update `CONTEXT.md`.

