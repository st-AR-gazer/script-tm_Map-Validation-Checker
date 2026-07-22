using System.Collections;
using System.Globalization;
using System.Text;

using GBX.NET.Engines.Script;

using MapValidationChecker.Cli.Infrastructure;
using MapValidationChecker.Core.Validation;

namespace MapValidationChecker.Cli.Evidence;

internal static class ValidationTagReader
{
    private const string SignatureText = "RaceValidationReplay Remover made by ar";
    private static readonly string SignatureHex = BuildSignatureHexString(SignatureText);

    public static ValidationTagEvidence? Read(CScriptTraitsMetadata? metadata)
    {
        if (metadata?.Traits is null || metadata.Traits.Count == 0)
            return null;

        var traits = metadata.Traits.ToList();

        for (var index = traits.Count - 1; index >= 0; index--)
        {
            var pair = traits[index];
            if (pair.Value is not CScriptTraitsMetadata.ScriptStructTrait structTrait)
                continue;

            var hasCompressed = TryGetStructFieldText(structTrait, "compressed", out var compressed) &&
                !string.IsNullOrWhiteSpace(compressed);
            var hasSignature = hasCompressed && MatchesSignature(compressed!);

            var note = TryGetStructFieldText(structTrait, "Note", out var noteText)
                ? noteText
                : null;
            int? authorTimeMs = null;
            string? authorTimeSource = null;

            if (TryGetStructFieldStruct(structTrait, "ChallengeParameters", out var challengeParameters) &&
                challengeParameters is not null &&
                TryGetStructFieldInt(challengeParameters, "AuthorTime", out var challengeAuthorTimeMs) &&
                challengeAuthorTimeMs >= 0)
            {
                authorTimeMs = challengeAuthorTimeMs;
                authorTimeSource = "ChallengeParameters.AuthorTime";
            }

            if (!authorTimeMs.HasValue &&
                TryExtractAuthorTimeFromRemovalNote(note, out var noteAuthorTimeMs, out var noteToken))
            {
                authorTimeMs = noteAuthorTimeMs;
                authorTimeSource = $"Note.AuthorTime ({noteToken})";
            }

            var noteLooksLikeRemoval = !string.IsNullOrWhiteSpace(note) &&
                (note.IndexOf("AuthorTime=", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 note.IndexOf("RaceValidationReplay", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 note.IndexOf("ValidationReplay Remover", StringComparison.OrdinalIgnoreCase) >= 0);
            var looksLikeRemovalStruct = hasCompressed || noteLooksLikeRemoval;

            if (!hasSignature && !looksLikeRemovalStruct)
                continue;

            if (!hasSignature && !authorTimeMs.HasValue)
                continue;

            return new ValidationTagEvidence(
                pair.Key,
                note,
                authorTimeMs,
                authorTimeSource,
                hasSignature);
        }

        for (var index = traits.Count - 1; index >= 0; index--)
        {
            var pair = traits[index];
            if (pair.Value is not null && TraitContainsSignature(pair.Value))
                return new ValidationTagEvidence(pair.Key, null, null, null, HasSignature: true);
        }

        return null;
    }

    private static bool TryGetStructField(
        CScriptTraitsMetadata.ScriptStructTrait structTrait,
        string fieldName,
        out CScriptTraitsMetadata.ScriptTrait? trait)
    {
        trait = null;
        if (structTrait.Value is null ||
            !structTrait.Value.TryGetValue(fieldName, out var value) ||
            value is null)
        {
            return false;
        }

        trait = value;
        return true;
    }

    private static bool TryGetStructFieldStruct(
        CScriptTraitsMetadata.ScriptStructTrait structTrait,
        string fieldName,
        out CScriptTraitsMetadata.ScriptStructTrait? childStruct)
    {
        childStruct = null;
        if (!TryGetStructField(structTrait, fieldName, out var trait) ||
            trait is not CScriptTraitsMetadata.ScriptStructTrait value)
        {
            return false;
        }

        childStruct = value;
        return true;
    }

    private static bool TryGetStructFieldText(
        CScriptTraitsMetadata.ScriptStructTrait structTrait,
        string fieldName,
        out string? value)
    {
        value = null;
        if (!TryGetStructField(structTrait, fieldName, out var trait) || trait is null)
            return false;

        object? raw = null;
        try
        {
            raw = trait.GetValue();
        }
        catch
        {
        }

        if (raw is string text)
        {
            value = text;
            return true;
        }

        if (raw is null)
            return false;

        value = raw.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetStructFieldInt(
        CScriptTraitsMetadata.ScriptStructTrait structTrait,
        string fieldName,
        out int value)
    {
        value = default;
        if (!TryGetStructField(structTrait, fieldName, out var trait) || trait is null)
            return false;

        object? raw = null;
        try
        {
            raw = trait.GetValue();
        }
        catch
        {
        }

        if (raw is int intValue)
        {
            value = intValue;
            return true;
        }

        if (raw is long longValue)
        {
            value = unchecked((int)longValue);
            return true;
        }

        if (raw is uint uintValue)
        {
            value = unchecked((int)uintValue);
            return true;
        }

        return raw is not null && int.TryParse(raw.ToString(), out value);
    }

    private static bool TryExtractAuthorTimeFromRemovalNote(
        string? note,
        out int authorTimeMs,
        out string? token)
    {
        authorTimeMs = default;
        token = null;

        if (string.IsNullOrWhiteSpace(note))
            return false;

        var start = note.IndexOf("AuthorTime=", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return false;

        start += "AuthorTime=".Length;
        var end = note.IndexOf(';', start);
        var raw = (end >= 0 ? note[start..end] : note[start..]).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        token = raw;
        if (!GbxTime.TryParse(raw, out var parsedTimeMs))
            return false;

        authorTimeMs = parsedTimeMs;
        return true;
    }

    private static bool TraitContainsSignature(CScriptTraitsMetadata.ScriptTrait trait)
    {
        var visited = new HashSet<object>(ObjectReferenceEqualityComparer.Instance);
        var stack = new Stack<object>();
        stack.Push(trait);

        while (stack.Count > 0)
        {
            var value = stack.Pop();
            if (value is string text)
            {
                if (MatchesSignature(text))
                    return true;
                continue;
            }

            if (!visited.Add(value))
                continue;

            if (value is CScriptTraitsMetadata.ScriptTrait scriptTrait)
            {
                object? traitValue = null;
                try
                {
                    traitValue = scriptTrait.GetValue();
                }
                catch
                {
                }

                if (traitValue is not null && !ReferenceEquals(traitValue, value))
                    stack.Push(traitValue);

                if (scriptTrait is CScriptTraitsMetadata.ScriptStructTrait structTrait &&
                    structTrait.Value is not null)
                {
                    foreach (var child in structTrait.Value.Values)
                    {
                        if (child is not null)
                            stack.Push(child);
                    }
                }
                else if (scriptTrait is CScriptTraitsMetadata.ScriptArrayTrait arrayTrait &&
                    arrayTrait.Value is not null)
                {
                    foreach (var child in arrayTrait.Value)
                    {
                        if (child is not null)
                            stack.Push(child);
                    }
                }
            }

            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is not null)
                        stack.Push(entry.Key);
                    if (entry.Value is not null)
                        stack.Push(entry.Value);
                }
                continue;
            }

            if (value is IEnumerable enumerable)
            {
                var count = 0;
                foreach (var item in enumerable)
                {
                    if (item is not null)
                        stack.Push(item);
                    if (++count > 20_000)
                        break;
                }
            }
        }

        return false;
    }

    private static bool MatchesSignature(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var candidate = value.Trim();
        if (string.Equals(candidate, SignatureText, StringComparison.Ordinal))
            return true;
        if (string.Equals(candidate, SignatureHex, StringComparison.OrdinalIgnoreCase))
            return true;

        return TryDecodeHexToAscii(candidate, out var decoded) &&
            string.Equals(decoded, SignatureText, StringComparison.Ordinal);
    }

    private static bool TryDecodeHexToAscii(string hex, out string decoded)
    {
        decoded = string.Empty;
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        var candidate = hex.Trim();
        if (candidate.Length % 2 != 0)
            return false;

        var byteCount = candidate.Length / 2;
        Span<byte> bytes = byteCount <= 512 ? stackalloc byte[byteCount] : new byte[byteCount];

        for (var index = 0; index < byteCount; index++)
        {
            var slice = candidate.AsSpan(index * 2, 2);
            if (!byte.TryParse(
                    slice,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var byteValue))
            {
                return false;
            }

            bytes[index] = byteValue;
        }

        decoded = Encoding.ASCII.GetString(bytes);
        return true;
    }

    private static string BuildSignatureHexString(string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var byteValue in bytes)
            builder.Append(byteValue.ToString("X2", CultureInfo.InvariantCulture));
        return builder.ToString();
    }
}
