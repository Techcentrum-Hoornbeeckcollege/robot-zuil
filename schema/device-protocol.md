# Pi ↔ ESP32 protocol

Two implementations must agree on this document:

| Side | File |
|---|---|
| Host (C#) | [`src/Zuil.Device/Framing.cs`](../src/Zuil.Device/Framing.cs) |
| Firmware (C++) | [`firmware/esp32/include/protocol.h`](../firmware/esp32/include/protocol.h) |

Change one, change the other, in the same commit.

## Transport

i2c, Raspberry Pi as **master**, ESP32 as **slave** at address `0x42` on bus 1
(`/dev/i2c-1`), clocked at **100 kHz**.

Both parts run at 3.3V, so no level shifter — but they need a solid common
ground, and the bus wants short wires.

### Why this is the fragile part of the design

ESP32 i2c-*slave* support is historically the weak spot of that peripheral; it
cannot stretch the clock reliably while it thinks. Consequences baked into the
protocol:

- The host **writes, pauses ~5 ms, then reads**, rather than expecting the slave
  to hold the bus.
- The firmware's `onReceive()` only copies bytes and sets a flag. All real work
  happens in `loop()`, and the reply is staged for the *next* read.
- The host retries up to 3 times on a NACK, bus error or bad CRC.

If this proves unreliable on the bench, swap the host side for a UART
implementation of `IDeviceBridge`. Nothing above that interface knows which
transport is in use — that is the whole reason the interface exists.

### Attention line

An i2c slave cannot start a conversation. When the ESP32 has something to
report it pulls **GPIO 4 → Pi BCM 17** low; the host sees the falling edge and
answers with `READ_EVENT`. The firmware de-asserts the line once the event has
been collected.

Set the attention pin to -1 under **Instellingen → Apparaat** to disable this
and poll instead.

## Frame format

```
[len][cmd][payload 0..16][crc8]
```

| Field | Meaning |
|---|---|
| `len` | Number of bytes *after* `len` — i.e. `cmd` + payload + `crc8` |
| `cmd` | Command or response code (below) |
| `payload` | 0–16 bytes, command-specific |
| `crc8` | CRC-8/ATM, polynomial `0x07`, init `0x00`, over `[len][cmd][payload]` |

`len` exists because i2c has no message boundaries — it is what tells the
firmware a command is complete. `crc8` exists because i2c has no integrity
checking: electrical noise arrives as a perfectly valid-looking byte, and a
corrupted signal id would send a visitor to the wrong room.

## Commands (host → device)

| Code | Name | Payload | Expected response |
|---|---|---|---|
| `0x01` | `PING` | — | `PONG` |
| `0x02` | `SHOW_DIRECTION` | `[signalId]` | `ACK [signalId]` |
| `0x03` | `CLEAR` | — | `ACK` |
| `0x04` | `READ_EVENT` | — | an event frame, or `NO_EVENT` |

`signalId` comes from the room list under **Instellingen → Zalen**; `0` means
"no indicator". The firmware does not know about rooms at all — only ids — so
re-mapping a room to a different indicator is a setting on the host, not a
reflash.

## Responses (device → host)

| Code | Name | Meaning |
|---|---|---|
| `0x81` | `PONG` | Alive, framing works |
| `0x82` | `ACK` | Command applied |
| `0x83` | `NACK` | Bad CRC, unknown command, or nothing staged |
| `0x84` | `NO_EVENT` | Attention line asserted but nothing to report |
| `0x85` | `FAULT` | Hardware fault; payload is implementation-defined |

## The host-side catalogue

The frame format above is the contract with the firmware. On the host, what
commands exist is **data**, in
[`src/Zuil.Core/Devices/DeviceCatalogue.cs`](../src/Zuil.Core/Devices/DeviceCatalogue.cs).
Each entry is a `DeviceCapability`: a key, a Dutch label, the command byte, and
a `DeviceParameter` per value it carries — kind, range, step, unit, default, and
how many bytes it occupies.

Everything else reads that one list:

| Reader | What it does with it |
|---|---|
| `Esp32I2cBridge` | frames the command byte and the encoded payload |
| `CommandPolicy` | refuses any command the catalogue does not declare |
| `GET /api/admin/device/capabilities` | publishes it to the admin screen |
| `POST /api/admin/device/command` | validates submitted values against it |
| `DeviceCommandPanel.tsx` | draws a control per parameter |

`IDeviceBridge` has one method — `SendAsync(DeviceInstruction, ct)` — so none of
that fans out per command. `DeviceBridgeExtensions` gives the three the kiosk's
own code calls (`PingAsync`, `ShowDirectionAsync`, `ClearAsync`) names on top of
it.

### Adding a command

Say the column gains a motor and the firmware learns `0x05` to move the sign to
a height in millimetres. The host side is one record:

```csharp
public static readonly DeviceCapability SetHeight = new()
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
            Width = 2,       // 1600 does not fit in one byte
            Unit = "mm",
        },
    ],
};
```

Add it to `DeviceCatalogue.All`, add `0x05` to the command table above, and
implement the handler in the firmware. The slider then appears under
**Instellingen → Apparaat** with that range, step and unit, the API accepts it,
and the rate limiter allows it. There is no endpoint to write, no form field, no
settings column and no migration.

Three rules the declaration has to keep, all of them enforced by
`DeviceCatalogue.DeclarationErrors()` and therefore by a unit test rather than by
a frame on the bus:

- **Values are unsigned**, and a parameter's range must fit its `Width`. A byte
  protocol has no sign convention; a quantity that can go negative is declared
  with an offset the firmware subtracts.
- **Payloads are big-endian**, most significant byte first, in declaration
  order. The firmware reads them that way; reversed, a height is a motor moving
  somewhere else entirely.
- **A command's parameters must fit the payload**, 16 bytes. Declaring more
  fails the build instead of truncating on the wire.

`AllowedFromKiosk` decides whether the visitor-facing API may reach a command at
all. `READ_EVENT` is not marked, because a visitor polling for events would race
the attention-line handler for the events it is waiting for.

## Open: what the indicators physically are

`SHOW_DIRECTION` currently takes a single `signalId` and the firmware blinks the
onboard LED, because the physical build is not decided yet. Whether the column
drives an addressable LED strip, a bank of lit signs, or a moving arrow changes
the payload — a strip wants a colour and an animation, a stepper wants a target
position.

Both `applyDirection()` in the firmware and the `ShowDirection` payload spec here
are marked TODO for that reason.
