# HTTP Request Understanding Milestone 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Parse the raw HTTP request text into method, path, version, and headers, then log the parsed view beside the raw bytes.

**Architecture:** Keep raw sockets in `MiniWebServer.Host`. Add tiny pure parsing types in the host project so the parser can be tested without opening sockets. Add a no-dependency console test project that references the host project and exits nonzero on failed assertions.

**Tech Stack:** .NET 10, C#, raw `Socket`, manual HTTP parsing, console-based tests.

---

## Tasks

### Task 1: Parser Tests

**Files:**
- Create: `tests/MiniWebServer.Host.Tests/MiniWebServer.Host.Tests.csproj`
- Create: `tests/MiniWebServer.Host.Tests/Program.cs`
- Modify: `MiniWebServer.sln`

- [ ] Add console test project referencing `src/MiniWebServer.Host/MiniWebServer.Host.csproj`.
- [ ] Add tests for:
  - request line parse: `GET /ostep HTTP/1.1`
  - headers parse: `Host`, `User-Agent`
  - invalid/empty request returns fallback unknown request
- [ ] Run tests and confirm fail because parser does not exist.

### Task 2: Parser Implementation

**Files:**
- Create: `src/MiniWebServer.Host/HttpRequest.cs`
- Create: `src/MiniWebServer.Host/HttpRequestParser.cs`

- [ ] Add immutable `HttpRequest` record.
- [ ] Add `HttpRequestParser.Parse(string rawRequest)`.
- [ ] Parse first line by spaces into method, path, version.
- [ ] Parse headers until blank line.
- [ ] Return unknown request for malformed input.
- [ ] Run console tests and confirm pass.

### Task 3: Host Logging

**Files:**
- Modify: `src/MiniWebServer.Host/Program.cs`

- [ ] After logging raw request, parse it.
- [ ] Log:
  - parsed method
  - parsed path
  - parsed version
  - parsed header count
- [ ] Keep response fixed as `Hello World!`.
- [ ] Run tests and build.

### Task 4: Runtime Verification

**Files:**
- No code changes.

- [ ] Run server.
- [ ] `curl http://localhost:8080/`
- [ ] `curl http://localhost:8080/ostep`
- [ ] Confirm logs show raw request and parsed path `/` then `/ostep`.
- [ ] Stop server.

### Task 5: Learning Note

**Files:**
- Create: `docs/learning/milestone-2-http-request-understanding.md`

- [ ] Explain TCP byte stream vs HTTP message structure.
- [ ] Explain .NET string decoding + parser.
- [ ] Explain why parser tests do not need sockets.
- [ ] Commit.

