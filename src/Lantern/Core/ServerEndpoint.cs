namespace Lantern.Core;

/// <summary>
/// A host the client can connect to: what the user sees in the list on the startup window
/// plus everything the transport needs to dial it.
/// </summary>
public sealed record class ServerEndpoint(string ServerName, string DeviceAddress, Guid ServiceId)
{
    /// <summary>What the startup list shows.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(ServerName) ? DeviceAddress : $"{ServerName} ({DeviceAddress})";

    public override string ToString() => DisplayName;
}
