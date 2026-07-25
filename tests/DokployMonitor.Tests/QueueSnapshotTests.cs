using DokployMonitor.Core.Queueing;

namespace DokployMonitor.Tests;

public class QueueSnapshotTests
{
    [Fact]
    public void Waiting_positions_are_numbered_per_partition_in_fifo_order()
    {
        // Dokploy kuyrugu isleri sunucuya (partition) gore boler; her partition
        // kendi icinde FIFO calisir, dolayisiyla sira numarasi da partition bazinda olmali.
        var snapshot = new QueueSnapshot
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Jobs =
            [
                Job("a", "waiting", minutesAgo: 3, serverId: null),
                Job("b", "waiting", minutesAgo: 5, serverId: null),
                Job("c", "waiting", minutesAgo: 1, serverId: "srv-2"),
                Job("d", "active", minutesAgo: 10, serverId: null),
            ],
        };

        var positions = snapshot.WaitingPositions();

        // Yerel partition: b (5 dk once) once eklendi, a sonra.
        Assert.Equal(1, positions["b"]);
        Assert.Equal(2, positions["a"]);

        // Farkli sunucu kendi sirasina sahip.
        Assert.Equal(1, positions["c"]);

        // Calisan isler siraya dahil degil.
        Assert.False(positions.ContainsKey("d"));
    }

    [Fact]
    public void Active_and_waiting_are_separated()
    {
        var snapshot = new QueueSnapshot
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Jobs = [Job("a", "waiting", 1, null), Job("b", "active", 2, null)],
        };

        Assert.Equal("a", Assert.Single(snapshot.Waiting).Id);
        Assert.Equal("b", Assert.Single(snapshot.Active).Id);
    }

    [Fact]
    public void Empty_snapshot_is_available_but_has_no_jobs()
    {
        var snapshot = new QueueSnapshot { Jobs = [], CapturedAt = DateTimeOffset.UtcNow };

        Assert.True(snapshot.IsAvailable);
        Assert.Empty(snapshot.Waiting);
    }

    private static QueueJob Job(string id, string state, int minutesAgo, string? serverId) => new()
    {
        Id = id,
        State = state,
        ServerId = serverId,
        EnqueuedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo),
        ProcessedAt = state == "active" ? DateTimeOffset.UtcNow.AddMinutes(-minutesAgo + 1) : null,
    };
}
