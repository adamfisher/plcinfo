# Third-party notices

plcinfo is MIT-licensed (see `LICENSE`). It includes code ported from the following
project, which remains under its own license.

## hutcheb/acd

- Source: https://github.com/hutcheb/acd (commit `3a2aa87314012e095a81881da321da6af046684f`)
- License: Apache License, Version 2.0. Full text: `PlcInfo.Tests/Fixtures/LICENSES/Apache-2.0.txt`.
- Copyright the hutcheb/acd contributors. The project has no `NOTICE` file.
- Used in: `PlcInfo/Parsing/RockwellAcdContainer.cs`, `PlcInfo/Parsing/RockwellAcdDatabase.cs`,
  `PlcInfo/Parsing/RockwellAcdParser.cs`. These files are an independent C# port of the
  reverse-engineered ACD container and record layouts, component attribute offsets and the
  CIP identity to catalog number table. Each file carries a notice that it was ported.

Test fixtures and their licenses are listed in `PlcInfo.Tests/Fixtures/THIRD_PARTY_NOTICES.md`.
