namespace Zuil.Core.Devices;

/// <summary>
/// Everything the host knows how to ask the ESP32 for.
///
/// This is the single list the rest of the kiosk reads from: the i2c bridge
/// frames from it, the rate limiter allowlists from it, the admin API publishes
/// it, and the admin screen draws a control per declared parameter. Adding a
/// command is therefore one entry here plus a firmware handler — no endpoint, no
/// form, no settings column, no migration.
///
/// Worked example, for when the column gains a motor. Say the firmware learns
/// command 0x05 to move the sign to a height in millimetres:
///
/// <code>
/// public static readonly DeviceCapability SetHeight = new()
/// {
///     Key = "setHeight",
///     Label = "Hoogte",
///     Command = (DeviceCommand)0x05,
///     Hint = "Zet de zuil op een hoogte. De firmware begrenst dit ook zelf.",
///     Parameters =
///     [
///         new DeviceParameter
///         {
///             Key = "height",
///             Label = "Hoogte",
///             Minimum = 400,
///             Maximum = 1600,
///             Step = 10,
///             Default = 1200,
///             Width = 2,          // two bytes, big-endian: 1600 will not fit in one
///             Unit = "mm",
///         },
///     ],
/// };
/// </code>
///
/// Add it to <see cref="All"/> and the slider appears under Instellingen →
/// Apparaat with the right range, step and unit. Nothing else changes.
/// </summary>
public static class DeviceCatalogue
{
    public static readonly DeviceCapability Ping = new()
    {
        Key = "ping",
        Label = "Ping",
        Command = DeviceCommand.Ping,
        Hint = "Controleert of de ESP32 antwoordt en of de framing klopt.",
        AllowedFromKiosk = true,
    };

    public static readonly DeviceCapability ShowDirection = new()
    {
        Key = "showDirection",
        Label = "Indicator aan",
        Command = DeviceCommand.ShowDirection,
        Hint = "Laat één indicator branden. 0 betekent: geen enkele.",
        AllowedFromKiosk = true,
        Parameters =
        [
            new DeviceParameter
            {
                Key = "signalId",
                Label = "Indicator",
                // The firmware knows nothing about rooms, only ids, which is why
                // re-mapping a room to another indicator is a settings change
                // here and never a reflash.
                Minimum = 0,
                Maximum = byte.MaxValue,
                Unit = "indicatornummer",
            },
        ],
    };

    public static readonly DeviceCapability Clear = new()
    {
        Key = "clear",
        Label = "Alles uit",
        Command = DeviceCommand.Clear,
        Hint = "Zet de zuil terug in rusttoestand.",
        AllowedFromKiosk = true,
    };

    public static readonly DeviceCapability ReadEvent = new()
    {
        Key = "readEvent",
        Label = "Melding ophalen",
        Command = DeviceCommand.ReadEvent,
        Hint = "Haalt op wat de ESP32 te melden heeft. Gebeurt normaal automatisch "
            + "als de attention-lijn omlaag gaat.",

        // Not AllowedFromKiosk: a visitor polling this would race the attention
        // line and collect events the kiosk's own handler is waiting for.
    };

    /// <summary>Declaration order is the order the admin screen shows them in.</summary>
    public static readonly IReadOnlyList<DeviceCapability> All =
    [
        Ping,
        ShowDirection,
        Clear,
        ReadEvent,
    ];

    private static readonly Dictionary<string, DeviceCapability> ByKey =
        All.ToDictionary(capability => capability.Key, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<DeviceCommand> Commands =
        All.Select(capability => capability.Command).ToHashSet();

    public static DeviceCapability? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var capability) ? capability : null;

    /// <summary>
    /// Whether this command byte is one the catalogue declares. The bridge asks
    /// before every transfer, so a code assembled anywhere else never reaches
    /// the bus.
    /// </summary>
    public static bool IsDeclared(DeviceCommand command) => Commands.Contains(command);

    /// <summary>
    /// Every mistake in the declarations, for the test that guards this file.
    /// Empty means the catalogue is self-consistent and fits the wire format.
    /// </summary>
    public static IReadOnlyList<string> DeclarationErrors()
    {
        var problems = new List<string>();

        foreach (var capability in All)
        {
            if (capability.DeclarationError() is { } problem)
            {
                problems.Add(problem);
            }
        }

        var duplicateKeys = All
            .GroupBy(capability => capability.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);

        problems.AddRange(
            duplicateKeys.Select(group => $"two capabilities share the key {group.Key}"));

        var duplicateCommands = All
            .GroupBy(capability => capability.Command)
            .Where(group => group.Count() > 1);

        problems.AddRange(
            duplicateCommands.Select(
                group => $"two capabilities share command 0x{(byte)group.Key:X2}"));

        return problems;
    }
}
