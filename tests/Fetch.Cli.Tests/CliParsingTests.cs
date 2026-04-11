using System.CommandLine;
using System.CommandLine.Parsing;
using FluentAssertions;

namespace Fetch.Cli.Tests;

public class CliParsingTests
{
    private static RootCommand BuildRootCommand()
    {
        var urlArgument = new Argument<Uri[]>("urls", "Azure Blob Storage URL(s)")
        {
            Arity = ArgumentArity.OneOrMore
        };
        var outputOption = new Option<string?>(["-o", "--output"], "Output file or directory");
        var keyOption = new Option<string?>(["-k", "--key"], "Storage account key");
        var concurrencyOption = new Option<int?>(["-c", "--concurrency"], "Max parallel chunk downloads");
        var chunkSizeOption = new Option<int?>(["-s", "--chunk-size"], "Chunk size in MB");
        var showChunksOption = new Option<bool>("--ShowChunks", "Show chunk and manifest files");
        var debugOption = new Option<bool>("--debug", "Write download manifest");

        var rootCommand = new RootCommand("Fetch");
        rootCommand.AddArgument(urlArgument);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(keyOption);
        rootCommand.AddOption(concurrencyOption);
        rootCommand.AddOption(chunkSizeOption);
        rootCommand.AddOption(showChunksOption);
        rootCommand.AddOption(debugOption);

        return rootCommand;
    }

    [Fact]
    public void Parse_SingleUrl_Succeeds()
    {
        var cmd = BuildRootCommand();
        var result = cmd.Parse("https://account.blob.core.windows.net/container/file.zip");

        result.Errors.Should().BeEmpty();
        var uris = result.GetValueForArgument((cmd.Arguments.First() as Argument<Uri[]>)!);
        uris.Should().HaveCount(1);
        uris[0].Should().Be(new Uri("https://account.blob.core.windows.net/container/file.zip"));
    }

    [Fact]
    public void Parse_MultipleUrls_Succeeds()
    {
        var cmd = BuildRootCommand();
        var result = cmd.Parse(
            "https://account.blob.core.windows.net/c/file1.zip " +
            "https://account.blob.core.windows.net/c/file2.zip " +
            "https://account.blob.core.windows.net/c/file3.zip");

        result.Errors.Should().BeEmpty();
        var uris = result.GetValueForArgument((cmd.Arguments.First() as Argument<Uri[]>)!);
        uris.Should().HaveCount(3);
    }

    [Fact]
    public void Parse_MissingUrl_HasErrors()
    {
        var cmd = BuildRootCommand();
        var result = cmd.Parse("");

        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Parse_AllOptions_Succeeds()
    {
        var cmd = BuildRootCommand();
        var result = cmd.Parse("https://test.blob.core.windows.net/c/f -o /tmp/out -k mykey -c 8 -s 128 --ShowChunks --debug");

        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_OutputOption_ParsesCorrectly()
    {
        var cmd = BuildRootCommand();
        var result = cmd.Parse("https://test.blob.core.windows.net/c/f --output /my/path");

        result.Errors.Should().BeEmpty();
        var outputOpt = cmd.Options.First(o => o.Name == "output") as Option<string?>;
        result.GetValueForOption(outputOpt!).Should().Be("/my/path");
    }

    [Fact]
    public void Parse_ConcurrencyOption_ParsesAsInt()
    {
        var cmd = BuildRootCommand();
        var result = cmd.Parse("https://test.blob.core.windows.net/c/f -c 32");

        result.Errors.Should().BeEmpty();
        var concurrencyOpt = cmd.Options.First(o => o.Name == "concurrency") as Option<int?>;
        result.GetValueForOption(concurrencyOpt!).Should().Be(32);
    }

    [Fact]
    public void Parse_ShowChunksOption_DefaultsFalse()
    {
        var cmd = BuildRootCommand();
        var result = cmd.Parse("https://test.blob.core.windows.net/c/f");

        result.Errors.Should().BeEmpty();
        var showChunksOpt = cmd.Options.First(o => o.Name == "ShowChunks") as Option<bool>;
        result.GetValueForOption(showChunksOpt!).Should().BeFalse();
    }

    [Fact]
    public void Parse_ShowChunksOption_WhenPresent_ReturnsTrue()
    {
        var cmd = BuildRootCommand();
        var result = cmd.Parse("https://test.blob.core.windows.net/c/f --ShowChunks");

        result.Errors.Should().BeEmpty();
        var showChunksOpt = cmd.Options.First(o => o.Name == "ShowChunks") as Option<bool>;
        result.GetValueForOption(showChunksOpt!).Should().BeTrue();
    }
}
