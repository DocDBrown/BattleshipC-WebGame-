using Battleship.Server.Realtime;

namespace Battleship.Server.Rooms;

public enum RoomRole
{
    Host,
    Client
}

public enum RoleAssignResult
{
    Ok,
    HostSlotTaken,
    ClientSlotTaken,
    HostNotConnected,
    RoomClosed
}

public sealed class Room
{
    private readonly object _sync = new();
    private DateTimeOffset _lastActivity;
    private bool _hostAssigned;

    public Room(string code, DateTimeOffset createdAt)
    {
        Code = code;
        CreatedAt = createdAt;
        _lastActivity = createdAt;
    }

    public string Code { get; }
    public DateTimeOffset CreatedAt { get; }
    public Participant? Host { get; private set; }
    public Participant? Client { get; private set; }
    public bool IsClosed { get; private set; }

    public int ParticipantCount
    {
        get
        {
            lock (_sync)
            {
                return CountCore;
            }
        }
    }

    public DateTimeOffset LastActivity
    {
        get
        {
            lock (_sync)
            {
                return _lastActivity;
            }
        }
    }

    public RoleAssignResult Assign(Participant participant)
    {
        lock (_sync)
        {
            if (IsClosed)
            {
                return RoleAssignResult.RoomClosed;
            }

            if (participant.Role == RoomRole.Host)
            {
                if (Host is not null || _hostAssigned)
                {
                    return RoleAssignResult.HostSlotTaken;
                }

                Host = participant;
                _hostAssigned = true;
            }
            else
            {
                if (Client is not null)
                {
                    return RoleAssignResult.ClientSlotTaken;
                }

                if (Host is null)
                {
                    return RoleAssignResult.HostNotConnected;
                }

                Client = participant;
            }

            _lastActivity = DateTimeOffset.UtcNow;
            return RoleAssignResult.Ok;
        }
    }

    public Participant? PeerOf(Participant participant)
    {
        lock (_sync)
        {
            if (Host is not null && Host.Id == participant.Id)
            {
                return Client;
            }

            if (Client is not null && Client.Id == participant.Id)
            {
                return Host;
            }

            return null;
        }
    }

    public (bool Removed, Participant? Peer) Remove(Participant participant)
    {
        lock (_sync)
        {
            var removed = false;
            if (Host is not null && Host.Id == participant.Id)
            {
                Host = null;
                removed = true;
            }
            else if (Client is not null && Client.Id == participant.Id)
            {
                Client = null;
                removed = true;
            }

            if (!removed)
            {
                return (false, null);
            }

            _lastActivity = DateTimeOffset.UtcNow;
            return (true, Host ?? Client);
        }
    }

    public void Touch()
    {
        lock (_sync)
        {
            _lastActivity = DateTimeOffset.UtcNow;
        }
    }

    public bool TryCloseIfEmpty()
    {
        lock (_sync)
        {
            if (IsClosed || CountCore != 0)
            {
                return false;
            }

            IsClosed = true;
            _lastActivity = DateTimeOffset.UtcNow;
            return true;
        }
    }

    public bool TryMarkClosed()
    {
        lock (_sync)
        {
            if (IsClosed)
            {
                return false;
            }

            IsClosed = true;
            _lastActivity = DateTimeOffset.UtcNow;
            return true;
        }
    }

    public bool IsExpired(DateTimeOffset now, TimeSpan idleTimeout, TimeSpan maxLifetime)
    {
        lock (_sync)
        {
            if (IsClosed)
            {
                return false;
            }

            if (CountCore == 0 && now - _lastActivity >= idleTimeout)
            {
                return true;
            }

            return now - CreatedAt >= maxLifetime;
        }
    }

    public Participant[] SnapshotParticipants()
    {
        lock (_sync)
        {
            return new[] { Host, Client }.Where(p => p is not null).Cast<Participant>().ToArray();
        }
    }

    private int CountCore => (Host is not null ? 1 : 0) + (Client is not null ? 1 : 0);
}
