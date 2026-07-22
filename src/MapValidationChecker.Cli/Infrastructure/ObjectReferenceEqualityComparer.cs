using System.Runtime.CompilerServices;

namespace MapValidationChecker.Cli.Infrastructure;

internal sealed class ObjectReferenceEqualityComparer : IEqualityComparer<object>
{
    public static ObjectReferenceEqualityComparer Instance { get; } = new();

    private ObjectReferenceEqualityComparer()
    {
    }

    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

    public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
}
