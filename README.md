# Fetcher

A .NET 9 console application that downloads large files from Azure Blob Storage using parallel, chunked, resumable downloads with a live-updating terminal progress display.

## Features

- **Parallel chunked downloads** — splits large blobs into one chunk per concurrent thread (capped at 256 MB) and downloads them in parallel
- **Resumable** — persists download state to a manifest file; interrupted downloads continue where they left off
- **MD5 integrity validation** — verifies the assembled file against the blob's content hash
- **Live progress display** — real-time progress bar with speed, ETA, and chunk status
- **Sleep prevention** — prevents Windows from sleeping during long downloads
- **Configurable** — chunk size, concurrency, output path, and authentication method

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- An Azure Blob Storage account with a blob to download
- Authentication: either a storage account key or credentials configured for [DefaultAzureCredential](https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/)

## Building

```bash
dotnet build Fetcher.slnx
```

## Running Tests

```bash
dotnet test Fetcher.slnx
```

## Usage

```
fetcher <url> [options]

Arguments:
  <url>    Azure Blob Storage URL (required)

Options:
  -o, --output <path>        Output file or directory [default: current directory]
  -k, --key <key>            Storage account key (omit for DefaultAzureCredential)
  -c, --concurrency <n>      Max parallel chunk downloads [default: min(CPU * 4, 32)]
  -s, --chunk-size <mb>      Max chunk size in MB (cap) [default: 256]
  --WaitForDownload          Download all chunks before assembling (disables streaming assembly)
  --debug                    Write download manifest after each chunk
  --version                  Show version
  -h, --help                 Show help
```

### Examples

Download a blob to the current directory using DefaultAzureCredential:

```bash
dotnet run --project src/Fetcher.Cli -- "https://myaccount.blob.core.windows.net/mycontainer/largefile.zip"
```

Download with a storage account key to a specific directory:

```bash
dotnet run --project src/Fetcher.Cli -- "https://myaccount.blob.core.windows.net/mycontainer/largefile.zip" \
  -k "your-storage-account-key" \
  -o "D:\Downloads"
```

Download with custom concurrency and chunk size:

```bash
dotnet run --project src/Fetcher.Cli -- "https://myaccount.blob.core.windows.net/mycontainer/largefile.zip" \
  -c 64 -s 128 -o "output.zip"
```

### Chunking Strategy

By default, the file is divided into `file size / concurrency` sized chunks so that every download thread gets work immediately. The `--chunk-size` option acts as a **cap** — if the computed chunk size exceeds it, the cap is used instead (which may produce more chunks than threads, with the surplus queued).

For example, a 4 GB file with 16 threads produces 16 chunks of 256 MB each. A 16 GB file with 16 threads would compute 1 GB chunks, but the 256 MB default cap limits them to 256 MB, yielding 64 chunks that are processed 16-at-a-time.

### Resume

If a download is interrupted, simply re-run the same command. Fetcher detects the manifest file (`{filename}.fetcher-manifest.json`) next to the output location and resumes from where it left off. If the blob has changed since the previous attempt, the stale state is discarded and the download starts fresh.

## Project Structure

```
Fetcher.slnx
├── src/
│   ├── Fetcher.Core/           Core library (no UI dependencies)
│   │   ├── Configuration/      DownloadOptions record
│   │   ├── Models/             ChunkState, DownloadManifest
│   │   ├── Services/           IBlobService, IChunkDownloader, IFileAssembler, IIntegrityValidator
│   │   ├── Orchestration/      DownloadOrchestrator, IDownloadProgress
│   │   ├── Exceptions/         Typed exceptions
│   │   └── Utilities/          ByteFormatter
│   └── Fetcher.Cli/            Console application
│       ├── Program.cs          CLI entry point (System.CommandLine)
│       ├── Platform/           Windows sleep prevention
│       └── Ui/                 ProgressReporter
└── tests/
    ├── Fetcher.Core.Tests/     Unit tests for core library
    └── Fetcher.Cli.Tests/      CLI argument parsing tests
```

## Architecture

The core library (`Fetcher.Core`) is fully decoupled from the console UI:

- **`IBlobService`** — abstracts Azure Blob SDK operations behind a testable interface
- **`IChunkDownloader`** — downloads a single chunk with retry and exponential backoff
- **`IFileAssembler`** — assembles chunk files into the final output using `RandomAccess` (lock-free parallel writes)
- **`IIntegrityValidator`** — validates file integrity via MD5 hash
- **`DownloadOrchestrator`** — coordinates the full download lifecycle
- **`IDownloadProgress`** — push-based progress reporting interface for UI decoupling

All dependencies are injected via `Microsoft.Extensions.DependencyInjection`, making the system testable and extensible.
