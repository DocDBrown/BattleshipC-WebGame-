using System.Collections.Concurrent;

namespace Battleship.Server.Rooms;

public sealed class RoomRegistry
{
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.Ordinal);

    public int Count => _rooms.Count;

    public bool TryAdd(Room room) => _rooms.TryAdd(room.Code, room);

    public Room? Get(string code) => _rooms.TryGetValue(code, out var room) ? room : null;

    public Room[] Snapshot() => _rooms.Values.ToArray();

    public bool TryRemove(Room room) =>
        ((ICollection<KeyValuePair<string, Room>>)_rooms)
        .Remove(new KeyValuePair<string, Room>(room.Code, room));
}
