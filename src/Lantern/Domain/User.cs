namespace Lantern.Domain;

/// <summary>
/// A connected person (Figure 7). <paramref name="ConnectionId"/> identifies the socket, so the
/// session tables never confuse two people even for a moment. It is not what a room reads, though: a
/// transcript and a member list say who spoke by nickname alone, which is why the room is told when a
/// second of a name joins rather than being left to work out afterwards which one was meant.
/// </summary>
public sealed class User : IEquatable<User>
{
    public User(string connectionId, string nickname)
    {
        ConnectionId = string.IsNullOrWhiteSpace(connectionId)
            ? throw new ArgumentException("Connection id is required.", nameof(connectionId))
            : connectionId.Trim();

        Nickname = ValidationRules.Normalize(nickname);
        if (Nickname.Length == 0)
        {
            throw new ArgumentException("Nickname is required.", nameof(nickname));
        }
    }

    /// <summary>Stable id of the connection, not of the person.</summary>
    public string ConnectionId { get; }

    public string Nickname { get; }

    public bool Equals(User? other)
        => other is not null && ConnectionId == other.ConnectionId && Nickname == other.Nickname;

    public override bool Equals(object? obj) => Equals(obj as User);

    public override int GetHashCode() => HashCode.Combine(ConnectionId, Nickname);

    public override string ToString() => Nickname;
}
