using System;
using System.Collections.Generic;
using System.Linq;

public static class CollectionExtensions
{
    /// <summary>
    /// Checks if the values of a specific field differ among items in the collection.
    /// </summary>
    public static bool HasDifferentValues<T, TField>(this IEnumerable<T> collection, Func<T, TField> selector)
    {
        if (collection == null || !collection.Any())
            return false;

        var firstValue = selector(collection.First());

        return collection.Skip(1).Any(item => !EqualityComparer<TField>.Default.Equals(selector(item), firstValue));
    }
}
