using System.Text.Json;

namespace Ado.Cli;

internal static class OutputWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly JsonSerializerOptions TextOptions = new(JsonOptions) { WriteIndented = true };

    public static Task SuccessAsync<T>(TextWriter output, T data, bool json) => output.WriteLineAsync(json
        ? JsonSerializer.Serialize(new { ok = true, data, meta = new { schemaVersion = 1, truncated = false } }, JsonOptions)
        : JsonSerializer.Serialize(data, TextOptions));

    public static Task ErrorAsync(TextWriter output, TextWriter error, string code, string message, bool retryable, bool json) => json
        ? output.WriteLineAsync(JsonSerializer.Serialize(new { ok = false, error = new { code, message, details = new { }, retryable } }, JsonOptions))
        : error.WriteLineAsync($"{code}: {message}");
}
