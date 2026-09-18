using System.Text.Json;
using Ado.Domain;

namespace Ado.Cli;

internal static class OutputWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly JsonSerializerOptions TextOptions = new(JsonOptions) { WriteIndented = true };

    public static Task SuccessAsync<T>(TextWriter output, T data, bool json, ResultMetadata? metadata = null) => output.WriteLineAsync(json
        ? JsonSerializer.Serialize(new { ok = true, data, meta = metadata ?? new ResultMetadata() }, JsonOptions)
        : JsonSerializer.Serialize(data, TextOptions));

    public static Task ErrorAsync(TextWriter output, TextWriter error, string code, string message, bool retryable, bool json) => json
        ? output.WriteLineAsync(JsonSerializer.Serialize(new { ok = false, error = new { code, message, details = new { }, retryable } }, JsonOptions))
        : error.WriteLineAsync($"{code}: {message}");

    public static Task PartialAsync<T>(TextWriter output, IReadOnlyList<T> data, ResultMetadata meta) => PartialValueAsync(output, data, meta);

    public static Task PartialValueAsync<T>(TextWriter output, T data, ResultMetadata meta) => output.WriteLineAsync(
        JsonSerializer.Serialize(new { ok = false, data, meta, error = new { code = "incomplete_result", message = "Full result completeness could not be established within the selected bounds.", details = new { }, retryable = false } }, JsonOptions));

    public static string TerminalSafe(string value) => string.Concat(value.Select(c =>
        char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ? $"\\u{(int)c:x4}" : c.ToString()));
}
