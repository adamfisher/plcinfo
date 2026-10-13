# Third-party test fixtures

The files in this directory are real project exports from third-party open-source
repositories. They are used only as test inputs and are **unmodified** copies of the
files at the pinned commits below (verified by git blob hash). Each remains under
its original license; license texts are in `LICENSES/`.

| File | Source | Commit | License |
|---|---|---|---|
| `Rockwell/ACDTestsWithAOI.L5X` | [hutcheb/acd](https://github.com/hutcheb/acd) `resources/ACDTestsWithAOI.L5X` | `75b95955026ec58495b35b86cb091547ed2e2907` | Apache-2.0 (`LICENSES/Apache-2.0.txt`) |
| `Rockwell/ACDTestsWithAOI.ACD` | [hutcheb/acd](https://github.com/hutcheb/acd) `resources/ACDTestsWithAOI.ACD` (the Studio 5000 v34.01 project that `ACDTestsWithAOI.L5X` was exported from; 3.7 MB) | `75b95955026ec58495b35b86cb091547ed2e2907` | Apache-2.0 (`LICENSES/Apache-2.0.txt`) |
| `Rockwell/ExampleForCICD_L85E.L5X` | [RockwellAutomation/ra-logix-cicd](https://github.com/RockwellAutomation/ra-logix-cicd) `1-production-files/L5Xs/ExampleForCICD_L85E.L5X` | `de14d4ac87d5295b2380cce441ed581d1930947f` | MIT, Copyright (c) 2024 Rockwell Automation (`LICENSES/MIT-RockwellAutomation.txt`) |
| `PLCopen/OSC_MQTT.xml` | [kmpm/OSC-MQTT](https://github.com/kmpm/OSC-MQTT) `OSC_MQTT.xml` | `fca3e8b0ec116260d449b0a40085c35488a9080b` | Apache-2.0 (`LICENSES/Apache-2.0.txt`) |

Neither Apache-2.0 source repository ships a `NOTICE` file at the pinned commit.

## Ported code

`PlcInfo/Parsing/RockwellAcdContainer.cs`, `RockwellAcdDatabase.cs` and `RockwellAcdParser.cs`
contain a C# port of parts of [hutcheb/acd](https://github.com/hutcheb/acd) (ACD container
layout, Comps/SbRegion record layouts, component attribute offsets and the CIP identity to
catalog number table), licensed under the Apache License, Version 2.0
(`LICENSES/Apache-2.0.txt`). Each file carries a notice saying so. hutcheb/acd has no
`NOTICE` file.

When adding a fixture: use only permissively licensed sources (MIT, BSD, Apache-2.0,
0BSD, CC0), pin the commit, record it here, and add the license text if it is new.
GPL or unlicensed files must not be committed.
