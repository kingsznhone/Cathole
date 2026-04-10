using System.CommandLine;
using System.Security.Cryptography;
using CatFlapRelay;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace CatFlapRelay.CLI;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
            args = ["--help"];

        var rootCommand = new RootCommand(Strings.RootDescription);

        var listenOption = new Option<string>("--listen", "-l")
        {
            Description = Strings.ListenDescription,
            Required = true
        };

        var targetOption = new Option<string>("--target", "-t")
        {
            Description = Strings.TargetDescription,
            Required = true
        };

        var nameOption = new Option<string>("--name", "-n")
        {
            Description = Strings.NameDescription,
            DefaultValueFactory = _ => $"Relay_{RandomNumberGenerator.GetString("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 4)}"
        };

        var noTcpOption = new Option<bool>("--no-tcp")
        {
            Description = Strings.NoTcpDescription
        };

        var noUdpOption = new Option<bool>("--no-udp", "-U")
        {
            Description = Strings.NoUdpDescription
        };

        var bufferSizeOption = new Option<int>("--buffer-size", "-b")
        {
            Description = Strings.BufferSizeDescription,
            DefaultValueFactory = _ => 128 * 1024
        };

        var timeoutOption = new Option<int>("--timeout")
        {
            Description = Strings.TimeoutDescription,
            DefaultValueFactory = _ => 1000
        };

        var dualModeOption = new Option<bool>("--dual-mode")
        {
            Description = Strings.DualModeDescription
        };

        var (verboseOpt, quietOpt, logLevelOpt) = AddLoggingOptions(rootCommand);

        rootCommand.Options.Add(listenOption);
        rootCommand.Options.Add(targetOption);
        rootCommand.Options.Add(nameOption);
        rootCommand.Options.Add(noTcpOption);
        rootCommand.Options.Add(noUdpOption);
        rootCommand.Options.Add(bufferSizeOption);
        rootCommand.Options.Add(timeoutOption);
        rootCommand.Options.Add(dualModeOption);

        rootCommand.SetAction(async (parseResult, ct) =>
        {
            var logLevel = ResolveLogLevel(
                parseResult.GetValue(verboseOpt),
                parseResult.GetValue(quietOpt),
                parseResult.GetValue(logLevelOpt));

            return await RunRelayAsync(
                listen: parseResult.GetValue(listenOption)!,
                target: parseResult.GetValue(targetOption)!,
                name: parseResult.GetValue(nameOption)!,
                tcp: !parseResult.GetValue(noTcpOption),
                udp: !parseResult.GetValue(noUdpOption),
                bufferSize: parseResult.GetValue(bufferSizeOption),
                timeoutMs: parseResult.GetValue(timeoutOption),
                dualMode: parseResult.GetValue(dualModeOption),
                logLevel: logLevel,
                ct: ct);
        });

        return await rootCommand.Parse(args).InvokeAsync();
    }

    /// <summary>
    /// Adds --verbose, --quiet, and --log-level options to the given command and returns them.
    /// </summary>
    private static (Option<bool> Verbose, Option<bool> Quiet, Option<string?> LogLevel) AddLoggingOptions(Command command)
    {
        var verbose = new Option<bool>("--verbose", "-v") { Description = Strings.VerboseDescription };
        var quiet = new Option<bool>("--quiet", "-q") { Description = Strings.QuietDescription };
        var logLevel = new Option<string?>("--log-level")
        {
            Description = Strings.LogLevelDescription
        };
        logLevel.CompletionSources.Add("Verbose", "Debug", "Information", "Warning", "Error", "Fatal");

        command.Options.Add(verbose);
        command.Options.Add(quiet);
        command.Options.Add(logLevel);

        return (verbose, quiet, logLevel);
    }

    private static LogEventLevel ResolveLogLevel(bool verbose, bool quiet, string? logLevelStr)
    {
        if (logLevelStr is not null && Enum.TryParse<LogEventLevel>(logLevelStr, ignoreCase: true, out var parsed))
            return parsed;
        if (verbose) return LogEventLevel.Debug;
        if (quiet) return LogEventLevel.Warning;
        return LogEventLevel.Information;
    }

    private static async Task<int> RunRelayAsync(
        string listen, string target, string name,
        bool tcp, bool udp, int bufferSize, int timeoutMs,
        bool dualMode, LogEventLevel logLevel, CancellationToken ct)
    {
        var option = new FlapRelayOption
        {
            Name = name,
            ListenHost = listen,
            TargetHost = target,
            TCP = tcp,
            UDP = udp,
            BufferSize = bufferSize,
            SocketTimeout = TimeSpan.FromMilliseconds(timeoutMs),
            DualMode = dualMode,
        };

        using var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .WriteTo.Console(
                theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .Enrich.FromLogContext()
            .CreateLogger();

        using var loggerFactory = LoggerFactory.Create(b => b.AddSerilog(serilogLogger));
        await using var manager = new FlapRelayManager(loggerFactory);

        manager.AddRelay(option);

        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException) { }

        await manager.StopAllAsync(CancellationToken.None);
        return 0;
    }
}

