---
name: dataminer-backend-builder
description: >
  Generates the complete DataMiner SDM backend solution from a YAML domain model.
  Orchestrates 5 scripts in sequence: creates the backend .slnx, adds the UDAPI
  automation project, adds GQI ad-hoc data sources, generates assistant md files,
  and adds the installer package with DOM + UDAPI registration.
  USE FOR: "create backend", "build backend solution", "generate UDAPI", "generate
  GQI data sources", "generate ad-hoc sources", "generate assistant files", "build
  installer package", "full backend pipeline", Stage 3 of the SDM pipeline.
  DO NOT USE FOR: generating the NuGet devpack (use dataminer-devpack-builder),
  extracting models from documents (use dataminer-model-analyzer), frontend/UI
  generation (use dataminer-frontend-builder), deploying packages to DataMiner.
argument-hint: >
  Provide the path to the input YAML file and optionally the output directory.
  The devpack NuGet must already exist in the output directory.
---

# Backend Builder Skill

## Purpose

Generate the full DataMiner SDM backend solution from a YAML domain model definition
and the solution description produced by the model analyzer. This skill orchestrates
5 scripts that together produce a complete backend automation solution ready for
deployment.

## Prerequisites

Before running this pipeline:
1. The **YAML input file** must exist (produced by the model analyzer or manually written)
2. The **DevPack NuGet** must already be built in the output directory (run `dataminer-devpack-builder` first)

The devpack is expected at: `<output-dir>/<SolutionName>/<SolutionName>/bin/Debug/*.nupkg`

## Pipeline (5 Steps)

Run these scripts in order. All share the same `--input-yaml` and `--output-dir` arguments.

| Order | Script | Location | Purpose |
|-------|--------|----------|---------|
| 1 | `New-Backend.cs` | `dataminer-backend-builder/` | Creates the empty `{Name}Backend.slnx` |
| 2 | `New-Udapi.cs` | `dataminer-backend-builder/dataminer-udapi-builder/` | Adds UDAPI automation project with controllers |
| 3 | `New-Adhoc.cs` | `dataminer-backend-builder/dataminer-adhoc-builder/` | Adds GQI ad-hoc data source project |
| 4 | `New-AssistantMdFiles.cs` | `dataminer-backend-builder/dataminer-assistant-mdfiles/` | Generates assistant skill/adhoc/script md files |
| 5 | `New-BackendInstaller.cs` | `dataminer-backend-builder/dataminer-backend-installer/` | Adds `.Package` project with DOM + UDAPI installers |

## Execution

```powershell
# Set variables
$yaml = "<path-to-input.yaml>"
$out  = "<output-directory>"

# Step 1: Create backend solution
dotnet run dataminer-backend-builder/New-Backend.cs -- -i $yaml -o $out

# Step 2: Add UDAPI project
dotnet run dataminer-backend-builder/dataminer-udapi-builder/New-Udapi.cs -- -i $yaml -o $out

# Step 3: Add GQI ad-hoc data sources
dotnet run dataminer-backend-builder/dataminer-adhoc-builder/New-Adhoc.cs -- -i $yaml -o $out

# Step 4: Generate assistant md files
dotnet run dataminer-backend-builder/dataminer-assistant-mdfiles/New-AssistantMdFiles.cs -- -i $yaml -o $out

# Step 5: Add installer package
dotnet run dataminer-backend-builder/dataminer-backend-installer/New-BackendInstaller.cs -- -i $yaml -o $out
```

### Parameters (same for all scripts)

| Parameter          | Required | Default    | Description |
|--------------------|----------|------------|-------------|
| `-i, --input-yaml` | Yes      | —          | Path to the YAML domain model definition file |
| `-o, --output-dir` | No       | `C:\temp`  | Root directory where solution folders are created |

## What Each Step Produces

### Step 1 — New-Backend.cs
- Creates `<output>/{Name}Backend/{Name}Backend.slnx`
- Empty solution container for backend projects

### Step 2 — New-Udapi.cs (8 internal steps)
- Scaffolds `{Name}UDAPI` automation project
- Generates `OnApiTrigger.cs`, `UserDefinedApiExtensions.cs`, `ErrorResponse.cs`
- Generates query parameter helpers
- Generates one controller per model with full CRUD (GET/POST/PUT/DELETE)
- Generates `openapi.yaml` spec (in build output at `bin/Debug/net48/openapi/openapi.yaml`)
- Builds the project

### Step 3 — New-Adhoc.cs (6 internal steps)
- Scaffolds `{Name}GQI` automation project (DataMinerType=AdHocDataSource)
- Generates `GQIPageEnumerator.cs` shared paging utility
- For each main model: `{Model}s/Columns.cs`, `Get{Model}s.cs`, `Inputs.cs` (OData filter)
- For each sub-object: `{SubObj}s/Columns.cs`, `Get{SubObj}s.cs`, `Inputs.cs` (parent identifier)
- Builds the project

### Step 4 — New-AssistantMdFiles.cs (3 internal steps)
- Generates `SetupContent/adhocs/get{model}s.md` — GQI data source descriptions with OpenAPI spec
- Generates `SetupContent/scripts/{Name}UDAPI.md` — Script description with ApiTriggerInput example
- Generates `SetupContent/skills/SKILL.md` — Assistant skill explaining retrieval and CRUD operations

### Step 5 — New-BackendInstaller.cs (5 internal steps)
- Scaffolds `{Name}Backend.Package` project
- Generates DOM installer (copies DomMapper .g.cs files, creates section/definition builders)
- Generates UDAPI installer (registers API routes)
- Generates `Package.cs` entry point
- Builds the package project

## Output Structure

```
<output-dir>/
├── {Name}/                          ← DevPack (prerequisite)
│   ├── {Name}.slnx
│   ├── {Name}/                      ← NuGet library with models, helpers
│   └── {Name}.Package/              ← DevPack installer
└── {Name}Backend/                   ← Backend (produced by this pipeline)
    ├── {Name}Backend.slnx
    ├── nuget.config
    ├── {Name}UDAPI/                 ← UDAPI automation script
    │   ├── Controllers/
    │   ├── OnApiTrigger.cs
    │   └── ...
    ├── {Name}GQI/                   ← GQI ad-hoc data sources
    │   ├── {Model}s/
    │   ├── {SubObj}s/
    │   └── GQIPageEnumerator.cs
    └── {Name}Backend.Package/       ← Installer package
        ├── DOM/
        ├── Installers/
        ├── SetupContent/
        │   ├── adhocs/              ← Assistant ad-hoc md files
        │   ├── scripts/             ← Assistant script md file
        │   └── skills/              ← Assistant skill md file
        └── Package.cs
```

## YAML Input Format

Same format as the devpack builder. See `dataminer-devpack-builder/SKILL.md` for full schema.

Minimum required fields:
```yaml
solution:
  name: "SDMExample"
  domModuleId: "examplemgmt"
  nugetPackageId: "Skyline.DataMiner.Utils.SDMExample"
  apiRoute: "examplemanager"
  apiName: "Example Manager API"
  apiDescription: "The web API for the example manager."

models:   # or mainModel for single-model
  - name: "Example"
    properties:
      - { name: "Name", type: "string" }
```
