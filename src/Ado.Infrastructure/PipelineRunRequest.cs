using System.Text.Json;
using Ado.Domain;

namespace Ado.Infrastructure;

// Do not expose request values through diagnostic serialization or ToString().
public sealed class PipelineRunRequest
{
    private readonly Dictionary<string, object> body = new();
    public int ParameterCount { get; private set; }
    public int VariableCount { get; private set; }
    public string? RefName { get; private set; }

    public static async Task<PipelineRunRequest> LoadAsync(string? parametersFile, string? variablesFile,
        string? refName, CancellationToken cancellationToken)
    {
        var request = new PipelineRunRequest();
        if (refName is not null)
        {
            if (refName.Length > 1024 || refName.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))
                || !(refName.StartsWith("refs/heads/", StringComparison.Ordinal) && refName.Length > 11
                    || refName.StartsWith("refs/tags/", StringComparison.Ordinal) && refName.Length > 10))
                throw new AdoException("invalid_ref", "--ref requires a full refs/heads/... or refs/tags/... reference without whitespace or controls.", ExitCode.Usage);
            request.RefName = refName;
            request.body["resources"] = new { repositories = new { self = new { refName } } };
        }
        if (parametersFile is not null)
        {
            var parameters = await ReadObjectAsync(parametersFile, cancellationToken);
            request.ParameterCount = parameters.EnumerateObject().Count();
            request.body["templateParameters"] = parameters;
        }
        if (variablesFile is not null)
        {
            var variables = await ReadObjectAsync(variablesFile, cancellationToken);
            foreach (var variable in variables.EnumerateObject())
            {
                if (variable.Value.ValueKind != JsonValueKind.Object
                    || !variable.Value.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String)
                    throw Invalid();
                foreach (var field in variable.Value.EnumerateObject())
                    if (field.Name != "value" && !(field.Name == "isSecret" && field.Value.ValueKind is JsonValueKind.True or JsonValueKind.False))
                        throw Invalid();
            }
            request.VariableCount = variables.EnumerateObject().Count();
            request.body["variables"] = variables;
        }
        // Enforce the serialized bound during local planning too: JSON escaping can expand input.
        if (System.Text.Encoding.UTF8.GetByteCount(request.Serialize()) > 1024 * 1024)
            throw new AdoException("request_limit_exceeded", "The run request exceeds the 1 MiB safety limit.", ExitCode.Usage);
        return request;
    }

    internal string Serialize() => JsonSerializer.Serialize(body);
    internal string SerializePreview()
    {
        // Caller-controlled input cannot override this flag; never mutate the start request.
        var preview = new Dictionary<string, object>(body) { ["previewRun"] = true };
        string json = JsonSerializer.Serialize(preview);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 1024 * 1024)
            throw new AdoException("request_limit_exceeded", "The preview request exceeds the 1 MiB safety limit.", ExitCode.Usage);
        return json;
    }

    public void ValidatePreview() => _ = SerializePreview();

    internal string RedactInputValues(string text, CancellationToken cancellationToken)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        void Collect(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } value)
                values.Add(value);
            else if (element.ValueKind == JsonValueKind.Object)
                foreach (var property in element.EnumerateObject()) Collect(property.Value);
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) Collect(item);
        }
        foreach (object value in body.Values)
            if (value is JsonElement element) Collect(element);
        // Literal and JSON-escaped forms only; arbitrary template transformations cannot be inferred.
        foreach (string value in values.ToArray()) values.Add(JsonSerializer.Serialize(value)[1..^1]);
        foreach (string value in values.OrderByDescending(value => value.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            text = text.Replace(value, "[REDACTED]", StringComparison.Ordinal);
        }
        return text;
    }
    public override string ToString() => "[pipeline run request; values omitted]";

    private static async Task<JsonElement> ReadObjectAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            const int maximum = 256 * 1024;
            if (stream.Length > maximum) throw Invalid();
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            while (true)
            {
                int read = await stream.ReadAsync(chunk, cancellationToken);
                if (read == 0) break;
                if (buffer.Length + read > maximum) throw Invalid();
                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }
            using var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() > 256) throw Invalid();
            ValidateKeys(root);
            return root.Clone();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        { throw Invalid(); }
    }

    private static void ValidateKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name) || property.Name.Any(char.IsControl) || !names.Add(property.Name)) throw Invalid();
                ValidateKeys(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) ValidateKeys(item);
    }

    private static AdoException Invalid() => new("invalid_run_inputs",
        "Run input files must be readable JSON objects within 256 KiB, depth 16 and 256 top-level entries, without duplicate keys. Variables require string value and optional boolean isSecret fields.", ExitCode.Usage);
}
