using System.Text.Json;

namespace ProviderStudio.Query;

/// <summary>
/// Maps ParamsJson (from gRPC OperationRequest) to typed SQL parameters
/// using the configured param_mappings for an operation.
/// </summary>
public static class ParamMapper
{
    public static IReadOnlyList<SqlParam> Map(
        string? paramsJson,
        IReadOnlyList<ParamMappingConfig> mappings)
    {
        JsonElement root = default;
        bool hasJson = !string.IsNullOrWhiteSpace(paramsJson);
        if (hasJson)
        {
            using var doc = JsonDocument.Parse(paramsJson!);
            root = doc.RootElement.Clone(); // Clone so doc can be disposed
        }

        var result = new List<SqlParam>(mappings.Count);

        foreach (var m in mappings.OrderBy(x => x.JsonPath))
        {
            string? rawValue = null;

            if (hasJson && root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(m.JsonPath, out var prop))
            {
                rawValue = prop.ValueKind == JsonValueKind.String
                    ? prop.GetString()
                    : prop.GetRawText();
            }

            if (rawValue is null)
            {
                if (m.IsRequired)
                    throw new ArgumentException(
                        $"Required param '{m.JsonPath}' is missing from ParamsJson.");

                rawValue = m.DefaultValue;
            }

            object? typed = ConvertValue(rawValue, m.ParamType);
            result.Add(new SqlParam(m.ParamName, typed));
        }

        return result;
    }

    private static object? ConvertValue(string raw, string paramType)
    {
        if (string.IsNullOrEmpty(raw)) return DBNull.Value;

        // Special sentinels
        if (raw.Equals("today", StringComparison.OrdinalIgnoreCase))
            raw = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        if (raw.Equals("now", StringComparison.OrdinalIgnoreCase))
            raw = DateTimeOffset.UtcNow.ToString("O");

        return paramType.ToLowerInvariant() switch
        {
            "int"     => int.Parse(raw),
            "decimal" => decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture),
            "bool"    => bool.Parse(raw),
            "date"    => DateOnly.Parse(raw),
            _         => raw,   // "string" and unknown → pass as string
        };
    }
}
