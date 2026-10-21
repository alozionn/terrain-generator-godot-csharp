#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Terrain.Core.Level;

/// <summary>JSON (de)serialisation of level, layout and palette files: camelCase, comments allowed, unknown fields ignored, missing fields keep defaults.</summary>
public static class LevelJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static T Deserialize<T>(string json) where T : new() =>
        JsonSerializer.Deserialize<T>(json, Options) ?? new T();

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Reads "#rrggbb" into 0..1 RGB; anything else is mid grey.</summary>
    public static Vector3 ParseColor(string? hex)
    {
        if (hex is { Length: 7 } && hex[0] == '#'
            && int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            return new Vector3((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
        return new Vector3(0.5f);
    }

    /// <summary>Problems that make a definition unusable; empty when it is fine.</summary>
    public static string[] Check(LevelDefinition level)
    {
        var issues = new System.Collections.Generic.List<string>();
        if (level.Size <= 0 || level.SampleSpacing <= 0 || level.CellSize <= 0) issues.Add("size, sampleSpacing and cellSize must be positive");
        else if (MathF.Abs(level.CellSize / level.SampleSpacing - MathF.Round(level.CellSize / level.SampleSpacing)) > 1e-4f)
            issues.Add("cellSize must be a whole number of samples");
        if (string.IsNullOrEmpty(level.Layout)) issues.Add("no layout");
        if (string.IsNullOrEmpty(level.Palette)) issues.Add("no palette");
        foreach (var z in level.Zones)
        {
            if (z.Origin is not { Length: 2 } || z.Cells is not { Length: 2 } || z.Height is not { Length: 2 })
                issues.Add($"zone '{z.Id}': origin, cells and height need two values each");
            else if (z.Cells.Any(c => c <= 0) || z.Height[1] < z.Height[0])
                issues.Add($"zone '{z.Id}': cells must be positive and height must be [min, max]");
        }
        return issues.ToArray();
    }
}
