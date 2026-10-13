# PLCInfo

[![CI](https://github.com/adamfisher/plcinfo/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/adamfisher/plcinfo/actions/workflows/ci.yml)

A cross-platform command-line tool for analyzing and inspecting Programmable Logic Controller (PLC) project files from multiple vendors. Extract project information, analyze code metrics, compare versions, and export data in various formats.

## Overview

PLCInfo provides a unified interface to read and analyze PLC project files from different manufacturers without requiring the proprietary software. Perfect for version control integration, CI/CD pipelines, documentation generation, and automated code reviews.

### Supported PLC Platforms

- **Rockwell Automation**
  - `.L5X` (Logix 5000 Export files)
  - `.ACD` (RSLogix/Studio 5000 Archive files)
- **Siemens**
  - `.s7p` (Step 7 Project archives)
  - TIA Portal project files
- **CODESYS** project files
- **Beckhoff TwinCAT** project files

> **Note:** This project is still in development and needs testing with real-world PLC files from various manufacturers. If you encounter any issues or have example files to contribute, please open an issue or submit a pull request. I'm not an industrial automation engineer and don't have access to a comprehensive set of PLC project files. Community contributions for testing and validation would be greatly appreciated!

## Features

- **Project Summary** - Quick overview of controller, firmware, programs, routines, and tags
- **Project Structure** - Hierarchical view of tasks, programs, and routines
- **Tag Analysis** - List and filter tags by scope, type, or I/O usage
- **I/O Configuration** - Display configured I/O modules and their details
- **Code Metrics** - Calculate complexity metrics, instruction counts, and code statistics
- **Search** - Find tags, instructions, or comments across the entire project
- **Diff Comparison** - Compare two project versions to identify changes
- **Export** - Convert projects to vendor-neutral JSON format
- **Multiple Output Formats** - Text, JSON, YAML, and Markdown output options

## Installation

### Quick Start - Download Pre-built Executable

The easiest way to get started is to download the latest pre-built executable from the [Releases](https://github.com/adamfisher/plcinfo/releases) page.

**Windows:**
1. Download `plcinfo.exe` from the latest release
2. Place it in a directory of your choice
3. (Optional) Add the directory to your PATH environment variable
4. Run `plcinfo --version` to verify installation

**Note:** The executable is self-contained and does not require .NET to be installed separately.

### Build from Source

If you prefer to build from source:

**Prerequisites:**
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later

**Using the build script (Windows):**
```powershell
git clone https://github.com/adamfisher/plcinfo.git
cd plcinfo
.\Build.ps1
```

The executable will be created in `publish/win-x64/plcinfo.exe`

**Manual build:**
```bash
git clone https://github.com/adamfisher/plcinfo.git
cd plcinfo
dotnet build
```

**Install as Global Tool:**
```bash
dotnet pack
dotnet tool install --global --add-source ./PlcInfo/nupkg plcinfo
```

**Run directly:**
```bash
dotnet run --project PlcInfo -- [command] [options]
```

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

Identify differences between two versions of a project.

```bash
plcinfo diff project-v1.L5X project-v2.L5X
plcinfo diff old.ACD new.ACD --format md
```

**Detects changes in:**
- Tags (added, removed, modified)
- Routines (added, removed, modified)
- Programs and tasks
- I/O configuration
- Controller settings

Exit code is `0` if identical, `1` if differences are found, and `2` if a file could not be read or parsed (useful for CI/CD).

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
| `-f`, `--format FORMAT` | Output format: `text` (default), `json`, `yaml`, `md`/`markdown`. `export` defaults to `json`. |
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

## Quick Start Examples

### Example 1: Basic Project Analysis

```bash
# Get a quick overview
plcinfo summary MyProject.L5X

# View the project structure
plcinfo structure MyProject.L5X

# List all controller-scoped tags
plcinfo tags MyProject.L5X --scope controller
```

### Example 2: Code Review Workflow

```bash
# Calculate complexity metrics
plcinfo metrics MyProject.L5X --format md > metrics.md

# Search for potential issues
plcinfo search MyProject.L5X "TODO" --kind comment

# Find all timer instructions
plcinfo search MyProject.L5X "TON" --kind instruction
```

### Example 3: Version Control Integration

```bash
# Compare versions (returns exit code 1 if different)
plcinfo diff main.L5X feature-branch.L5X --format md > changes.md

# Use in CI/CD pipeline
if plcinfo diff baseline.L5X current.L5X; then
    echo "No changes detected"
else
    echo "Changes found - review required"
    exit 1
fi
```

### Example 4: Documentation Generation

```bash
# Generate project documentation
plcinfo summary MyProject.L5X --format md > docs/summary.md
plcinfo structure MyProject.L5X --format md > docs/structure.md
plcinfo io MyProject.L5X --format md > docs/io-config.md
plcinfo metrics MyProject.L5X --format md > docs/metrics.md
```

### Example 5: Data Integration

```bash
# Export to JSON for custom processing
plcinfo export MyProject.L5X --output project.json

# Export tags as YAML
plcinfo tags MyProject.L5X --format yaml > tags.yaml

# Get metrics as JSON for monitoring dashboards
plcinfo metrics MyProject.L5X --format json | curl -X POST https://monitoring.example.com/api/metrics
```

### Example 6: Working with Different PLC Platforms

```bash
# Rockwell L5X file
plcinfo summary RockwellProject.L5X

# Rockwell ACD archive
plcinfo structure RockwellProject.ACD

# Siemens Step 7 project
plcinfo tags SiemensProject.s7p --format json

# TIA Portal project
plcinfo metrics TIAProject.ap16 --format yaml
```

## Output Formats

### Text Format (Default)

Clean, human-readable output suitable for terminal display.

### JSON Format

Structured data format for programmatic consumption and integration with other tools.

### YAML Format

Human-friendly structured format, ideal for configuration files and documentation.

### Markdown Format

Formatted documentation output with tables and hierarchy, perfect for generating reports or wiki pages.

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

Contributions are welcome! This project especially needs:

- **Testing with real-world PLC files** from various manufacturers and versions
- **Parser improvements** for better file format support
- **Bug reports** when encountering unsupported file variations
- **Example files** (with sensitive data removed) for testing
- **Feature requests** for additional analysis capabilities

If you work with PLCs and have example project files you can share, please consider contributing them to help improve the tool's reliability across different PLC platforms and versions.