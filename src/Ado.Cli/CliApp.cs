using System.CommandLine;
using System.Reflection;
using System.Text.Json;
using Ado.Domain;

namespace Ado.Cli;

public static class CliApp
{
    public static string Version => typeof(CliApp).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default)
    {
        var root = new RootCommand("Azure DevOps Services CLI. Licensed under AGPL-3.0-only; no warranty.");
        var json = new Option<bool>("--json") { Recursive = true };
        var format = new Option<string>("--output") { Recursive = true };
        root.Options.Add(json);
        root.Options.Add(format);
        var parsed = root.Parse(args);
        bool jsonOutput = parsed.GetValue(json) || parsed.GetValue(format) == "json";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (parsed.Errors.Count != 0 || parsed.GetValue(format) is not (null or "json" or "table"))
                throw new AdoException("invalid_arguments", "Invalid arguments. Use ado --help for supported syntax.", ExitCode.Usage);

            if (args.Contains("--version", StringComparer.Ordinal))
            {
                await output.WriteLineAsync(Version);
                return 0;
            }

            if (jsonOutput)
            {
                await output.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    ok = true,
                    data = new { description = root.Description, commands = Array.Empty<string>(), version = Version },
                    meta = new { schemaVersion = 1 }
                }));
                return 0;
            }

            root.SetAction(_ => { });
            var help = root.Parse(["--help"]);
            return await help.InvokeAsync(new InvocationConfiguration { Output = output, Error = error }, cancellationToken);
        }
        catch (AdoException ex)
        {
            if (jsonOutput)
            {
                await output.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    ok = false,
                    error = new { code = ex.Code, message = ex.Message, details = new { }, retryable = ex.Retryable }
                }));
            }
            else
                await error.WriteLineAsync($"{ex.Code}: {ex.Message}");
            return (int)ex.ExitCode;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("cancelled: Operation cancelled.");
            return (int)ExitCode.Cancelled;
        }
    }
}
