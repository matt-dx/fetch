using System.CommandLine;
using System.CommandLine.Parsing;
using Fetch.Cli.Ui;
using Fetch.Cli.Ui.Components;
using Fetch.Core.Configuration;
using Fetch.Core.Orchestration;
using Fetch.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;

var urlArgument = new Argument<Uri[]>("urls", "Azure Blob Storage URL(s)")
{
    Arity = ArgumentArity.OneOrMore
};

var outputOption = new Option<string?>(["-o", "--output"], "Output file or directory");
var keyOption = new Option<string?>(["-k", "--key"], "Storage account key (omit for DefaultAzureCredential)");
var concurrencyOption = new Option<int?>(["-c", "--concurrency"], "Max parallel chunk downloads");
var chunkSizeOption = new Option<int?>(["-s", "--chunk-size"], "Max chunk size in MB (cap)");
var waitOption = new Option<bool>("--WaitForDownload", "Download all chunks before assembling (disables streaming assembly)");
var showChunksOption = new Option<bool>("--ShowChunks", "Show chunk and manifest files (do not hide them)");
var debugOption = new Option<bool>("--debug", "Write download manifest after each chunk");

var rootCommand = new RootCommand("Fetch - Azure Blob Storage parallel downloader");
rootCommand.AddArgument(urlArgument);
rootCommand.AddOption(outputOption);
rootCommand.AddOption(keyOption);
rootCommand.AddOption(concurrencyOption);
rootCommand.AddOption(chunkSizeOption);
rootCommand.AddOption(waitOption);
rootCommand.AddOption(showChunksOption);
rootCommand.AddOption(debugOption);

// Parse first — let System.CommandLine handle --help, --version, and validation errors
var parseResult = rootCommand.Parse(args);
if (parseResult.Errors.Any()
    || args.Contains("--help") || args.Contains("-h") || args.Contains("--version"))
{
    return await rootCommand.InvokeAsync(args);
}

var uris = parseResult.GetValueForArgument(urlArgument);
var output = parseResult.GetValueForOption(outputOption);
var key = parseResult.GetValueForOption(keyOption);
var concurrency = parseResult.GetValueForOption(concurrencyOption);
var chunkSizeMb = parseResult.GetValueForOption(chunkSizeOption);
var waitForDownload = parseResult.GetValueForOption(waitOption);
var showChunks = parseResult.GetValueForOption(showChunksOption);
var debug = parseResult.GetValueForOption(debugOption);

var options = new DownloadOptions
{
    BlobUri = uris[0],
    LocalPath = output ?? Directory.GetCurrentDirectory(),
    AccountKey = key,
    MaxConcurrency = concurrency ?? Math.Min(Environment.ProcessorCount * 4, 32),
    MaxChunkSizeBytes = (chunkSizeMb ?? 256) * 1024 * 1024,
    WaitForDownload = waitForDownload,
    ShowChunks = showChunks,
    WriteDebugManifest = debug
};

// Build and run the RazorConsole host at the top level so it has
// full control over stdin for keyboard input (TextButton, etc.)
var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
builder.UseRazorConsole<MainComponent>();

// Allow enough time on Ctrl+C for the orchestrator to save the manifest
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(uris);
builder.Services.AddSingleton<ProgressReporter>();
builder.Services.AddSingleton<IBlobService, AzureBlobService>();
builder.Services.AddSingleton<IChunkDownloader, ChunkDownloader>();
builder.Services.AddSingleton<IFileAssembler, FileAssembler>();
builder.Services.AddSingleton<IIntegrityValidator, Md5Validator>();
builder.Services.AddSingleton<DownloadOrchestrator>();

await builder.Build().RunAsync();
return Environment.ExitCode;
