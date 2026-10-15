using Microsoft.Extensions.Logging.Abstractions;
using Zuil.Core.Abstractions;
using Zuil.Core.Devices;

namespace Zuil.Device.Tests;

/// <summary>
/// The fake is what the dev loop and the admin screen talk to on a laptop, so
/// it has to answer like the firmware does — otherwise a command looks fine in
/// development and NACKs on the column.
/// </summary>
public class FakeBridgeTests
{
    private static FakeBridge NewBridge() => new(NullLogger<FakeBridge>.Instance);

    [Fact]
    public async Task Answers_a_ping_with_pong()
    {
        var reply = await NewBridge()
            .SendAsync(DeviceCatalogue.Ping.Build(), CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Equal((byte)DeviceResponse.Pong, reply.Code);
        Assert.Equal("PONG", reply.Name);
        Assert.True(reply.IsSuccess);
    }

    [Fact]
    public async Task Acks_everything_else_and_echoes_the_payload()
    {
        DeviceCatalogue.ShowDirection.TryBuild(
            new Dictionary<string, long> { ["signalId"] = 4 }, out var instruction, out _);

        var reply = await NewBridge().SendAsync(instruction, CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Equal((byte)DeviceResponse.Ack, reply.Code);
        Assert.Equal(new byte[] { 4 }, reply.Payload.ToArray());
    }

    /// <summary>
    /// A command declared before its firmware exists still has to be sendable,
    /// or the catalogue could not be developed ahead of a reflash.
    /// </summary>
    [Fact]
    public async Task Answers_a_command_the_firmware_does_not_have_yet()
    {
        var setHeight = new DeviceCapability
        {
            Key = "setHeight",
            Label = "Hoogte",
            Command = (DeviceCommand)0x05,
            Parameters =
            [
                new DeviceParameter
                {
                    Key = "height",
                    Label = "Hoogte",
                    Minimum = 400,
                    Maximum = 1600,
                    Step = 10,
                    Default = 1200,
                    Width = 2,
                    Unit = "mm",
                },
            ],
        };

        var reply = await NewBridge()
            .SendAsync(setHeight.Build(), CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Equal(new byte[] { 0x04, 0xB0 }, reply.Payload.ToArray());
    }

    [Fact]
    public async Task Show_direction_goes_through_the_named_wrapper()
    {
        // The extension methods are what the kiosk's own code calls, so they are
        // worth one test that they still reach the bridge at all.
        var bridge = NewBridge();

        await bridge.ShowDirectionAsync(2, CancellationToken.None);
        await bridge.ClearAsync(CancellationToken.None);

        Assert.True(await bridge.PingAsync(CancellationToken.None));
    }
}
