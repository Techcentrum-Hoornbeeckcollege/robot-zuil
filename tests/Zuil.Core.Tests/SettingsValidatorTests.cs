using Zuil.Core.Models;
using Zuil.Core.Settings;

namespace Zuil.Core.Tests;

/// <summary>
/// These rules run on the admin PUT and on the first-run seed, so they are the
/// only thing standing between a typo on a touchscreen and a kiosk that sends
/// people to the wrong room.
/// </summary>
public class SettingsValidatorTests
{
    private static Room Room(string id, byte signalId, string? mailbox = null) => new()
    {
        Id = id,
        Mailbox = mailbox ?? $"{id}@example.com",
        DisplayName = id,
        Floor = "Begane grond",
        Directions = "Rechtdoor.",
        SignalId = signalId,
    };

    /// <summary>
    /// The invariant the whole device protocol rests on: two rooms sharing an
    /// indicator means the column points at the wrong place half the time.
    /// </summary>
    [Fact]
    public void Rejects_two_rooms_sharing_an_indicator()
    {
        var errors = SettingsValidator.ValidateRooms([Room("a", 1), Room("b", 1)]);

        Assert.Contains(errors, e => e.Field == "rooms[1].signalId");
    }

    [Fact]
    public void Rejects_a_duplicate_mailbox()
    {
        var errors = SettingsValidator.ValidateRooms(
            [Room("a", 1, "shared@example.com"), Room("b", 2, "shared@example.com")]);

        Assert.Contains(errors, e => e.Field == "rooms[1].mailbox");
    }

    [Fact]
    public void Rejects_a_duplicate_id()
    {
        var errors = SettingsValidator.ValidateRooms(
            [Room("a", 1), Room("a", 2, "other@example.com")]);

        Assert.Contains(errors, e => e.Field == "rooms[1].id");
    }

    /// <summary>Zero is the protocol's "no indicator", so a room carrying it lights nothing.</summary>
    [Fact]
    public void Rejects_indicator_zero()
    {
        var errors = SettingsValidator.ValidateRooms([Room("a", 0)]);

        Assert.Contains(errors, e => e.Field == "rooms[0].signalId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("missing@domain")]
    [InlineData("spaces in@example.com")]
    public void Rejects_a_mailbox_that_is_not_an_address(string mailbox)
    {
        var errors = SettingsValidator.ValidateRooms([Room("a", 1, mailbox)]);

        Assert.Contains(errors, e => e.Field == "rooms[0].mailbox");
    }

    [Fact]
    public void Accepts_a_well_formed_room_set()
    {
        var errors = SettingsValidator.ValidateRooms([Room("a", 1), Room("b", 2), Room("c", 3)]);

        Assert.Empty(errors);
    }

    /// <summary>
    /// An empty room list is not an error. A fresh install has to boot with no
    /// rooms so they can be added from the GUI, which is now the only way in.
    /// </summary>
    [Fact]
    public void Accepts_no_rooms_at_all()
    {
        Assert.Empty(SettingsValidator.ValidateRooms([]));
    }

    /// <summary>
    /// Blank credentials are allowed for the same reason: an unconfigured kiosk
    /// must start. Malformed ones are not.
    /// </summary>
    [Fact]
    public void Accepts_blank_graph_credentials()
    {
        var errors = SettingsValidator.Validate(new KioskSettings());

        Assert.DoesNotContain(errors, e => e.Field.StartsWith("graph.", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_tenant_id_that_is_not_a_guid()
    {
        var errors = Validate(graph: new GraphSettings { TenantId = "contoso" });

        Assert.Contains(errors, e => e.Field == "graph.tenantId");
    }

    [Fact]
    public void Rejects_a_graph_url_that_is_not_https()
    {
        var errors = Validate(graph: new GraphSettings { BaseUrl = "http://graph.example.com" });

        Assert.Contains(errors, e => e.Field == "graph.baseUrl");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void Rejects_an_interval_outside_the_range(int minutes)
    {
        var errors = Validate(sync: new SyncSettings
        {
            Interval = TimeSpan.FromMinutes(minutes),
            StaleAfter = TimeSpan.FromHours(2),
        });

        Assert.Contains(errors, e => e.Field == "sync.interval");
    }

    /// <summary>
    /// A staleness threshold inside the sync interval leaves the "data may be
    /// out of date" banner permanently lit, which trains people to ignore it.
    /// </summary>
    [Fact]
    public void Rejects_a_stale_threshold_tighter_than_the_interval()
    {
        var errors = Validate(sync: new SyncSettings
        {
            Interval = TimeSpan.FromMinutes(10),
            StaleAfter = TimeSpan.FromMinutes(5),
        });

        Assert.Contains(errors, e => e.Field == "sync.staleAfter");
    }

    [Theory]
    [InlineData(0x07)]
    [InlineData(0x78)]
    public void Rejects_a_reserved_i2c_address(int address)
    {
        var errors = Validate(device: new DeviceSettings { Address = address });

        Assert.Contains(errors, e => e.Field == "device.address");
    }

    [Fact]
    public void Accepts_a_disabled_attention_pin()
    {
        var errors = Validate(device: new DeviceSettings { AttentionPin = null });

        Assert.DoesNotContain(errors, e => e.Field == "device.attentionPin");
    }

    [Fact]
    public void Rejects_an_idle_timeout_long_enough_to_strand_a_name_on_screen()
    {
        var errors = Validate(privacy: new PrivacySettings
        {
            IdleTimeout = TimeSpan.FromHours(1),
        });

        Assert.Contains(errors, e => e.Field == "privacy.idleTimeout");
    }

    /// <summary>
    /// Pasting the UI's own mask back into the field would otherwise overwrite a
    /// working credential with bullet characters.
    /// </summary>
    [Fact]
    public void Rejects_a_secret_that_is_only_the_mask()
    {
        Assert.NotNull(SettingsValidator.ValidateSecret("••••••••"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_secret(string secret) =>
        Assert.NotNull(SettingsValidator.ValidateSecret(secret));

    [Fact]
    public void Accepts_a_real_looking_secret() =>
        Assert.Null(SettingsValidator.ValidateSecret("abc~1Zq.def-GHI_jkl234"));

    private static IReadOnlyList<FieldError> Validate(
        GraphSettings? graph = null,
        SyncSettings? sync = null,
        DeviceSettings? device = null,
        PrivacySettings? privacy = null) =>
        SettingsValidator.Validate(new KioskSettings
        {
            Graph = graph ?? new GraphSettings(),
            Sync = sync ?? new SyncSettings(),
            Device = device ?? new DeviceSettings(),
            Privacy = privacy ?? new PrivacySettings(),
        });
}
