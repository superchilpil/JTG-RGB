# OpenRGB backend staging

The application looks for the optional backend executable at:

`vendor/OpenRGB/OpenRGB.exe`

When this file is present in the published app directory, JTG-RGB can attempt to launch it with `--server`. This directory intentionally does not contain an executable yet.

## Intended compatibility target

The first SDK client targets OpenRGB SDK protocol version 5, matching the Windows x64 PawnIO build offered with OpenRGB 1.0 release-candidate 2. Test this exact combination against supported hardware before pinning it in CI or a release.

Official sources:

- OpenRGB releases: https://openrgb.org/releases.html
- OpenRGB SDK protocol documentation: https://github.com/CalcProgrammer1/OpenRGB/blob/master/Documentation/OpenRGBSDK.md
- PawnIO project: https://pawnio.eu

## Distribution requirements

OpenRGB is GPL-2.0. Before redistributing its binary, keep the exact source revision and build/configuration information, provide the required license notices and corresponding source-code access, and review all bundled dependencies. Do not assume that linking to the upstream homepage alone satisfies source obligations.

PawnIO is a separate Windows driver. It is not included or installed by JTG-RGB. Review its current license and redistribution requirements independently before considering bundling it. Driver installation and elevation must remain explicit user actions.
