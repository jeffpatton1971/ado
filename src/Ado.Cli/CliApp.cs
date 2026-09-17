using System.CommandLine;
using System.Reflection;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Configuration;

namespace Ado.Cli;

public static class CliApp
{
    public static string Version => typeof(CliApp).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        var root = new RootCommand("Azure DevOps Services CLI. Licensed under AGPL-3.0-only; no warranty.");
        var json = new Option<bool>("--json") { Recursive = true, Description = "Emit a stable JSON envelope." };
        var format = new Option<string>("--output") { Recursive = true, Description = "table or json" };
        var configPath = new Option<string>("--config") { Recursive = true, Description = "Explicit strict JSON configuration file." };
        var profile = new Option<string>("--profile") { Recursive = true, Description = "Named profile (no inheritance)." };
        var organization = new Option<string>("--organization") { Recursive = true };
        var project = new Option<string>("--project") { Recursive = true };
        var limit = new Option<int?>("--limit") { Recursive = true };
        var timeout = new Option<int?>("--timeout") { Recursive = true, Description = "Request timeout in seconds." };
        foreach (var option in new Option[] { json, format, configPath, profile, organization, project, limit, timeout })
            root.Options.Add(option);
        var config = new Command("config", "Inspect configuration without retrieving credentials.");
        var paths = new Command("paths", "Show resolved configuration locations.");
        var show = new Command("show", "Show validated configuration without secrets.");
        var effective = new Option<bool>("--effective") { Description = "Resolve flags, environment and the selected profile." };
        show.Options.Add(effective);
        config.Subcommands.Add(paths);
        config.Subcommands.Add(show);
        root.Subcommands.Add(config);

        var parsed = root.Parse(args);
        bool jsonOutput = parsed.GetValue(json) || (parsed.GetValue(format) ?? environment("ADO_OUTPUT")) == "json";
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
            if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
            {
                if (jsonOutput)
                    await OutputWriter.SuccessAsync(output, new { version = Version, commands = new[] { "config paths", "config show" } }, true);
                else
                {
                    var helpArgs = args.Length == 0 ? new[] { "--help" } : args;
                    return await root.Parse(helpArgs).InvokeAsync(new InvocationConfiguration { Output = output, Error = error }, cancellationToken);
                }
                return 0;
            }

            var location = ConfigurationPaths.Resolve(parsed.GetValue(configPath), environment);
            if (parsed.CommandResult.Command == paths)
            {
                await OutputWriter.SuccessAsync(output, location, jsonOutput);
                return 0;
            }
            if (parsed.CommandResult.Command != show)
                throw new AdoException("command_required", "Choose a command. Use ado --help for supported syntax.", ExitCode.Usage);

            var loaded = await ConfigurationLoader.LoadAsync(location, cancellationToken);
            foreach (string warning in loaded.Warnings) await error.WriteLineAsync("warning: " + warning);
            var resolved = ConfigurationResolver.Resolve(loaded.File,
                new(parsed.GetValue(profile), parsed.GetValue(organization), parsed.GetValue(project),
                    parsed.GetValue(json) ? "json" : parsed.GetValue(format), parsed.GetValue(limit), parsed.GetValue(timeout)), environment);
            jsonOutput = resolved.Settings.Output == "json";
            if (parsed.GetValue(effective))
                await OutputWriter.SuccessAsync(output, resolved, jsonOutput);
            else
                await OutputWriter.SuccessAsync(output, loaded.File, jsonOutput);
            return 0;
        }
        catch (AdoException ex)
        {
            await OutputWriter.ErrorAsync(output, error, ex.Code, ex.Message, ex.Retryable, jsonOutput);
            return (int)ex.ExitCode;
        }
        catch (OperationCanceledException)
        {
            await OutputWriter.ErrorAsync(output, error, "cancelled", "Operation cancelled.", false, jsonOutput);
            return (int)ExitCode.Cancelled;
        }
        catch (Exception)
        {
            await OutputWriter.ErrorAsync(output, error, "internal_error", "An unexpected internal error occurred.", false, jsonOutput);
            return (int)ExitCode.Internal;
        }
    }
}
