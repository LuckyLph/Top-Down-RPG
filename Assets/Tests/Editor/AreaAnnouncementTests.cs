using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;

public class AreaAnnouncementTests
{
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

    [Test]
    public void NullStrings_BecomeEmpty()
    {
        AreaAnnouncement announcement = new(null, null, 1, false);

        Assert.That(announcement.ScenePath, Is.Empty);
        Assert.That(announcement.SpawnId, Is.Empty);
    }
}
