# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build Fetch.slnx               # build all projects
dotnet test Fetch.slnx                # run all tests
dotnet test Fetch.slnx --filter "Name~<TestMethodName>"      # run a single test by name
dotnet test Fetch.slnx --filter "ClassName=<FullClassName>"  # run a test class
dotnet run --project src/Fetch.Cli -- "<blob-url>"           # run the CLI locally
```

## Architecture

The solution is split into a UI-free core library and a console application:

- **`Fetch.Core`** — all download logic, no UI dependencies. Driven entirely by interfaces (`IBlobService`, `IChunkDownloader`, `IFileAssembler`, `IIntegrityValidator`, `IDownloadProgress`). Composed via constructor injection.
- **`Fetch.Cli`** — CLI parsing (`System.CommandLine`), DI wiring (`Microsoft.Extensions.DependencyInjection` + `IHost`), progress UI (`RazorConsole` components), sleep prevention (Windows P/Invoke).

`DownloadOrchestrator` is the top-level coordinator. It is created by `OrchestratorFactory` and drives the full lifecycle: metadata fetch → manifest load/create → parallel chunk download → streaming assembly → MD5 validation → rename from `.part` to final.

### Streaming assembly (default)

Chunk download and file assembly run concurrently. A `Channel<ChunkState>` (unbounded, single-reader) decouples producers (chunk downloader tasks behind a `SemaphoreSlim`) from the assembler consumer. `--WaitForDownload` disables this and assembles via `RandomAccess.WriteAsync()` after all chunks complete.

### Resume

`DownloadManifest` is persisted as JSON alongside the output file. On restart, the orchestrator validates the manifest against the blob (URI, total size, content hash) and migrates chunk file visibility if `--ShowChunks` changed between runs.

### Authentication (`AzureBlobService`)

Three paths:
1. `-k <key>` → `StorageSharedKeyCredential`
2. No key → `ChainedTokenCredential(DefaultAzureCredential, TimeoutCredential(InteractiveBrowserCredential, 2min))`

`TimeoutCredential` is a thin `TokenCredential` wrapper that links a `CancellationTokenSource` with `CancelAfter` into every `GetToken`/`GetTokenAsync` call.

### Error handling

- Custom typed exceptions: `BlobNotFoundException`, `IntegrityException`, `DownloadException`, `FileAlreadyExistsException`
- Retry with exponential backoff at the chunk level. Transient: `IOException`, HTTP 408/429/500/502/503/504. Non-transient errors fail immediately.
- Manifest is saved on failure so the download can resume.

## Conventions

- `sealed` classes and `record` types are preferred
- `DownloadOptions` is an immutable `record` — add new options there with `init` properties and wire them up in `Program.cs`
- Private fields: `_camelCase`. Async methods: `*Async` suffix.
- Tests use **xUnit**, **NSubstitute** (mocks), and **FluentAssertions**. Each test class creates a real temp directory in its constructor and cleans it up in `Dispose()`.
