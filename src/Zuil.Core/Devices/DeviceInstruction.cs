namespace Zuil.Core.Devices;

/// <summary>
/// A validated command, ready to be framed and put on the bus. Only
/// <see cref="DeviceCapability.TryBuild"/> produces one, so a bridge can take it
/// at face value: the payload is the right length and every value was in range.
/// </summary>
public sealed record DeviceInstruction
{
    public required DeviceCapability Capability { get; init; }

    public required ReadOnlyMemory<byte> Payload { get; init; }

    public DeviceCommand Command => Capability.Command;

    public bool ExpectsReply => Capability.ExpectsReply;
}

/// <summary>What came back.</summary>
public sealed record DeviceReply
{
    public required byte Code { get; init; }

    public required ReadOnlyMemory<byte> Payload { get; init; }

    public DateTimeOffset ReceivedAt { get; init; }

    /// <summary>How long the write, the wait and the read took together.</summary>
    public TimeSpan RoundTrip { get; init; }

    /// <summary>How many transfers it took, so a flaky bus shows up on screen.</summary>
    public int Attempts { get; init; } = 1;

    public string Name => DeviceResponseNames.Describe(Code);

    public bool IsSuccess => DeviceResponseNames.IsSuccess(Code);
}
