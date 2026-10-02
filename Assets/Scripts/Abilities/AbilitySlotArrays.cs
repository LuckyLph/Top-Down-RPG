/// <summary>
/// Keeps serialized ability lists at their fixed slot count.
/// </summary>
public static class AbilitySlotArrays
{
    /// <summary>
    /// Returns <paramref name="source"/> when it already has <paramref name="count"/> entries, otherwise a copy
    /// truncated or padded with empty slots.
    /// </summary>
    public static AbilityDefinition[] Resize(AbilityDefinition[] source, int count)
    {
        if (source != null && source.Length == count)
        {
            return source;
        }

        AbilityDefinition[] resized = new AbilityDefinition[count];
        if (source != null)
        {
            for (int i = 0; i < count && i < source.Length; i++)
            {
                resized[i] = source[i];
            }
        }

        return resized;
    }
}
