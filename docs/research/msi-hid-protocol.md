# MSI HID controller research

## Target board

The SignalRGB plugin archive supplied for JTG-RGB contains a controller script at:

`Msi/185 Byte Motherboard Controller/MSI_185Byte_Controller_0x0DB0.js`

That script declares HID vendor ID `0x0DB0` and includes a profile for:

`MPG Z890 CARBON WIFI (MS-7E17)`

The profile declares three 5V ARGB headers and onboard zones including `JPIPE1` (IO Shield, 11 LEDs) and `JPIPE3` (Carbon M.2, 6 LEDs). This is evidence that the controller implementation models individual LEDs, but it does not by itself establish the connected HID product ID on a particular machine.

## Protocol families to support

- MSI 185-byte HID controller family, including the `0x0DB0` script.
- MSI 162-byte HID controller families, including the scripts named for product IDs `0xB926` and `0x7B85`.
- Protocol detection must be performed per connected controller; do not infer protocol solely from the motherboard marketing name.

## Initialization flow observed in the reference

The 185-byte script:
1. Reads motherboard and BIOS identity through its system-information interface.
2. Calls `checkProtocolVersion()` to choose old/new protocol paths.
3. For the old protocol, checks per-LED support and configures zones/header settings.
4. For the new protocol, reads device zones, creates zones, and sets device-zone configuration.
5. Uses separate packet builders for configuration and RGB frame data.

The script contains a prominent warning that malformed packets can disable the motherboard RGB controller. JTG-RGB must therefore validate VID/PID, protocol generation, packet length, and target board profile before enabling writes. Unknown devices and unverified packet variants must remain read-only.

## Implementation requirements

- Keep a clean-room C# implementation; do not bundle SignalRGB binaries or copy its proprietary application code.
- Separate HID discovery, controller-family identification, protocol initialization, zone/LED mapping, RGB frame generation, and packet transport.
- Add a diagnostic mode that reports the detected HID identity and selected profile without sending configuration or lighting packets.
- Do not send experimental initialization/configuration writes to the user's hardware until packet lengths and command semantics have been independently checked against the selected profile.
- Add a per-LED test pattern only after initialization has been validated; allow the user to stop it immediately.
- Keep OpenRGB as the preferred backend for devices it already supports, and use this backend only for verified MSI controllers.

## Status

Research/profile identification only. The existing MSI Mystic Light SDK adapter remains separate. Direct HID output is not yet implemented or claimed to work.
