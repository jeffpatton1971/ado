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
        CancellationToken cancellationToken = default, Func<string, string?>? environment = null,
        TextReader? input = null, HttpMessageHandler? testHandler = null, ICredentialProvider? testNativeProvider = null)
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
        var authType = new Option<string>("--auth-type") { Recursive = true, Description = "pat or entra-token" };
        var token = new Option<string>("--token") { Recursive = true, Description = "Insecure: token may appear in process lists/history. Prefer stdin or keyring." };
        var tokenStdin = new Option<bool>("--token-stdin") { Recursive = true };
        var tokenPrompt = new Option<bool>("--token-prompt") { Recursive = true };
        var credentialProvider = new Option<string>("--credential-provider") { Recursive = true };
        var credentialService = new Option<string>("--credential-service") { Recursive = true };
        var credentialAccount = new Option<string>("--credential-account") { Recursive = true };
        var nonInteractive = new Option<bool>("--non-interactive") { Recursive = true };
        var readOnly = new Option<bool>("--read-only") { Recursive = true };
        var dryRun = new Option<bool>("--dry-run") { Recursive = true };
        foreach (var option in new Option[] { json, format, configPath, profile, organization, project, limit, timeout,
            authType, token, tokenStdin, tokenPrompt, credentialProvider, credentialService, credentialAccount, nonInteractive, readOnly, dryRun })
            root.Options.Add(option);
        var config = new Command("config", "Inspect configuration without retrieving credentials.");
        var paths = new Command("paths", "Show resolved configuration locations.");
        var show = new Command("show", "Show validated configuration without secrets.");
        var effective = new Option<bool>("--effective") { Description = "Resolve flags, environment and the selected profile." };
        show.Options.Add(effective);
        config.Subcommands.Add(paths);
        config.Subcommands.Add(show);
        root.Subcommands.Add(config);
        var projectCommand = new Command("project", "Read Azure DevOps projects.");
        var list = new Command("list", "List accessible projects with bounded pagination.");
        var get = new Command("get", "Get the project selected by --project.");
        var search = new Command("search", "Filter a bounded project scan by name (client-side substring match).");
        var top = new Option<int?>("--top") { Description = "Requested page size." };
        var all = new Option<bool>("--all") { Description = "Scan up to the configured maximum." };
        var continuation = new Option<string>("--continuation-token") { Description = "Numeric project continuation offset." };
        var requireComplete = new Option<bool>("--require-complete") { Description = "Exit 10 if output is truncated." };
        var searchName = new Option<string>("--name") { Description = "Project name fragment." };
        foreach (var command in new[] { list, search })
        {
            foreach (var option in new Option[] { top, all, continuation, requireComplete }) command.Options.Add(option);
        }
        search.Options.Add(searchName);
        projectCommand.Subcommands.Add(list);
        projectCommand.Subcommands.Add(get);
        projectCommand.Subcommands.Add(search);
        root.Subcommands.Add(projectCommand);
        var auth = new Command("auth", "Check access using the selected credential.");
        var check = new Command("check", "Test project endpoint access; other services are not checked.");
        auth.Subcommands.Add(check);
        root.Subcommands.Add(auth);
        var doctor = new Command("doctor", "Local configuration/platform diagnostics; no secret lookup or network request.");
        root.Subcommands.Add(doctor);

        var parsed = root.Parse(args);
        bool jsonOutput = parsed.GetValue(json) || (parsed.GetValue(format) ?? environment("ADO_OUTPUT")) == "json";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (parsed.Errors.Count != 0 || parsed.GetValue(format) is not (null or "json" or "table"))
                throw new AdoException("invalid_arguments", "Invalid arguments. Use ado --help for supported syntax.", ExitCode.Usage);
            if (parsed.GetValue(token) is not null)
                await error.WriteLineAsync("warning: --token can expose the credential in process listings and shell history. Prefer --token-stdin or a credential store.");
            if (args.Contains("--version", StringComparer.Ordinal))
            {
                await output.WriteLineAsync(Version);
                return 0;
            }
            if (args.Length == 0 || parsed.Action is System.CommandLine.Help.HelpAction)
            {
                if (jsonOutput)
                    await OutputWriter.SuccessAsync(output, new { version = Version, commands = new[] { "config paths", "config show", "project list", "project get", "project search", "auth check", "doctor" } }, true);
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
            var selectedCommand = parsed.CommandResult.Command;
            string? serviceCommand = selectedCommand == list ? "project list" : selectedCommand == get ? "project get"
                : selectedCommand == search ? "project search" : selectedCommand == check ? "auth check" : selectedCommand == doctor ? "doctor" : null;
            if (selectedCommand != show && serviceCommand is null)
                throw new AdoException("command_required", "Choose a command. Use ado --help for supported syntax.", ExitCode.Usage);

            var loaded = await ConfigurationLoader.LoadAsync(location, cancellationToken);
            foreach (string warning in loaded.Warnings) await error.WriteLineAsync("warning: " + warning);
            string? profileName = parsed.GetValue(profile) ?? environment("ADO_PROFILE") ?? loaded.File.DefaultProfile;
            var configuredAuth = profileName is not null && loaded.File.Profiles.TryGetValue(profileName, out var selectedProfile)
                ? selectedProfile.Authentication : new CredentialReference();
            CredentialSelection? selection = null;
            if (serviceCommand is not null && serviceCommand != "doctor")
                selection = CredentialSelection.Resolve(configuredAuth, parsed.GetValue(authType), parsed.GetValue(token) is not null,
                    parsed.GetValue(tokenStdin), parsed.GetValue(tokenPrompt), parsed.GetValue(credentialProvider),
                    parsed.GetValue(credentialService), parsed.GetValue(credentialAccount), environment);
            var resolved = ConfigurationResolver.Resolve(loaded.File,
                new(parsed.GetValue(profile), parsed.GetValue(organization), parsed.GetValue(project),
                    parsed.GetValue(json) ? "json" : parsed.GetValue(format), parsed.GetValue(limit), parsed.GetValue(timeout), selection?.OverridesProfile ?? false), environment);
            jsonOutput = resolved.Settings.Output == "json";
            if (serviceCommand is not null)
            {
                if (parsed.GetValue(all) && parsed.GetValue(limit) is not null)
                    throw new AdoException("invalid_pagination", "Choose --all or --limit, not both.", ExitCode.Usage);
                using var argumentToken = parsed.GetValue(token) is { } tokenValue ? new SecretValue(tokenValue) : null;
                return await ServiceCommands.RunAsync(resolved.Settings, selection ?? new(configuredAuth, false), argumentToken,
                    new(serviceCommand, jsonOutput, parsed.GetValue(nonInteractive), parsed.GetValue(readOnly), parsed.GetValue(dryRun),
                        parsed.GetValue(top), parsed.GetValue(all), parsed.GetValue(continuation), parsed.GetValue(requireComplete), parsed.GetValue(searchName)),
                    output, error, input ?? Console.In, environment, testHandler, testNativeProvider, cancellationToken);
            }
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
