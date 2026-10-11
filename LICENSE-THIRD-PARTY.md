# Third-party licensing notes

This file tracks distribution obligations for third-party components used by JTG-RGB.

## OpenRGB

OpenRGB is licensed under GNU General Public License version 2.0 (GPL-2.0). Before bundling a release, record the exact version and source commit, preserve copyright and license notices, include the license text, and satisfy the applicable corresponding-source requirements for the exact distribution model. Modifications to OpenRGB must be clearly identified.

Official project: https://github.com/CalcProgrammer1/OpenRGB

## PawnIO

PawnIO is a separate driver project. Review the license and redistribution terms for the exact release before including its installer or binary. Do not assume OpenRGB's license covers PawnIO.

Official project: https://github.com/namazso/PawnIO

## SignalRGB community effects

Effects must be reviewed individually. Do not redistribute effect files unless their licenses or explicit terms allow redistribution. Compatibility work should be based on documented behavior and independently implemented interfaces, not copied proprietary code.

## Release checklist

- [ ] Pin exact third-party versions and checksums.
- [ ] Include all required notices and license texts.
- [ ] Provide corresponding source or an appropriate written offer where required.
- [ ] Confirm PawnIO redistribution permissions before bundling.
- [ ] Keep third-party files separate from JTG-RGB application code.
- [ ] Document any modifications to bundled components.
