# PLCInfo

[![CI](https://github.com/adamfisher/plcinfo/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/adamfisher/plcinfo/actions/workflows/ci.yml)

A cross-platform command-line tool for analyzing and inspecting Programmable Logic Controller (PLC) project files from multiple vendors. Extract project information, analyze code metrics, compare versions, and export data in various formats.

## Overview

PLCInfo provides a unified interface to read and analyze PLC project files from different manufacturers without requiring the proprietary software. Perfect for version control integration, CI/CD pipelines, documentation generation, and automated code reviews.

### Supported PLC Platforms

- **Rockwell Automation**
  - `.L5X` (Logix 5000 Export files)
  - `.ACD` (RSLogix/Studio 5000 project files), **partial support**. Rockwell does not
    document the ACD format; plcinfo reads it using layouts reverse-engineered by
    [hutcheb/acd](https://github.com/hutcheb/acd) (Apache-2.0), with no Rockwell software
    needed. It reads the controller, tasks, programs, routines and their languages, ladder
    rung text, Add-On Instruction names and routines, user-defined types, module identities,
    and tags (name, data type, dimensions, description, external access). It does not read
    Structured Text, FBD or SFC bodies, comments on rungs, programs, routines or modules,
    tag values, alias targets, parameter usage, or module configuration. Tags with External
    Access "None" are not stored in the ACD's tag index and are left out. Results carry
    `ParseStatus: Partial` and a warning. For complete content, export to L5X from Studio 5000.
- **Siemens**
  - TIA Portal Openness exports (SimaticML `.xml`): a single exported block, PLC data type or
    tag table, or a whole export folder / Version Control Interface workspace (tested with
    V18, V20 and V21 exports). LAD/FBD networks are rendered as boolean power-flow
    text (e.g. `Coil(#Run) := (#Start OR #Run) AND NOT #Stop`), SCL is rebuilt from its tokens,
    STL as statements, and GRAPH as steps, transitions and connections.
  - Step 7 / TIA "Generate source from blocks" files: STL `.awl` and SCL `.scl`
  - Step 7 Classic project folders and `.s7p` archives (block list only)
  - TIA Portal project files (`.apXX`) and archives (`.zapXX`) are **not** readable: their format is
    proprietary and undocumented. plcinfo recognizes them and explains how to export SimaticML instead.
- **CODESYS and other PLCopen XML tools** (Beremiz, OpenPLC Editor, ctrlX PLC, TwinCAT PLC Control)
  - `.xml` PLCopen XML exports (TC6 v2.0/v2.01). In CODESYS use *Project > Export PLCopenXML...*;
    binary `.project` and `.library` files cannot be read and are rejected with that advice.
  - POUs with ST/IL source, LD/FBD/CFC networks and SFC step/transition summaries; DUTs
    (struct, union, enum, array, pointer, subrange, alias); tasks; GVLs; and the CODESYS
    extensions for methods, properties, interfaces, inheritance and the device tree.
  - Ladder networks are written as canonical text for diffs, e.g. `XIC(a) [XIC(b) , XIO(c)] OTE(d)`
    (contacts `XIC`/`XIO`/`XICP`/`XICN`, coils `OTE`/`OTN`/`OTL`/`OTU`); FBD as assignments,
    e.g. `out := ADD(IN1 := x, IN2 := 1)`.
- **Beckhoff TwinCAT 3**
  - `.tsproj` (system manager project): tasks, the I/O tree (EtherCAT devices and nested boxes) with
    PLC variable links, and every referenced PLC project (via `PrjFilePath` or an `_Config/PLC/*.xti` file)
  - `.plcproj` (PLC project): all listed `.TcPOU`, `.TcIO`, `.TcGVL`, `.TcDUT` and `.TcTTO` sources
  - a single `.TcPOU`, `.TcIO`, `.TcGVL`, `.TcDUT` or `.TcTTO` file
  - POUs keep their ST source per body, method, property accessor, action and transition; graphical
    bodies (SFC, LD, FBD, CFC) are recorded with their language but not rendered yet. Archives
    (`.tszip`, `.tnzip`) must be extracted first.

> **Note:** This project is still in development and needs testing with real-world PLC files from various manufacturers. If you encounter any issues or have example files to contribute, please open an issue or submit a pull request. I'm not an industrial automation engineer and don't have access to a comprehensive set of PLC project files. Community contributions for testing and validation would be greatly appreciated!

## Features

- **Logic diff** - Compare two versions down to individual rungs, Structured Text lines and networks, with instruction-level summaries such as `-OTE(Motor) +OTL(Motor)`.
- **Ladder rendering** - Self-contained HTML reports, and visual before/after diffs that draw rungs as ladder diagrams.
- **Git integration** - `textconv` makes `git diff`, `git log -p` and code review show logic changes instead of XML noise.
- **Cross-reference** - See where every tag is read and written, and find who sets an output.
- **Lint** - Find duplicate `OTE`s, missing `JSR` targets, unreachable routines, leftover `AFI`s, unused tags and more. Works as a CI gate.
- **One model for every vendor** - Rockwell, Siemens, CODESYS/PLCopen and Beckhoff projects all summarize, search, diff and export the same way.
- **Project inspection** - Summary, structure, tags (controller and program scope), I/O, data types, metrics and search.
- **Many output formats** - Text, JSON, YAML, Markdown, CSV (for Excel) and HTML.

## Installation

### Download a pre-built binary

Download the archive for your platform from the [latest release](https://github.com/adamfisher/plcinfo/releases/latest). The binaries are self-contained, so .NET doesn't need to be installed.

| Platform | Asset |
|---|---|
| Windows x64 | `plcinfo.exe`, or `plcinfo-<version>-win-x64.zip` |
| Windows ARM64 | `plcinfo-<version>-win-arm64.zip` |
| Linux x64 / ARM64 | `plcinfo-<version>-linux-x64.tar.gz` / `plcinfo-<version>-linux-arm64.tar.gz` |
| macOS Apple Silicon / Intel | `plcinfo-<version>-osx-arm64.tar.gz` / `plcinfo-<version>-osx-x64.tar.gz` |

Put the binary in a directory on your `PATH`, then run `plcinfo --version`. `SHA256SUMS.txt` on the release lists checksums for every asset.

On macOS the binary isn't notarized. If Gatekeeper blocks it, run `xattr -d com.apple.quarantine plcinfo` once.

### Install as a .NET tool

With the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), download `plcinfo.<version>.nupkg` from the release and run:

```bash
dotnet tool install --global plcinfo --add-source <folder containing the .nupkg>
```

### Build from source

```bash
git clone https://github.com/adamfisher/plcinfo.git
cd plcinfo
dotnet test                                   # build and run the test suite
dotnet run --project PlcInfo -- summary project.L5X
```

To produce a self-contained executable, use `./Build.ps1 -Runtime <rid>` (PowerShell 7 on any OS) or `dotnet publish PlcInfo -c Release -r <rid> --self-contained -p:PublishSingleFile=true`.

## Usage

```bash
plcinfo <command> [options] <file>
plcinfo --help              # list commands
plcinfo <command> --help    # a command's options (-h also works)
plcinfo --version
```

### Commands

#### `summary` - Display Project Summary

Get a high-level overview of the PLC project.

```bash
plcinfo summary project.L5X
plcinfo summary project.ACD --format json
```

**Output includes:**
- Vendor and platform information
- Controller name and firmware version
- Task, program, routine, and rung counts
- Tag counts by scope
- I/O module count

#### `structure` - Display Project Structure

Show the hierarchical organization of tasks, programs, and routines.

```bash
plcinfo structure project.L5X
plcinfo structure project.L5X --format md
```

#### `tags` - List and Filter Tags

Display project tags with various filtering options.

```bash
# List all tags
plcinfo tags project.L5X

# Filter by scope (controller, program)
plcinfo tags project.L5X --scope controller

# Filter by data type
plcinfo tags project.L5X --type DINT

# Show only I/O tags
plcinfo tags project.L5X --io-only

# Filter by name
plcinfo tags project.L5X --name-contains "Motor"

# Tag list for Excel (CSV with a UTF-8 BOM when written to a file)
plcinfo tags project.L5X --format csv --output tags.csv

# Combine filters and export as JSON
plcinfo tags project.L5X --scope controller --io-only --format json
```

#### `io` - Display I/O Configuration

Show all configured I/O modules and their properties.

```bash
plcinfo io project.L5X
plcinfo io project.L5X --format yaml
```

#### `metrics` - Calculate Code Metrics

Analyze code complexity and generate statistics.

```bash
plcinfo metrics project.L5X
plcinfo metrics project.L5X --format json
```

**Metrics include:**
- Lines of code
- Cyclomatic complexity
- Instruction counts and types
- Routine complexity scores
- Tag usage statistics

#### `search` - Search Project Content

Search for specific text within tags, comments, or instructions.

```bash
# Search all content
plcinfo search project.L5X "Conveyor"

# Search only tags
plcinfo search project.L5X "Motor" --kind tag

# Search only comments
plcinfo search project.L5X "TODO" --kind comment

# Search only instructions
plcinfo search project.L5X "TON" --kind instruction
```

#### `diff` - Compare Two Projects

Compare two versions of a project down to individual rungs and lines of code.

```bash
plcinfo diff project-v1.L5X project-v2.L5X
plcinfo diff old.L5X new.L5X --format md --output changes.md
```

Example output:

```text
  + Programs.MainProgram.Routines.MainRoutine.Rungs[1]
      Rung 1 added to 'MainRoutine'
      + XIC(DINT.5)[XIO(DINT.6) ,XIC(DINT.7) ]OTE(DINT.8);

  * Programs.MainProgram.Routines.MainRoutine.Rungs[2]
      Rung 2 changed in 'MainRoutine': -OTE(DINT.3) +OTL(DINT.3)
      - XIC(DINT.2)OTE(DINT.3);
      + XIC(DINT.2)OTL(DINT.3);

  * Programs.MainProgram.Routines.STRoutine.Source[L2]
      Routine 'STRoutine': line L2 changed
      - DINT := DINT AND ULINT; // Line 2 Comment
      + DINT := DINT OR ULINT; // Line 2 Comment
```

**Detects changes in:**
- **Ladder logic:** rungs are matched by content, so inserting a rung reports one addition instead of shifting every rung after it. Changed rungs show which instructions were removed and added. Whitespace-only differences are ignored, and rung comment changes are informational.
- **Structured Text / SCL / STL source:** line-level hunks with line numbers.
- **Networks** (Siemens LAD/FBD, PLCopen LD), by their canonical text.
- **Routines and blocks:** added, removed, language changes.
- **User-defined data types:** members added, removed, retyped or reordered.
- **Tags:** controller and program scope (data type, scope, usage, initial value; description changes are informational).
- **Programs, tasks, I/O modules and controller settings.**

Rungs with pending or test online edits are skipped, with a note.

For a **visual diff**, use `--format html`. Changed rungs are drawn before and after, side by side, with removed instructions in red and added ones in green:

```bash
plcinfo diff old.L5X new.L5X --format html -o changes.html
```

Exit code is `0` if identical, `1` if differences are found, and `2` if a file could not be read or parsed (useful for CI/CD).

#### `xref` - Cross-Reference

Show where tags are read and written: rungs, Structured Text lines and networks. Writes are classified per instruction: coils (`OTE`/`OTL`/`OTU`), destinations of `MOV`/`COP`/math/`CPT`, timer and counter structures, and `:=` in ST.

```bash
plcinfo xref project.L5X                 # every reference
plcinfo xref project.L5X Motor_Run       # one tag
plcinfo xref project.L5X 'Conv*' --writes   # who sets the conveyor outputs?
```

#### `lint` - Check for Common Problems

| Code | Severity | Rule |
|---|---|---|
| PLC001 | error | The same output is written by more than one `OTE`; only the last scanned one takes effect. |
| PLC002 | error | `JSR`/`FOR`/`SFR`/`SFP` references a routine that doesn't exist. |
| PLC003 | warning | Routine is never called (not main, fault, or a `JSR`/`FOR` target). |
| PLC004 | warning | `AFI` (Always False) disables a rung, usually left over from testing. |
| PLC005 | warning | Program-scoped tag is never used (program parameters are excluded). |
| PLC006 | info | Controller-scoped tag isn't used in logic (it may still be used by an HMI or another controller). |
| PLC007 | info | Routine is empty. |
| PLC008 | warning | Task is inhibited or program is disabled. |
| PLC009 | warning | Routine has pending or test online edits. |

```bash
plcinfo lint project.L5X
plcinfo lint project.L5X --fail-on warning --ignore PLC006,PLC007   # CI gate
```

Exits with `1` when there are findings at or above `--fail-on` (`error` by default; also `warning`, `info`, `never`).

#### `report` - HTML Report with Rendered Ladder Logic

Write a single, self-contained HTML page with no external files or scripts, so it can be emailed or attached to a ticket:

- project summary and lint findings
- tasks
- every program and routine, with **ladder rungs drawn as diagrams** (contacts, coils, boxes and branches)
- Structured Text source and Siemens/PLCopen networks
- tags, data types and I/O modules

```bash
plcinfo report project.L5X -o project.html
```

The page follows the viewer's light or dark theme. Ladder diagrams are drawn for Rockwell rungs and PLCopen LD networks. Siemens LAD/FBD networks appear as their power-flow text.

#### `export` - Export to Neutral Format

Convert project to a vendor-neutral JSON format for integration with other tools.

```bash
# Export JSON to stdout
plcinfo export project.L5X

# Export to a file, or as YAML
plcinfo export project.L5X --output project.json
plcinfo export project.L5X --format yaml

# Compact JSON (no indentation)
plcinfo export project.L5X --compact
```

### Options for every command

| Option | Description |
|---|---|
| `-f`, `--format FORMAT` | Output format: `text` (default), `json`, `yaml`, `md`/`markdown`, or `csv` for tabular commands (`tags`, `io`, `search`, `diff`, `xref`, `lint`). `export` defaults to `json`. |
| `-o`, `--output FILE` | Write the result to `FILE` instead of standard output. |
| `-q`, `--quiet` | Don't print warnings (e.g. partially decoded files) on standard error. |
| `--verbose` | Print extra diagnostics, including stack traces for errors. |
| `-h`, `--help` | Show help. |

Standard output carries only the command's result, so it can be piped or redirected safely. Warnings and errors go to standard error, prefixed with `plcinfo:`.

### Exit status

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Error (e.g. file not found or not parseable). For `diff`: the projects differ. |
| `2` | Usage error (unknown command or option, bad option value). For `diff`: any error. |
| `130` | Interrupted (Ctrl+C) |

## Git integration

PLC exports are large XML files, so `git diff` on them shows markup noise rather than logic changes. plcinfo plugs into git in two ways.

### `git diff`, `git log -p` and code review: textconv

`plcinfo textconv FILE` prints a project as stable, line-oriented text: one line per rung, tag, data type member, network and source line. Volatile data like export timestamps and file paths is left out. Rung numbers are left out too, because Logix renumbers every following rung when one is inserted. Register it as a textconv driver:

```bash
# Once per machine
git config --global diff.plcinfo.textconv "plcinfo textconv"
git config --global diff.plcinfo.cachetextconv true

# Once per repository (commit this file)
cat >> .gitattributes <<'ATTR'
*.L5X diff=plcinfo
*.l5x diff=plcinfo
ATTR
```

Now `git diff`, `git log -p project.L5X` and `git show` show logic changes:

```diff
 MainProgram/MainRoutine rung: NOP();
+MainProgram/MainRoutine rung: XIC(DINT.5)OTE(DINT.8);
 MainProgram/MainRoutine rung: XIC(DINT.0)OTE(DINT.2);
-MainProgram/STRoutine 2| DINT := DINT AND ULINT; // Line 2 Comment
+MainProgram/STRoutine 2| DINT := DINT OR ULINT; // Line 2 Comment
```

Add the same `diff=plcinfo` attribute for any other format plcinfo reads.

### Detailed reports: difftool

For a report with rung numbers and instruction-level summaries, use `plcinfo diff` as a difftool:

```bash
git config --global difftool.plcinfo.cmd 'plcinfo diff "$LOCAL" "$REMOTE"'
git difftool --tool=plcinfo --no-prompt HEAD~1 -- project.L5X
```

`plcinfo diff` exits with `1` when the projects differ. git treats that as normal for diff tools.

## Quick Start Examples

### Review a change

```bash
plcinfo diff before.L5X after.L5X                          # rung-level report in the terminal
plcinfo diff before.L5X after.L5X -f html -o changes.html  # visual before/after ladder diagrams
```

### Gate a pull request in CI

```bash
plcinfo lint project.L5X --fail-on warning --ignore PLC006,PLC007
plcinfo diff main.L5X project.L5X -f md -o changes.md      # exit code 1 means "changed", not "failed"
```

### Find who drives an output

```bash
plcinfo xref project.L5X Conveyor_Run --writes
```

### Document a project

```bash
plcinfo report project.L5X -o project.html                 # one self-contained page with rendered ladder
plcinfo tags project.L5X -f csv -o tags.csv                 # tag list for Excel
plcinfo io project.L5X -f md -o io.md
```

### Work with different PLC platforms

```bash
plcinfo summary RockwellProject.L5X          # Rockwell export (full content)
plcinfo summary RockwellProject.ACD          # Rockwell project file (partial content)
plcinfo structure ./PLC_1                    # TIA Portal Openness export folder
plcinfo structure MOTOR.awl                  # Step 7 STL source
plcinfo metrics PLCopen.xml --format yaml    # CODESYS / Beremiz / OpenPLC PLCopen XML export
plcinfo tags Machine.tsproj --io-only        # Beckhoff TwinCAT 3 project
```

## Output Formats

| Format | Use it for |
|---|---|
| `text` (default) | Reading in a terminal. |
| `json`, `yaml` | Scripts and integrations. `export` writes the full neutral project model. |
| `md` | Pull request comments, wikis and documentation. |
| `csv` | Spreadsheets. Available for `tags`, `io`, `search`, `diff`, `xref` and `lint`. Files get a UTF-8 BOM so Excel detects the encoding. |
| `html` | `report` and `diff`: self-contained pages with rendered ladder logic. |

## Use Cases

- **Version Control** - Track changes to PLC code over time using `diff` command
- **Code Reviews** - Generate metrics and documentation for review processes
- **CI/CD Integration** - Automated testing and validation of PLC projects
- **Documentation** - Auto-generate project documentation in various formats
- **Cross-Platform Analysis** - Analyze projects from different vendors with a single tool
- **Migration Planning** - Export to neutral format for migration between platforms
- **Audit & Compliance** - Generate reports for compliance and audit requirements

## Architecture

The tool uses a modular parser architecture with vendor-specific parsers implementing a common interface. All parsed data is converted to a neutral internal model, allowing consistent analysis regardless of the source platform.

## Contributing

Contributions are welcome, especially:

- **Real-world PLC exports** from other vendors and versions. Test fixtures must be permissively licensed (MIT, BSD, Apache-2.0, 0BSD or CC0), unmodified, pinned to a commit and listed in `PlcInfo.Tests/Fixtures/THIRD_PARTY_NOTICES.md`.
- **Parser improvements** for format variations that aren't handled yet. Run plcinfo on the file and open an issue with the warning or error it prints.
- **New lint rules and analyses.**

Every pull request is built and tested on Ubuntu and Windows (`.github/workflows/ci.yml`), and warnings are treated as errors.

If you work with PLCs and have example project files you can share, please consider contributing them to help improve the tool's reliability across different PLC platforms and versions.