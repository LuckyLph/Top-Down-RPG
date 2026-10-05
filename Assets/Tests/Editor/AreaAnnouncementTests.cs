using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;

/// <summary>
/// Tests for the <see cref="AreaAnnouncement"/> network payload.
/// </summary>
public class AreaAnnouncementTests
{
    /// <summary>
    /// Writing an announcement to a buffer and reading it back preserves the scene path, spawn id, epoch and
    /// new-session flag.
    /// </summary>
    [Test]
    public void WriteThenRead_RoundTripsEveryField()
    {
        AreaAnnouncement original = new("Assets/Scenes/Areas/Area_Clearing.unity", "start", 7, true);

        using FastBufferWriter writer = new(1024, Allocator.Temp);
        original.Write(writer);
        using FastBufferReader reader = new(writer, Allocator.Temp);
        AreaAnnouncement copy = AreaAnnouncement.Read(reader);

        Assert.That(copy.ScenePath, Is.EqualTo(original.ScenePath));
        Assert.That(copy.SpawnId, Is.EqualTo(original.SpawnId));
        Assert.That(copy.Epoch, Is.EqualTo(7));
        Assert.That(copy.NewSession, Is.True);
    }

    /// <summary>
    /// A null scene path or spawn id is stored as an empty string.
    /// </summary>
    [Test]
    public void NullStrings_BecomeEmpty()
    {
        AreaAnnouncement announcement = new(null, null, 1, false);

        Assert.That(announcement.ScenePath, Is.Empty);
        Assert.That(announcement.SpawnId, Is.Empty);
    }
}
