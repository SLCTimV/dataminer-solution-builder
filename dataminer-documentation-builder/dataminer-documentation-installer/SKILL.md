---
name: dataminer-documentation-installer
description: >
  Creates a DataMiner package project (.dmapp) that deploys the DocFX documentation
  site to Webpages/Public/Documentation/<SolutionName>/ on a DataMiner system.
  Builds the DocFX site if needed, copies the _site output into CompanionFiles,
  and builds the installable package. Re-running always picks up the latest docs.
  USE FOR: "package documentation", "create docs installer", "deploy documentation",
  "rebuild docs package", "update documentation package", Stage 6 of the SDM pipeline.
  DO NOT USE FOR: scaffolding the DocFX site (use dataminer-docfx-builder), filling
  in documentation content (use dataminer-docfx-builder SKILL), frontend packaging
  (use dataminer-ui-installer), backend packaging (use dataminer-backend-installer).
argument-hint: "Provide --input-yaml and --output-dir."
---

# Documentation Installer Skill

## Purpose

Package the DocFX documentation site into a DataMiner `.dmapp` package that deploys
the built HTML site to `Webpages/Public/Documentation/<SolutionName>/` on the target
DataMiner system.

## Script

```
New-DocumentationInstaller.cs
```

A single-file .NET 10 program. Run directly with:

```powershell
dotnet run New-DocumentationInstaller.cs -- --input-yaml <path-to-yaml> [--output-dir <root>]
```

### Parameters

| Parameter          | Required | Default    | Description |
|--------------------|----------|------------|-------------|
| `-i, --input-yaml` | Yes      | —          | Path to the YAML domain model definition file |
| `-o, --output-dir` | No       | `C:\temp`  | Root directory where `<SolutionName>Documentation/` exists |

## Prerequisites

Before running this script:

1. The **DocFX site must be scaffolded** — run `New-DocfxBuilder.cs` first
2. The **documentation content should be filled in** — see the docfx-builder SKILL.md
3. **DocFX must be installed** — `dotnet tool install -g docfx`

If the `_site` folder does not exist, the script will attempt to run `docfx build`
automatically.

## Steps Performed

| Step | Description |
|------|-------------|
| 1/3 | Scaffold package project — creates `<SolutionName>Documentation.Package` using `dataminer-package-project` template |
| 2/3 | Copy docs build — cleans and copies `_site/` contents to `PackageContent/CompanionFiles/Skyline DataMiner/Webpages/Public/Documentation/<SolutionName>/` |
| 3/3 | Build package — compiles the `.dmapp` package |

## Deployment Target

When the package is installed on a DataMiner system, the documentation is accessible at:

```
http://<dataminer-host>/public/Documentation/<SolutionName>/index.html
```

## Re-running After Changes

If documentation content changes (markdown files updated, new pages added):

1. Rebuild the DocFX site: `docfx build docfx.json` (in the Documentation folder)
2. Re-run this script — it always cleans and re-copies the `_site` folder
3. The rebuilt package will contain the latest documentation

This ensures the `.dmapp` package is always in sync with the documentation source.

## Output Structure

```
<OutputDir>/<SolutionName>Documentation/
├── <SolutionName>Documentation.Package/
│   ├── <SolutionName>Documentation.Package.csproj
│   ├── Package.cs
│   └── PackageContent/
│       └── CompanionFiles/
│           └── Skyline DataMiner/
│               └── Webpages/
│                   └── Public/
│                       └── Documentation/
│                           └── <SolutionName>/
│                               ├── index.html
│                               ├── articles/
│                               ├── devpack/
│                               ├── webapi/
│                               └── ...
```

## Pipeline Position

```
Model Analyzer → DevPack → Backend → Frontend → DocFX Builder → Documentation Installer
                                                                       ▲ (this)
```

This skill runs as **Stage 6** — after the DocFX builder has scaffolded and the
agent has filled in the documentation content.
