using System.Globalization;

namespace Zuil.Core.Devices;

/// <summary>What kind of control the GUI should draw for a parameter.</summary>
public enum DeviceParameterKind
{
    /// <summary>A bounded whole number: a slider with a number box beside it.</summary>
    Number,

    /// <summary>On or off, sent as 1 or 0.</summary>
    Toggle,

    /// <summary>One of a fixed set of named values.</summary>
    Choice,
}

/// <summary>One option of a <see cref="DeviceParameterKind.Choice"/> parameter.</summary>
public sealed record DeviceChoice(long Value, string Label);

/// <summary>
/// One value a device command carries, described well enough that the GUI can
/// draw a control for it and the host can turn it into payload bytes — without
/// either side knowing what the value means.
///
/// This is the whole point of the catalogue: a new knob on the column (say a
/// height in millimetres) is a record added here, not a new endpoint, a new
/// form field and a new settings column.
///
/// Values are unsigned. A byte protocol has no sign convention, and inventing
/// one here would be a decision the firmware has to agree with; a quantity that
/// can go negative should be declared with an offset the firmware subtracts.
/// </summary>
public sealed record DeviceParameter
{
    /// <summary>Stable identifier used by the API. Not shown to anyone.</summary>
    public required string Key { get; init; }

    /// <summary>The Dutch label on the control.</summary>
    public required string Label { get; init; }

    public DeviceParameterKind Kind { get; init; } = DeviceParameterKind.Number;

    public long Minimum { get; init; }

    public long Maximum { get; init; } = byte.MaxValue;

    /// <summary>Granularity, counted from <see cref="Minimum"/>.</summary>
    public long Step { get; init; } = 1;

    public long Default { get; init; }

    /// <summary>Payload bytes this value occupies, big-endian.</summary>
    public int Width { get; init; } = 1;

    /// <summary>Unit shown beside the control; empty when the number speaks for itself.</summary>
    public string Unit { get; init; } = "";

    public string? Hint { get; init; }

    public IReadOnlyList<DeviceChoice> Choices { get; init; } = [];

    /// <summary>The largest value <see cref="Width"/> bytes can hold.</summary>
    public long Capacity => Width >= 8 ? long.MaxValue : (1L << (8 * Width)) - 1;

    /// <summary>
    /// Checks the declaration itself, rather than a value for it. A test runs
    /// this over the whole catalogue, so a mistake in a new command is a failing
    /// build and not a malformed frame on the bus.
    /// </summary>
    public string? DeclarationError()
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            return "a parameter needs a key";
        }

        if (Width is < 1 or > 4)
        {
            return $"{Key}: width must be 1 to 4 bytes";
        }

        if (Minimum < 0)
        {
            return $"{Key}: values are unsigned, so the minimum cannot be negative";
        }

        if (Maximum < Minimum)
        {
            return $"{Key}: maximum is below minimum";
        }

        if (Maximum > Capacity)
        {
            return $"{Key}: maximum {Maximum} does not fit in {Width} byte(s)";
        }

        if (Step < 1)
        {
            return $"{Key}: step must be at least 1";
        }

        if (Default < Minimum || Default > Maximum)
        {
            return $"{Key}: default {Default} is outside {Minimum}..{Maximum}";
        }

        if (Kind is DeviceParameterKind.Choice && Choices.Count == 0)
        {
            return $"{Key}: a choice parameter needs options";
        }

        foreach (var choice in Choices)
        {
            if (choice.Value < Minimum || choice.Value > Maximum)
            {
                return $"{Key}: option {choice.Value} is outside {Minimum}..{Maximum}";
            }
        }

        return null;
    }

    /// <summary>
    /// Validates one submitted value. Returns the Dutch message to put beside
    /// the control, or null when the value is acceptable.
    /// </summary>
    public string? Validate(long value)
    {
        if (Kind is DeviceParameterKind.Toggle)
        {
            return value is 0 or 1 ? null : "Moet 0 of 1 zijn.";
        }

        if (Kind is DeviceParameterKind.Choice)
        {
            return Choices.Any(choice => choice.Value == value)
                ? null
                : "Geen geldige keuze.";
        }

        if (value < Minimum || value > Maximum)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Moet tussen {0} en {1} liggen.",
                Minimum,
                Maximum);
        }

        if ((value - Minimum) % Step != 0)
        {
            return string.Format(
                CultureInfo.InvariantCulture, "Moet een veelvoud van {0} zijn.", Step);
        }

        return null;
    }

    /// <summary>Writes the value into <paramref name="destination"/>, big-endian.</summary>
    public void Encode(long value, Span<byte> destination)
    {
        for (var i = 0; i < Width; i++)
        {
            destination[i] = (byte)(value >> (8 * (Width - 1 - i)));
        }
    }
}
