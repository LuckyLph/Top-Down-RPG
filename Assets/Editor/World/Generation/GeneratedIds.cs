using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Derived persistent ids (Docs/WorldGeneration.md: Decisions: Persistent ids): computed from what they name rather than
/// drawn at random, in the format of <see cref="PersistentIds.NewId"/>, so a generated spawn point keeps its id through
/// re-seeding, retuning and composing again, and a mob killed before stays dead at its new place.
/// </summary>
public static class GeneratedIds
{
    /// <summary>
    /// The id of the <paramref name="ordinal"/>th thing of <paramref name="kind"/> (<c>mob</c>) in
    /// <paramref name="scope"/> (a region's key, or the wilds of a chunk) of the world <paramref name="worldId"/>: the
    /// first 32 hex digits, lower case, of the SHA-256 of the four joined by slashes.
    /// </summary>
    public static string For(string worldId, string scope, string kind, int ordinal)
    {
        byte[] text = Encoding.UTF8.GetBytes($"{worldId}/{scope}/{kind}/{ordinal}");
        byte[] hash;
        using (SHA256 sha = SHA256.Create())
        {
            hash = sha.ComputeHash(text);
        }

        StringBuilder id = new(32);
        for (int i = 0; i < 16; i++)
        {
            id.Append(hash[i].ToString("x2"));
        }

        return id.ToString();
    }
}
