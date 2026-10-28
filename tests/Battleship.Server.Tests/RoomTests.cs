using System.Reflection;
using Battleship.Server.Rooms;
using Xunit;

namespace Battleship.Server.Tests;

public sealed class RoomTests
{
    [Fact]
    public void Assign_FirstHost_Ok()
    {
        var room = TestData.Room();
        Assert.Equal(RoleAssignResult.Ok, room.Assign(TestData.Participant(RoomRole.Host)));
    }

    [Fact]
    public void Assign_SecondHost_HostSlotTaken()
    {
        var room = TestData.Room();
        room.Assign(TestData.Participant(RoomRole.Host));
        Assert.Equal(RoleAssignResult.HostSlotTaken, room.Assign(TestData.Participant(RoomRole.Host)));
    }

    [Fact]
    public void Assign_ClientBeforeHost_HostNotConnected()
    {
        var room = TestData.Room();
        Assert.Equal(RoleAssignResult.HostNotConnected, room.Assign(TestData.Participant(RoomRole.Client)));
    }

    [Fact]
    public void Assign_ClientAfterHost_Ok()
    {
        var room = TestData.Room();
        room.Assign(TestData.Participant(RoomRole.Host));
        Assert.Equal(RoleAssignResult.Ok, room.Assign(TestData.Participant(RoomRole.Client)));
    }

    [Fact]
    public void Assign_SecondClient_ClientSlotTaken()
    {
        var room = TestData.Room();
        room.Assign(TestData.Participant(RoomRole.Host));
        room.Assign(TestData.Participant(RoomRole.Client));
        Assert.Equal(RoleAssignResult.ClientSlotTaken, room.Assign(TestData.Participant(RoomRole.Client)));
    }

    [Fact]
    public void Assign_ClosedRoom_RoomClosed()
    {
        var room = TestData.Room();
        room.TryMarkClosed();
        Assert.Equal(RoleAssignResult.RoomClosed, room.Assign(TestData.Participant(RoomRole.Host)));
    }

    [Fact]
    public void Assign_Success_UpdatesLastActivity()
    {
        var created = DateTimeOffset.UtcNow.AddMinutes(-5);
        var room = TestData.Room(created);
        var before = room.LastActivity;
        room.Assign(TestData.Participant(RoomRole.Host));
        Assert.True(room.LastActivity > before);
    }

    [Fact]
    public void Assign_HostAfterHostLeft_RejectedNoReconnection()
    {
        var room = TestData.Room();
        var host = TestData.Participant(RoomRole.Host);
        room.Assign(host);
        room.Remove(host);
        Assert.Equal(RoleAssignResult.HostSlotTaken, room.Assign(TestData.Participant(RoomRole.Host)));
    }

    [Fact]
    public void Assign_ConcurrentHosts_ExactlyOneWins()
    {
        var room = TestData.Room();
        var results = new RoleAssignResult[8];
        Parallel.For(0, results.Length, i => results[i] = room.Assign(TestData.Participant(RoomRole.Host)));
        Assert.Equal(1, results.Count(result => result == RoleAssignResult.Ok));
        Assert.Equal(results.Length - 1, results.Count(result => result == RoleAssignResult.HostSlotTaken));
    }

    [Fact]
    public void Assign_Rejected_LeavesRoomUnchanged()
    {
        var room = TestData.Room();
        var first = TestData.Participant(RoomRole.Host);
        room.Assign(first);
        room.Assign(TestData.Participant(RoomRole.Host));
        Assert.Equal(1, room.ParticipantCount);
        Assert.Same(first, room.Host);
    }

    [Fact]
    public void PeerOf_Host_ReturnsClient()
    {
        var room = TestData.Room();
        var host = TestData.Participant(RoomRole.Host);
        var client = TestData.Participant(RoomRole.Client);
        room.Assign(host);
        room.Assign(client);
        Assert.Same(client, room.PeerOf(host));
    }

    [Fact]
    public void PeerOf_Client_ReturnsHost()
    {
        var room = TestData.Room();
        var host = TestData.Participant(RoomRole.Host);
        var client = TestData.Participant(RoomRole.Client);
        room.Assign(host);
        room.Assign(client);
        Assert.Same(host, room.PeerOf(client));
    }

    [Fact]
    public void PeerOf_Stranger_ReturnsNull()
    {
        var room = TestData.Room();
        room.Assign(TestData.Participant(RoomRole.Host));
        Assert.Null(room.PeerOf(TestData.Participant(RoomRole.Client)));
    }

    [Fact]
    public void Remove_Host_ReturnsRemainingPeer()
    {
        var room = TestData.Room();
        var host = TestData.Participant(RoomRole.Host);
        var client = TestData.Participant(RoomRole.Client);
        room.Assign(host);
        room.Assign(client);
        var result = room.Remove(host);
        Assert.True(result.Removed);
        Assert.Same(client, result.Peer);
        Assert.Null(room.Host);
    }

    [Fact]
    public void Remove_Stranger_ReturnsFalse()
    {
        var room = TestData.Room();
        var result = room.Remove(TestData.Participant(RoomRole.Host));
        Assert.False(result.Removed);
        Assert.Null(result.Peer);
    }

    [Fact]
    public void Remove_Twice_SecondReturnsFalse()
    {
        var room = TestData.Room();
        var host = TestData.Participant(RoomRole.Host);
        room.Assign(host);
        Assert.True(room.Remove(host).Removed);
        Assert.False(room.Remove(host).Removed);
    }

    [Fact]
    public void ParticipantCount_ReflectsSlots()
    {
        var room = TestData.Room();
        Assert.Equal(0, room.ParticipantCount);
        room.Assign(TestData.Participant(RoomRole.Host));
        Assert.Equal(1, room.ParticipantCount);
        room.Assign(TestData.Participant(RoomRole.Client));
        Assert.Equal(2, room.ParticipantCount);
    }

    [Fact]
    public void Touch_UpdatesLastActivity()
    {
        var room = TestData.Room(DateTimeOffset.UtcNow.AddMinutes(-1));
        var before = room.LastActivity;
        room.Touch();
        Assert.True(room.LastActivity > before);
    }

    [Fact]
    public void TryCloseIfEmpty_EmptyOpen_ClosesOnce()
    {
        var room = TestData.Room();
        Assert.True(room.TryCloseIfEmpty());
        Assert.False(room.TryCloseIfEmpty());
        Assert.True(room.IsClosed);
    }

    [Fact]
    public void TryCloseIfEmpty_Occupied_ReturnsFalse()
    {
        var room = TestData.Room();
        room.Assign(TestData.Participant(RoomRole.Host));
        Assert.False(room.TryCloseIfEmpty());
        Assert.False(room.IsClosed);
    }

    [Fact]
    public void TryMarkClosed_IsIdempotent()
    {
        var room = TestData.Room();
        Assert.True(room.TryMarkClosed());
        Assert.False(room.TryMarkClosed());
    }

    [Fact]
    public void IsExpired_EmptyPastIdleTimeout_True()
    {
        var room = TestData.Room(DateTimeOffset.UtcNow.AddSeconds(-10));
        Assert.True(room.IsExpired(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5), TimeSpan.FromHours(1)));
    }

    [Fact]
    public void IsExpired_OccupiedBeforeMaxLifetime_False()
    {
        var room = TestData.Room(DateTimeOffset.UtcNow);
        room.Assign(TestData.Participant(RoomRole.Host));
        Assert.False(room.IsExpired(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1), TimeSpan.FromHours(1)));
    }

    [Fact]
    public void IsExpired_OccupiedPastMaxLifetime_True()
    {
        var room = TestData.Room(DateTimeOffset.UtcNow.AddHours(-2));
        room.Assign(TestData.Participant(RoomRole.Host));
        Assert.True(room.IsExpired(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1), TimeSpan.FromHours(1)));
    }

    [Fact]
    public void IsExpired_Closed_False()
    {
        var room = TestData.Room(DateTimeOffset.UtcNow.AddHours(-2));
        room.TryMarkClosed();
        Assert.False(room.IsExpired(DateTimeOffset.UtcNow, TimeSpan.Zero, TimeSpan.Zero));
    }

    [Fact]
    public void SnapshotParticipants_ExcludesEmptySlots()
    {
        var room = TestData.Room();
        Assert.Empty(room.SnapshotParticipants());
        room.Assign(TestData.Participant(RoomRole.Host));
        Assert.Single(room.SnapshotParticipants());
    }

    [Fact]
    public void Room_HoldsNoMessageOrBoardState()
    {
        var fields = typeof(Room).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.DoesNotContain(fields, field => field.FieldType.Name.Contains("Board", StringComparison.Ordinal));
        Assert.DoesNotContain(fields, field => field.FieldType == typeof(Battleship.Shared.ProtocolMessage));
        Assert.DoesNotContain(typeof(Room).GetProperties(), property => property.PropertyType.Name.Contains("Board", StringComparison.Ordinal));
    }
}

public sealed class RoomRegistryTests
{
    [Fact]
    public void TryAdd_DuplicateCode_ReturnsFalse()
    {
        var registry = new RoomRegistry();
        Assert.True(registry.TryAdd(TestData.Room()));
        Assert.False(registry.TryAdd(TestData.Room()));
    }

    [Fact]
    public void Get_Missing_ReturnsNull()
    {
        Assert.Null(new RoomRegistry().Get("ABC234"));
    }

    [Fact]
    public void Get_IsCaseSensitive()
    {
        var registry = new RoomRegistry();
        registry.TryAdd(TestData.Room());
        Assert.NotNull(registry.Get("ABC234"));
        Assert.Null(registry.Get("abc234"));
    }

    [Fact]
    public void TryRemove_DifferentInstanceSameCode_ReturnsFalse()
    {
        var registry = new RoomRegistry();
        registry.TryAdd(TestData.Room());
        Assert.False(registry.TryRemove(TestData.Room()));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void TryRemove_SameInstance_Removes()
    {
        var registry = new RoomRegistry();
        var room = TestData.Room();
        registry.TryAdd(room);
        Assert.True(registry.TryRemove(room));
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Snapshot_IsIndependentCopy()
    {
        var registry = new RoomRegistry();
        var room = TestData.Room();
        registry.TryAdd(room);
        var snapshot = registry.Snapshot();
        snapshot[0] = TestData.Room();
        Assert.Same(room, registry.Get(room.Code));
    }

    [Fact]
    public void Count_ReflectsRooms()
    {
        var registry = new RoomRegistry();
        Assert.Equal(0, registry.Count);
        registry.TryAdd(TestData.Room());
        Assert.Equal(1, registry.Count);
    }
}
