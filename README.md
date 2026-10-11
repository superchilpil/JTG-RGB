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

## Status

The repository is at the project setup stage. The implementation and supported hardware list will be documented as they are tested.

## License

The project license will be finalized once the integration boundaries and third-party distribution model are established. See the notices accompanying each third-party component.
