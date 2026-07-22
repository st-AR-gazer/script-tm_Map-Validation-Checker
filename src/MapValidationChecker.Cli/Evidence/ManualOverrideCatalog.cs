using System.Text.Json;

using MapValidationChecker.Core.Validation;

namespace MapValidationChecker.Cli.Evidence;

internal sealed class ManualOverrideCatalog
{
    private readonly IReadOnlyDictionary<string, ManualOverrideEvidence> entries;

    private ManualOverrideCatalog(IReadOnlyDictionary<string, ManualOverrideEvidence> entries)
    {
        this.entries = entries;
    }

    public static ManualOverrideCatalog Empty { get; } = new(
        new Dictionary<string, ManualOverrideEvidence>(StringComparer.Ordinal));

    public static ManualOverrideCatalog Load(string filePath)
    {
        var entries = new Dictionary<string, ManualOverrideEvidence>(StringComparer.Ordinal);
        var raw = File.ReadAllText(filePath);

        using var document = ParseDocument(raw);

        void AddEntry(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty("uid", out var uidProperty))
            {
                return;
            }

            var uid = uidProperty.GetString();
            if (string.IsNullOrWhiteSpace(uid))
                return;

            var valid = element.TryGetProperty("valid", out var validProperty) &&
                validProperty.ValueKind == JsonValueKind.True
                    ? true
                    : element.TryGetProperty("valid", out validProperty) &&
                        validProperty.ValueKind == JsonValueKind.False
                        ? false
                        : true;

            string? note = null;
            if (element.TryGetProperty("note", out var noteProperty) &&
                noteProperty.ValueKind == JsonValueKind.String)
            {
                note = noteProperty.GetString();
            }

            entries[uid] = new ManualOverrideEvidence(valid, note);
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            AddEntry(document.RootElement);
        }
        else if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in document.RootElement.EnumerateArray())
                AddEntry(element);
        }

        return new ManualOverrideCatalog(entries);
    }

    public ManualOverrideEvidence? Find(string? mapUid)
    {
        if (string.IsNullOrWhiteSpace(mapUid))
            return null;

        return entries.TryGetValue(mapUid, out var entry)
            ? entry
            : null;
    }

    private static JsonDocument ParseDocument(string raw)
    {
        try
        {
            return JsonDocument.Parse(raw);
        }
        catch
        {
            return JsonDocument.Parse(raw.Replace("True", "true").Replace("False", "false"));
        }
    }
}
