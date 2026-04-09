using System.CommandLine;
using Fetcher.Cli.Platform;
using Fetcher.Cli.Ui;
using Fetcher.Core.Configuration;
using Fetcher.Core.Exceptions;
using Fetcher.Core.Orchestration;
using Fetcher.Core.Services;
using Fetcher.Core.Utilities;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;

var urlArgument = new Argument<Uri>("url", "Azure Blob Storage URL");

var outputOption = new Option<string?>(["-o", "--output"], "Output file or directory");
var keyOption = new Option<string?>(["-k", "--key"], "Storage account key (omit for DefaultAzureCredential)");
var concurrencyOption = new Option<int?>(["-c", "--concurrency"], "Max parallel chunk downloads");
var chunkSizeOption = new Option<int?>(["-s", "--chunk-size"], "Max chunk size in MB (cap)");
var waitOption = new Option<bool>("--WaitForDownload", "Download all chunks before assembling (disables streaming assembly)");
var debugOption = new Option<bool>("--debug", "Write download manifest after each chunk");

var rootCommand = new RootCommand("Fetcher - Azure Blob Storage parallel downloader");
rootCommand.AddArgument(urlArgument);
rootCommand.AddOption(outputOption);
rootCommand.AddOption(keyOption);
rootCommand.AddOption(concurrencyOption);
rootCommand.AddOption(chunkSizeOption);
rootCommand.AddOption(waitOption);
rootCommand.AddOption(debugOption);

rootCommand.SetHandler(async (context) =>
{
    var ct = context.GetCancellationToken();

    var uri = context.ParseResult.GetValueForArgument(urlArgument);
    var output = context.ParseResult.GetValueForOption(outputOption);
    var key = context.ParseResult.GetValueForOption(keyOption);
    var concurrency = context.ParseResult.GetValueForOption(concurrencyOption);
    var chunkSizeMb = context.ParseResult.GetValueForOption(chunkSizeOption);
    var waitForDownload = context.ParseResult.GetValueForOption(waitOption);
    var debug = context.ParseResult.GetValueForOption(debugOption);

    var options = new DownloadOptions
    {
        BlobUri = uri,
        LocalPath = output ?? Directory.GetCurrentDirectory(),
        AccountKey = key,
        MaxConcurrency = concurrency ?? Math.Min(Environment.ProcessorCount * 4, 32),
        MaxChunkSizeBytes = (chunkSizeMb ?? 256) * 1024 * 1024,
        WaitForDownload = waitForDownload,
        WriteDebugManifest = debug
    };

    var services = new ServiceCollection();
    services.AddSingleton(options);
    services.AddSingleton<IBlobService, AzureBlobService>();
    services.AddSingleton<IChunkDownloader, ChunkDownloader>();
    services.AddSingleton<IFileAssembler, FileAssembler>();
    services.AddSingleton<IIntegrityValidator, Md5Validator>();
    services.AddSingleton<DownloadOrchestrator>();

    using var provider = services.BuildServiceProvider();
    var orchestrator = provider.GetRequiredService<DownloadOrchestrator>();

    var progress = new ProgressReporter();

    using var sleepGuard = SleepPrevention.Prevent();
    using var uiCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

    // Start progress display
    var uiTask = RunProgressDisplayAsync(progress, uiCts.Token);

    try
    {
        var result = await orchestrator.DownloadAsync(progress, ct);

        await uiCts.CancelAsync();
        try { await uiTask; } catch (OperationCanceledException) { }

        // Clear both lines and move to a fresh line
        Console.Write("\r\x1b[2K");  // clear current line
        Console.Write("\x1b[A\x1b[2K"); // move up and clear

        if (result.Success)
        {
            Console.WriteLine($"Download complete: {result.OutputPath}");
            Console.WriteLine($"  Size:       {ByteFormatter.Format(result.TotalBytes)}");
            Console.WriteLine($"  Download:   {result.DownloadDuration.Humanize(2)}");
            Console.WriteLine($"  Assembly:   {result.AssemblyDuration.Humanize(2)}");
            Console.WriteLine($"  Validation: {result.ValidationDuration.Humanize(2)}");
            Console.WriteLine($"  Total:      {result.TotalDuration.Humanize(2)}");
        }
        else
        {
            Console.Error.WriteLine($"Download failed: {result.Error?.Message}");
            context.ExitCode = 1;
        }
    }
    catch (FileAlreadyExistsException ex)
    {
        await uiCts.CancelAsync();
        try { await uiTask; } catch (OperationCanceledException) { }
        Console.Error.WriteLine(ex.Message);
        context.ExitCode = 1;
    }
    catch (BlobNotFoundException ex)
    {
        await uiCts.CancelAsync();
        try { await uiTask; } catch (OperationCanceledException) { }
        Console.Error.WriteLine(ex.Message);
        context.ExitCode = 1;
    }
});

return await rootCommand.InvokeAsync(args);

static async Task RunProgressDisplayAsync(ProgressReporter progress, CancellationToken ct)
{
    // Reserve two lines for the display
    Console.WriteLine();
    Console.WriteLine();

    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));

    try
    {
        while (await timer.WaitForNextTickAsync(ct))
        {
            var phase = progress.CurrentPhase;

            // Download line
            var rawDlPct = progress.DownloadProgress * 100;
            var dlPct = progress.DownloadProgress >= 1.0 ? 100.0 : Math.Floor(rawDlPct * 10) / 10;
            var speed = ByteFormatter.Format((long)progress.BytesPerSecond);
            var written = ByteFormatter.Format(progress.TotalBytesWritten);
            var total = ByteFormatter.Format(progress.TotalSize);
            var eta = progress.EstimatedTimeRemaining;
            var dlChunks = $"{progress.CompletedChunks}/{progress.TotalChunks}";
            var dlBar = BuildProgressBar(progress.DownloadProgress, 30);

            var dlLabel = phase == DownloadPhase.Complete ? "Complete"
                : phase == DownloadPhase.Failed ? "Failed"
                : "Download";

            var dlLine = $"  [{dlLabel,-10}] {dlBar} {dlPct,5:F1}% | {written}/{total} | {speed}/s | ETA: {eta:hh\\:mm\\:ss} | Chunks: {dlChunks}";

            // Assembly line
            var asmPct = progress.AssemblyProgress * 100;
            var asmChunks = $"{progress.AssembledChunks}/{progress.TotalChunks}";
            var asmBar = BuildProgressBar(progress.AssemblyProgress, 30);
            var asmLine = $"  [{"Assembly",-10}] {asmBar} {asmPct,5:F1}% | Chunks: {asmChunks}";

            // Move up 2 lines, write both, leave cursor at end of second line
            Console.Write($"\x1b[2A\r\x1b[2K{dlLine}\n\x1b[2K{asmLine}");
        }
    }
    catch (OperationCanceledException)
    {
        // Expected when download completes
    }
}

static string BuildProgressBar(double progress, int width)
{
    var filled = (int)Math.Min(progress * width, width);
    var empty = width - filled;
    return $"[{new string('=', filled)}{new string(' ', empty)}]";
}
