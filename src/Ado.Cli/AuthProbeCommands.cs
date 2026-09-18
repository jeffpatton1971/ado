using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;

namespace Ado.Cli;

internal static class AuthProbeCommands
{
    public static void Validate(ServiceOptions options, string organization, string? project)
    {
        if (options.Capability is not ("project" or "build" or "artifact" or "feed"))
            throw new AdoException("invalid_capability", "--capability must be project, build, artifact or feed.", ExitCode.Usage);
        if (options.Capability is "build" or "artifact")
            _ = EndpointBuilder.Build(Operations.BuildGet, organization, project!, options.BuildId);
        if (options.Capability == "feed")
        {
            if (options.FeedScope is not ("project" or "organization"))
                throw new AdoException("invalid_feed_scope", "--scope must be project or organization.", ExitCode.Usage);
            if (options.FeedScope == "project") EndpointBuilder.ProjectSegment(project);
            _ = EndpointBuilder.Feed(Operations.FeedGet, organization, options.FeedScope == "organization" ? null : project, options.Feed);
        }
        if ((options.Capability is "project" or "feed") && options.BuildId is not null
            || options.Capability != "feed" && (options.Feed is not null || options.FeedScope != "project"))
            throw new AdoException("invalid_probe_context", "Use --build-id only for build/artifact probes and --feed/--scope only for feed probes.", ExitCode.Usage);
    }

    public static async Task<int> ReadAsync(ServiceTransport transport, ServiceOptions options, string organization, string? project,
        string authenticationType, TextWriter output, CancellationToken cancellationToken)
    {
        ResultMetadata metadata;
        string checkedCapability;
        if (options.Capability == "build")
        {
            metadata = (await new BuildsClient(transport, organization, project!).GetAsync(options.BuildId!.Value, cancellationToken)).Meta;
            checkedCapability = "build get";
        }
        else if (options.Capability == "artifact")
        {
            metadata = (await new BuildArtifactsClient(transport, organization, project!).ListAsync(options.BuildId!.Value, 1, cancellationToken)).Meta;
            checkedCapability = "build artifact list";
        }
        else
        {
            metadata = (await new FeedsClient(transport, organization, options.FeedScope == "organization" ? null : project)
                .ReadAsync(options.Feed, 1, cancellationToken)).Meta;
            checkedCapability = "feed get";
        }
        await OutputWriter.SuccessAsync(output, new
        {
            accessConfirmed = true,
            authenticationType,
            checkedCapability,
            credentialValidity = "not_independently_verified",
            note = "Only the selected metadata endpoint was read. Public access may succeed without proving credential validity. This does not establish content download, other resource or write permissions."
        }, options.Json, metadata with { Truncated = false, ContinuationToken = null, Completeness = "complete", TruncationReason = null });
        return 0;
    }
}
