namespace Linteum.BlazorApp.Api;

/// <summary>
/// Shared error-message helpers used across the resource repositories. Extracted from
/// <c>MyApiClient</c> (P‑MAIN‑03).
/// </summary>
internal static class ApiErrors
{
    /// <summary>Trims and unwraps a quoted error body, falling back when it is empty.</summary>
    public static string ParseErrorMessage(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return raw.Trim().Trim('"');
    }
}
