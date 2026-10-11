# JTG-RGB

JTG-RGB is an open-source Windows RGB lighting controller with an original interface inspired by the usability of SignalRGB and built around OpenRGB hardware support.

## Project goals

- Provide a polished, dark-themed desktop interface for managing RGB devices.
- Use OpenRGB as the hardware-control backend and bundle a compatible backend release when redistribution and source-notice obligations are satisfied.
- Use PawnIO through a compatible OpenRGB build for supported low-level hardware access.
- Support device discovery, per-device and per-zone controls, synchronized effects, profiles, and configurable device layouts.
- Prioritize non-audio lighting effects. Audio-reactive features are intentionally out of scope for the initial milestones.
- Investigate compatibility with community lighting effects and only redistribute effects whose licenses permit it.
- Work offline without requiring a cloud account.

## Initial technology direction

- C# / .NET 9 and WPF for the Windows desktop application.
- A separated hardware integration layer communicating with a managed OpenRGB backend.
- A separate effects engine and device-layout model so the interface does not depend on backend internals.
- Windows x64 builds, with portable and installer distributions planned.

The precise OpenRGB version, PawnIO dependency details, and packaging approach will be pinned after a compatibility and licensing review.

## Development milestones

1. **Application shell:** original JTG-RGB branding, navigation, theme, settings persistence, and build automation.
2. **Hardware integration:** backend lifecycle management, device enumeration, and basic color/brightness controls.
3. **Lighting engine:** built-in effects, synchronization, layouts, and profiles.
4. **Community effects:** investigate an import/runtime compatibility layer, with per-effect license checks.
5. **Packaging:** portable build, installer, backend notices/source compliance, and release workflows.

## Third-party components and licensing

OpenRGB is licensed under GPL-2.0. Redistribution must comply with the applicable license terms, including required notices and corresponding source-code obligations. Any modified OpenRGB components will be kept clearly identified.

PawnIO is a separate driver dependency; its own license and redistribution terms must be reviewed before bundling it. JTG-RGB will not silently install kernel drivers or elevate without user consent.

SignalRGB is an independent product and is not affiliated with JTG-RGB. Compatibility with its effect format is an exploratory goal, not a claim of complete compatibility. Third-party effects will not be bundled unless their terms permit redistribution.

## Current implementation status

- The WPF shell and Windows x64 build workflow are in place.
- The first OpenRGB SDK integration is implemented for the OpenRGB SDK protocol version 5: connect to localhost, enumerate controllers, request a hardware rescan, and send a selected static color to individual controllers or all detected controllers.
- The Devices page and Settings page expose connection and PawnIO-registration status.
- If a compatible backend is placed at `vendor/OpenRGB/OpenRGB.exe` beside the published app, JTG-RGB can attempt to launch it with `--server`. This repository does **not** currently include the OpenRGB binary or PawnIO driver.
- PawnIO status is only a registry/service-registration check. It does not prove the driver is loaded or that a specific motherboard/RAM device is supported.

- An MSI Mystic Light SDK adapter is available for MSI lighting devices. On startup, JTG-RGB checks the motherboard manufacturer through Windows BIOS registry information; when an MSI board is detected and the backend is not available, it offers to download MSI's SDK ZIP from MSI's official download host and installs only `MysticLight_SDK_x64.dll` under `%LOCALAPPDATA%\JTG-RGB\MSI`. The user can instead open MSI's page to install MSI Center or dismiss the prompt. MSI Center must also be installed with the Mystic Light feature enabled and running; downloading the SDK does not install MSI Center.
- The Settings and Devices pages include a **Download MSI SDK** action. The downloader limits archive size, checks for the expected x64 DLL, extracts only that DLL (not arbitrary ZIP paths), and saves the SDK path in `%LOCALAPPDATA%\JTG-RGB\msi-sdk-path.txt`. A locally supplied SDK can still be placed at `vendor/MSI/MysticLight_SDK_x64.dll` for packaging tests, and will be copied beside the published app. The binary is not checked into this repository by default. Some SDK operations may require administrator permissions; JTG-RGB will not silently elevate.
- When connected, the MSI SDK adapter enumerates device types and LED/zone counts and applies the selected static color across the devices reported by the SDK. Dynamic screen-sync effects and fine-grained per-zone selection are not yet implemented for this backend.

### Testing the current integration

1. Install a compatible Windows x64 OpenRGB build. The OpenRGB 1.0 release-candidate 2 Windows PawnIO build is the current intended compatibility target for the initial SDK-v5 integration.
2. Install PawnIO separately from its official source if your hardware requires it. Do not download or install kernel drivers from unofficial mirrors.
3. Start OpenRGB and enable its SDK server on the local default endpoint, `127.0.0.1:6742` (or run OpenRGB with `--server`).
4. Open JTG-RGB and use **Devices → Connect / refresh devices**. The app should list controllers returned by OpenRGB. Select a color on the Dashboard to send it to all detected controllers, or use **Apply selected color** on a device card.

Some motherboard and memory controllers require OpenRGB to run elevated. JTG-RGB does not silently elevate or install drivers. For the first test, run OpenRGB yourself with the permissions required by your hardware; JTG-RGB connects to its SDK server.

The bundled-backend path is a development hook only. Before shipping a bundled release, pin a tested OpenRGB build, include its applicable license notices and corresponding source offer/source distribution, and separately resolve PawnIO redistribution terms. See `vendor/OpenRGB/README.md`.

## License

The project license will be finalized once the integration boundaries and third-party distribution model are established. See the notices accompanying each third-party component.
