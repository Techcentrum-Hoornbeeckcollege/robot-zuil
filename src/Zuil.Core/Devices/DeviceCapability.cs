namespace Zuil.Core.Devices;

/// <summary>
/// One thing the ESP32 can be asked to do, described as data: the command byte,
/// the values it carries, and who is allowed to send it.
///
/// The firmware is the authority on what actually exists. Declaring a capability
/// here that the firmware does not implement earns a NACK, not a crash — but it
/// still puts a control on the admin screen that does nothing, so a new entry
/// and a new firmware handler belong in the same change.
/// </summary>
public sealed record DeviceCapability
{
    /// <summary>Stable identifier the API takes, e.g. "showDirection".</summary>
    public required string Key { get; init; }

    /// <summary>The Dutch label on the button.</summary>
    public required string Label { get; init; }

    public required DeviceCommand Command { get; init; }

    public IReadOnlyList<DeviceParameter> Parameters { get; init; } = [];

    /// <summary>
    /// Whether the host should read a reply frame back. Everything the firmware
    /// implements today answers, but a future fire-and-forget command (a
    /// streaming animation frame, say) would not.
    /// </summary>
    public bool ExpectsReply { get; init; } = true;

    public string? Hint { get; init; }

    /// <summary>
    /// False for commands only the kiosk's own logic and the admin panel may
    /// send. Nothing on the visitor-facing API can reach a capability that is
    /// not marked here, whatever it posts.
    /// </summary>
    public bool AllowedFromKiosk { get; init; }

    /// <summary>Payload bytes a fully populated frame of this command occupies.</summary>
    public int PayloadWidth => Parameters.Sum(parameter => parameter.Width);

    /// <summary>
    /// Validates the declaration. Run over the whole catalogue by a test, so an
    /// over-long command is a build failure rather than a truncated frame.
    /// </summary>
    public string? DeclarationError()
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            return "a capability needs a key";
        }

        if (Parameters.Select(parameter => parameter.Key).Distinct(StringComparer.Ordinal).Count()
            != Parameters.Count)
        {
            return $"{Key}: two parameters share a key";
        }

        foreach (var parameter in Parameters)
        {
            if (parameter.DeclarationError() is { } problem)
            {
                return $"{Key}: {problem}";
            }
        }

        return PayloadWidth > DeviceProtocol.MaxPayload
            ? $"{Key}: payload of {PayloadWidth} bytes exceeds the "
                + $"{DeviceProtocol.MaxPayload} the frame allows"
            : null;
    }

    /// <summary>
    /// Turns submitted values into a frame-ready instruction, or into the list of
    /// per-parameter messages the GUI shows beside the offending controls.
    ///
    /// A missing value falls back to the declared default rather than failing:
    /// that is what lets the GUI send a command whose new parameter it has not
    /// been taught about yet.
    /// </summary>
    public bool TryBuild(
        IReadOnlyDictionary<string, long> values,
        out DeviceInstruction instruction,
        out IReadOnlyList<DeviceParameterError> errors)
    {
        var problems = new List<DeviceParameterError>();
        var payload = new byte[PayloadWidth];
        var offset = 0;

        foreach (var parameter in Parameters)
        {
            var value = values.TryGetValue(parameter.Key, out var submitted)
                ? submitted
                : parameter.Default;

            if (parameter.Validate(value) is { } problem)
            {
                problems.Add(new DeviceParameterError(parameter.Key, problem));
            }
            else
            {
                parameter.Encode(value, payload.AsSpan(offset, parameter.Width));
            }

            offset += parameter.Width;
        }

        errors = problems;

        if (problems.Count > 0)
        {
            instruction = null!;
            return false;
        }

        instruction = new DeviceInstruction
        {
            Capability = this,
            Payload = payload,
        };

        return true;
    }

    /// <summary>The instruction for a command that carries no values.</summary>
    public DeviceInstruction Build()
    {
        var none = new Dictionary<string, long>(StringComparer.Ordinal);

        if (!TryBuild(none, out var instruction, out var errors))
        {
            // Only reachable if a declared default is itself invalid, which the
            // catalogue test rules out.
            throw new InvalidOperationException(
                $"Command {Key} cannot be built from its defaults: {errors[0].Message}");
        }

        return instruction;
    }
}

/// <summary>A rejected value, keyed by the parameter it belongs to.</summary>
public sealed record DeviceParameterError(string Parameter, string Message);
