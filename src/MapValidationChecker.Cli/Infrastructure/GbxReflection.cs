using System.Collections;
using System.Reflection;

namespace MapValidationChecker.Cli.Infrastructure;

internal static class GbxReflection
{
    public static SampleCollectionSummary GetSampleCollectionSummary(
        object value,
        string collectionPropertyName)
    {
        if (!TryGetMemberValue(value, collectionPropertyName, out var rawCollection) ||
            rawCollection is null ||
            rawCollection is string ||
            rawCollection is not IEnumerable enumerable)
        {
            return default;
        }

        var count = 0;
        int? lastIndexWithTime = null;
        int? lastTimeMs = null;

        foreach (var sample in enumerable)
        {
            var index = count;
            count++;

            var sampleTimeMs = ExtractSampleTimeMs(sample);
            if (sampleTimeMs.HasValue)
            {
                lastIndexWithTime = index;
                lastTimeMs = sampleTimeMs.Value;
            }

            if (count > 20_000)
                break;
        }

        return new SampleCollectionSummary(count, lastIndexWithTime, lastTimeMs);
    }

    public static int? TryGetIntMemberValue(object value, string memberName)
    {
        if (!TryGetMemberValue(value, memberName, out var raw) || raw is null)
            return null;

        if (raw is int intValue)
            return intValue;
        if (raw is long longValue)
            return unchecked((int)longValue);
        if (raw is uint uintValue)
            return unchecked((int)uintValue);

        return int.TryParse(raw.ToString(), out var parsedValue)
            ? parsedValue
            : null;
    }

    public static bool TryGetCollectionCount(object value, out int count)
    {
        count = 0;

        if (value is ICollection collection)
        {
            count = collection.Count;
            return true;
        }

        if (!TryGetPropertyValue(value, "Count", out var raw) || raw is null)
            return false;

        if (raw is int intValue)
        {
            count = intValue;
            return true;
        }

        if (raw is long longValue && longValue >= 0 && longValue <= int.MaxValue)
        {
            count = (int)longValue;
            return true;
        }

        if (raw is uint uintValue && uintValue <= int.MaxValue)
        {
            count = (int)uintValue;
            return true;
        }

        return false;
    }

    public static IEnumerable<IndexedItem> EnumerateCollectionItems(object collectionValue)
    {
        if (TryGetCollectionCount(collectionValue, out var count))
        {
            var itemProperty = collectionValue.GetType().GetProperty(
                "Item",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                returnType: null,
                types: [typeof(int)],
                modifiers: null);

            if (itemProperty is not null && itemProperty.CanRead)
            {
                var limit = Math.Min(count, 20_000);
                for (var index = 0; index < limit; index++)
                {
                    object? item = null;
                    try
                    {
                        item = itemProperty.GetValue(collectionValue, [index]);
                    }
                    catch
                    {
                    }

                    yield return new IndexedItem(index, item);
                }

                yield break;
            }
        }

        if (collectionValue is not IEnumerable enumerable)
            yield break;

        var enumerableIndex = 0;
        foreach (var item in enumerable)
        {
            yield return new IndexedItem(enumerableIndex, item);
            enumerableIndex++;
            if (enumerableIndex >= 20_000)
                break;
        }
    }

    public static IEnumerable<ObjectPathChild> EnumerateObjectChildrenWithPaths(
        object value,
        string path)
    {
        if (value is IEnumerable enumerable && value is not string)
        {
            var index = 0;
            foreach (var item in enumerable)
            {
                if (item is not null)
                    yield return new ObjectPathChild(item, $"{path}[{index}]");

                index++;
                if (index >= 20_000)
                    yield break;
            }

            yield break;
        }

        var type = value.GetType();

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!CanTraverse(property.PropertyType) ||
                !property.CanRead ||
                property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            object? childValue = null;
            try
            {
                childValue = property.GetValue(value);
            }
            catch
            {
            }

            if (childValue is null)
                continue;

            foreach (var child in ExpandChild(childValue, $"{path}.{property.Name}"))
                yield return child;
        }

        foreach (var field in type.GetFields(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!CanTraverse(field.FieldType))
                continue;

            object? childValue = null;
            try
            {
                childValue = field.GetValue(value);
            }
            catch
            {
            }

            if (childValue is null)
                continue;

            foreach (var child in ExpandChild(childValue, $"{path}.{field.Name}"))
                yield return child;
        }
    }

    public static bool TryGetMemberValue(object value, string memberName, out object? memberValue)
    {
        if (TryGetPropertyValue(value, memberName, out memberValue))
            return true;

        memberValue = null;
        try
        {
            var field = value.GetType().GetField(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is null)
                return false;

            memberValue = field.GetValue(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static IEnumerable<T> TraverseForType<T>(object root, int? maxDepth)
        where T : class
    {
        var visited = new HashSet<object>(ObjectReferenceEqualityComparer.Instance);
        var stack = new Stack<(object Value, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            var (value, depth) = stack.Pop();
            if (!visited.Add(value))
                continue;

            if (value is T match)
                yield return match;

            if (maxDepth.HasValue && depth >= maxDepth.Value)
                continue;

            foreach (var child in GetChildren(value))
                stack.Push((child, depth + 1));
        }
    }

    private static int? ExtractSampleTimeMs(object? sample)
    {
        if (sample is null)
            return null;

        if (TryGetMemberValue(sample, "Time", out var rawTime) && rawTime is not null)
        {
            var timeMs = GbxTime.ToMilliseconds(rawTime);
            if (timeMs.HasValue)
                return timeMs.Value;
        }

        var directTimeMs = GbxTime.ToMilliseconds(sample);
        if (directTimeMs.HasValue)
            return directTimeMs.Value;

        if (TryGetMemberValue(sample, "RaceTime", out var rawRaceTime) && rawRaceTime is not null)
        {
            var raceTimeMs = GbxTime.ToMilliseconds(rawRaceTime);
            if (raceTimeMs.HasValue)
                return raceTimeMs.Value;
        }

        return null;
    }

    private static bool TryGetPropertyValue(
        object value,
        string propertyName,
        out object? propertyValue)
    {
        propertyValue = null;
        try
        {
            var property = value.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public);
            if (property is null || !property.CanRead || property.GetIndexParameters().Length != 0)
                return false;

            propertyValue = property.GetValue(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<object> GetChildren(object value)
    {
        if (value is IEnumerable enumerable && value is not string)
        {
            var count = 0;
            foreach (var item in enumerable)
            {
                if (item is null)
                    continue;

                yield return item;
                if (++count > 20_000)
                    yield break;
            }

            yield break;
        }

        var type = value.GetType();
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!CanTraverse(property.PropertyType) ||
                !property.CanRead ||
                property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            object? childValue = null;
            try
            {
                childValue = property.GetValue(value);
            }
            catch
            {
            }

            if (childValue is not null)
                yield return childValue;
        }
    }

    private static bool CanTraverse(Type type) =>
        type != typeof(string) &&
        !type.IsPrimitive &&
        !type.IsEnum &&
        !type.IsValueType;

    private static IEnumerable<ObjectPathChild> ExpandChild(object value, string path)
    {
        if (value is not IEnumerable enumerable || value is string)
        {
            yield return new ObjectPathChild(value, path);
            yield break;
        }

        var index = 0;
        foreach (var item in enumerable)
        {
            if (item is not null)
                yield return new ObjectPathChild(item, $"{path}[{index}]");

            index++;
            if (index >= 20_000)
                break;
        }
    }
}

internal readonly record struct SampleCollectionSummary(
    int Count,
    int? LastIndexWithTime,
    int? LastTimeMs);

internal readonly record struct IndexedItem(int Index, object? Value);

internal readonly record struct ObjectPathChild(object Obj, string Path);
