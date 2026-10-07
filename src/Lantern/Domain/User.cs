namespace Lantern.Domain;

/// <summary>
/// A connected person (Figure 7). <paramref name="ConnectionId"/> identifies the socket so the
//  same nickname can appear twice without confusing the session tables.
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
