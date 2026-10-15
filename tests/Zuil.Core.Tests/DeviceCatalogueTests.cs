using Zuil.Core.Devices;

namespace Zuil.Core.Tests;

/// <summary>
/// The catalogue is what the i2c bridge frames from, what the rate limiter
/// allowlists from, and what the admin screen draws itself from. A mistake in a
/// declaration therefore reaches the bus, so these tests sit on the declaration
/// itself as much as on the code that reads it.
/// </summary>
public class DeviceCatalogueTests
{
    /// <summary>
    /// The guard on every future command: ranges that fit their byte width,
    /// defaults inside their range, no duplicate keys or command bytes, and a
    /// payload that fits the frame. Adding a command that breaks any of those
    /// fails the build instead of the hardware.
    /// </summary>
    [Fact]
    public void Every_declaration_is_consistent() =>
        Assert.Empty(DeviceCatalogue.DeclarationErrors());

    [Fact]
    public void Declares_the_commands_the_firmware_implements()
    {
        Assert.Equal(DeviceCommand.Ping, DeviceCatalogue.Ping.Command);
        Assert.Equal(DeviceCommand.ShowDirection, DeviceCatalogue.ShowDirection.Command);
        Assert.Equal(DeviceCommand.Clear, DeviceCatalogue.Clear.Command);
        Assert.Equal(DeviceCommand.ReadEvent, DeviceCatalogue.ReadEvent.Command);
    }

    [Theory]
    [InlineData("ping")]
    [InlineData("showDirection")]
    [InlineData("SHOWDIRECTION")]
    public void Finds_a_capability_by_key(string key) =>
        Assert.NotNull(DeviceCatalogue.Find(key));

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData(null)]
    public void Does_not_find_what_it_does_not_declare(string? key) =>
        Assert.Null(DeviceCatalogue.Find(key));

    /// <summary>
    /// The gate the bridge uses. A command byte assembled anywhere else — a
    /// reflashed firmware's new code, a typo in a cast — never reaches the bus.
    /// </summary>
    [Fact]
    public void Only_declared_commands_count_as_declared()
    {
        Assert.True(DeviceCatalogue.IsDeclared(DeviceCommand.ShowDirection));
        Assert.False(DeviceCatalogue.IsDeclared((DeviceCommand)0x66));
    }

    [Fact]
    public void Builds_a_payload_from_the_declared_values()
    {
        var built = DeviceCatalogue.ShowDirection.TryBuild(
            new Dictionary<string, long> { ["signalId"] = 7 },
            out var instruction,
            out var errors);

        Assert.True(built);
        Assert.Empty(errors);
        Assert.Equal(DeviceCommand.ShowDirection, instruction.Command);
        Assert.Equal(new byte[] { 7 }, instruction.Payload.ToArray());
    }

    [Fact]
    public void Falls_back_to_the_default_for_a_value_it_was_not_given()
    {
        var built = DeviceCatalogue.ShowDirection.TryBuild(
            new Dictionary<string, long>(),
            out var instruction,
            out _);

        Assert.True(built);
        Assert.Equal(new byte[] { 0 }, instruction.Payload.ToArray());
    }

    [Fact]
    public void A_command_without_parameters_has_an_empty_payload() =>
        Assert.Empty(DeviceCatalogue.Ping.Build().Payload.ToArray());

    [Fact]
    public void Rejects_a_value_outside_its_range()
    {
        var parameter = new DeviceParameter
        {
            Key = "height",
            Label = "Hoogte",
            Minimum = 400,
            Maximum = 1600,
            Default = 1200,
            Width = 2,
        };

        Assert.Null(parameter.Validate(400));
        Assert.Null(parameter.Validate(1600));
        Assert.NotNull(parameter.Validate(399));
        Assert.NotNull(parameter.Validate(1601));
    }

    [Fact]
    public void Rejects_a_value_off_the_step()
    {
        var parameter = new DeviceParameter
        {
            Key = "height",
            Label = "Hoogte",
            Minimum = 400,
            Maximum = 1600,
            Step = 10,
            Default = 1200,
            Width = 2,
        };

        Assert.Null(parameter.Validate(410));
        Assert.NotNull(parameter.Validate(405));
    }

    /// <summary>
    /// Big-endian, because that is what the firmware reads. A two-byte value
    /// written the other way round is a motor moving to the wrong place.
    /// </summary>
    [Fact]
    public void Encodes_a_wide_value_most_significant_byte_first()
    {
        var parameter = new DeviceParameter
        {
            Key = "height",
            Label = "Hoogte",
            Maximum = 65535,
            Width = 2,
        };

        var destination = new byte[2];
        parameter.Encode(1200, destination);

        Assert.Equal(new byte[] { 0x04, 0xB0 }, destination);
    }

    /// <summary>
    /// The example in the catalogue's own doc comment, built for real. If this
    /// stops working, the instructions for adding a command are wrong.
    /// </summary>
    [Fact]
    public void A_new_command_needs_only_a_declaration()
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

        Assert.Null(setHeight.DeclarationError());

        var built = setHeight.TryBuild(
            new Dictionary<string, long> { ["height"] = 1200 },
            out var instruction,
            out _);

        Assert.True(built);
        Assert.Equal(new byte[] { 0x04, 0xB0 }, instruction.Payload.ToArray());
    }

    [Fact]
    public void Refuses_a_declaration_that_would_overrun_the_frame()
    {
        var tooWide = new DeviceCapability
        {
            Key = "tooWide",
            Label = "Te breed",
            Command = (DeviceCommand)0x06,
            Parameters =
            [
                .. Enumerable.Range(0, 5).Select(index => new DeviceParameter
                {
                    Key = $"value{index}",
                    Label = $"Waarde {index}",
                    Maximum = 4294967295,
                    Width = 4,
                }),
            ],
        };

        var error = tooWide.DeclarationError();

        Assert.Equal(20, tooWide.PayloadWidth);
        Assert.NotNull(error);
        Assert.Contains("exceeds", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_range_that_does_not_fit_its_width()
    {
        var parameter = new DeviceParameter
        {
            Key = "height",
            Label = "Hoogte",
            Maximum = 300, // needs two bytes
            Width = 1,
        };

        var error = parameter.DeclarationError();

        Assert.NotNull(error);
        Assert.Contains("does not fit", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_every_invalid_value_at_once()
    {
        var capability = new DeviceCapability
        {
            Key = "two",
            Label = "Twee",
            Command = (DeviceCommand)0x07,
            Parameters =
            [
                new DeviceParameter { Key = "a", Label = "A", Maximum = 10 },
                new DeviceParameter { Key = "b", Label = "B", Maximum = 10 },
            ],
        };

        var built = capability.TryBuild(
            new Dictionary<string, long> { ["a"] = 99, ["b"] = 99 },
            out _,
            out var errors);

        Assert.False(built);
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, error => error.Parameter == "a");
        Assert.Contains(errors, error => error.Parameter == "b");
    }

    [Fact]
    public void A_toggle_takes_only_zero_or_one()
    {
        var parameter = new DeviceParameter
        {
            Key = "enabled",
            Label = "Aan",
            Kind = DeviceParameterKind.Toggle,
            Maximum = 1,
        };

        Assert.Null(parameter.Validate(0));
        Assert.Null(parameter.Validate(1));
        Assert.NotNull(parameter.Validate(2));
    }

    [Fact]
    public void A_choice_takes_only_its_options()
    {
        var parameter = new DeviceParameter
        {
            Key = "mode",
            Label = "Modus",
            Kind = DeviceParameterKind.Choice,
            Maximum = 2,
            Choices = [new DeviceChoice(0, "Uit"), new DeviceChoice(2, "Knipperen")],
        };

        Assert.Null(parameter.Validate(2));
        Assert.NotNull(parameter.Validate(1));
    }

    [Fact]
    public void Read_event_is_not_reachable_from_the_visitor_facing_api()
    {
        // A visitor polling for events would race the attention-line handler and
        // collect the events it is waiting for.
        Assert.False(DeviceCatalogue.ReadEvent.AllowedFromKiosk);
        Assert.True(DeviceCatalogue.ShowDirection.AllowedFromKiosk);
    }

    [Theory]
    [InlineData(0x81, "PONG", true)]
    [InlineData(0x82, "ACK", true)]
    [InlineData(0x83, "NACK", false)]
    [InlineData(0x85, "FAULT", false)]
    [InlineData(0x1F, "0x1F", false)]
    public void Names_a_reply_code(byte code, string name, bool success)
    {
        Assert.Equal(name, DeviceResponseNames.Describe(code));
        Assert.Equal(success, DeviceResponseNames.IsSuccess(code));
    }
}
