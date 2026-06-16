---
name: dataminer-aspire-integration
description: >
  Generates a .NET Aspire local development environment for a DataMiner SDM solution.
  Produces an AppHost that orchestrates: AutomationHost (net48 script runner),
  UdapiProxy (REST API with Scalar/OpenAPI), DllWatcher (hot-reload trigger),
  ApiService (mock DataMiner Web API + frontend static files), and a Vite frontend
  dev server. All components are distributed as NuGet tool packages.
  USE FOR: "create aspire integration", "add local dev environment", "generate
  aspire folder", "set up hot reload for UDAPI", "run SDM solution locally",
  "add Aspire to solution", Stage 5 of the SDM pipeline.
  DO NOT USE FOR: generating the UDAPI project (use dataminer-backend-builder),
  generating the frontend app (use dataminer-frontend-builder), generating models
  or devpack (use dataminer-devpack-builder), deploying to a live DataMiner agent.
argument-hint: >
  Provide --input-yaml (required). Optionally provide --output-dir, --udapi-dll,
  --devpack-dll, --openapi, --frontend, --nuget-feed for non-standard layouts.
---

# Aspire Integration Skill

## Purpose

Generate a complete .NET Aspire local development environment for a DataMiner SDM
solution. This allows developers to run the full SDM application (backend + frontend)
locally without needing a DataMiner agent, with hot-reload support for both DLLs
and frontend code.

## Prerequisites

Before running this script:
1. The **YAML input file** must exist (produced by the model analyzer or manually written)
2. The **DevPack** must be built (`bin/Debug/netstandard2.0/*.dll` exists)
3. The **UDAPI project** must be built (`bin/Debug/net48/*.dll` + `openapi/openapi.yaml` exists)
4. The **Frontend** folder must exist with a `package.json` and `dev` script
5. **NuGet packages** must be available at the feed location:
   - `Skyline.DataMiner.Aspire.AutomationHost` (1.0.0)
   - `Skyline.DataMiner.Aspire.AutomationHost.Hosting` (1.0.0)
   - `Skyline.DataMiner.Aspire.UdapiProxy` (1.0.0)
   - `Skyline.DataMiner.Aspire.UdapiProxy.Hosting` (1.0.0)
   - `Skyline.DataMiner.Aspire.DllWatcher` (1.0.0)
   - `Skyline.DataMiner.Aspire.DllWatcher.Hosting` (1.0.0)

## Script

```
New-AspireIntegration.cs
```

A single-file .NET 10 program. Run directly with:

```powershell
dotnet run dataminer-aspire-integration/New-AspireIntegration.cs -- --input-yaml <path> [options]
```

### Parameters

| Parameter          | Required | Default                   | Description |
|--------------------|----------|---------------------------|-------------|
| `-i, --input-yaml` | Yes      | —                         | Path to the YAML domain model definition file |
| `-o, --output-dir` | No       | `C:\temp`                 | Root directory where solution folders are located |
| `--udapi-dll`      | No       | `<out>/<Name>UDAPI/<Name>UDAPI/bin/Debug/net48/<Name>UDAPI.dll` | Path to the compiled UDAPI DLL |
| `--devpack-dll`    | No       | `<out>/<Name>/<Name>/bin/Debug/netstandard2.0/Skyline.DataMiner.Utils.<Name>.dll` | Path to the compiled DevPack DLL |
| `--openapi`        | No       | `<udapi-dir>/openapi/openapi.yaml` | Path to the OpenAPI spec file |
| `--frontend`       | No       | `<out>/<Name>Frontend`    | Path to the frontend project folder |
| `--nuget-feed`     | No       | `C:\Users\Tim\source\nugets` | Path to the local NuGet feed containing Aspire packages |

## Steps Performed

| Step | Description |
|------|-------------|
| 1/6 | Create Aspire folder structure (`<Name>Aspire/`) |
| 2/6 | Write `nuget.config` with local package source mapping |
| 3/6 | Write `AspireSDM.ServiceDefaults` project (OpenTelemetry, health checks) |
| 4/6 | Write `AspireSDM.ApiService` project (mock DataMiner Web API + static files) |
| 5/6 | Write `AspireSDM.AppHost` project (Aspire orchestrator with all resources) |
| 6/6 | Write `aspire.config.json` and `.slnx` solution file |

## Output Structure

```
<OutputDir>/
└── <SolutionName>Aspire/
    ├── nuget.config
    ├── aspire.config.json
    ├── <SolutionName>Aspire.slnx
    ├── AspireSDM.AppHost/
    │   ├── AppHost.cs
    │   ├── AspireSDM.AppHost.csproj
    │   ├── appsettings.json
    │   └── Properties/launchSettings.json
    ├── AspireSDM.ApiService/
    │   ├── Program.cs
    │   ├── AspireSDM.ApiService.csproj
    │   ├── appsettings.json
    │   └── appsettings.Development.json
    └── AspireSDM.ServiceDefaults/
        ├── Extensions.cs
        └── AspireSDM.ServiceDefaults.csproj
```

## Architecture

```
Aspire AppHost (orchestrator)
├── automationhost    — .NET Framework 4.8 process, runs UDAPI script DLLs via HTTP
├── dataminerwebapi   — ASP.NET Core mock DataMiner Web API + frontend static files
├── udapi             — REST proxy (HTTP → ApiTriggerInput) with Scalar OpenAPI UI
├── dllwatcher        — Monitors UDAPI + DevPack DLLs, triggers reload on change
└── frontend          — npm dev server (Vite) with HMR via AddNpmApp
```

### Hot Reload Flow

- **Frontend edits** → Vite HMR refreshes browser automatically
- **UDAPI DLL rebuild** → DllWatcher detects change → signals AutomationHost to restart
- **DevPack DLL rebuild** → DllWatcher touches UDAPI DLL → cascade triggers AutomationHost restart

## Running the Generated Aspire App

```powershell
# Start Aspire
dotnet run --project "<OutputDir>/<Name>Aspire/AspireSDM.AppHost" --launch-profile http

# Aspire Dashboard (resource status, logs, traces)
# → http://localhost:15146

# Scalar OpenAPI UI (test UDAPI endpoints)
# → http://localhost:5180/scalar/v1

# Frontend dev server
# → http://localhost:5173
```

## NuGet Package Details

The Aspire integration relies on 6 NuGet packages built from source projects in this
repository under `dataminer-aspire-integration/`:

| Package | Content | Target Framework |
|---------|---------|-----------------|
| `Skyline.DataMiner.Aspire.AutomationHost` | Executable that loads net48 DLLs | `tools/net48/` |
| `Skyline.DataMiner.Aspire.AutomationHost.Hosting` | `AddAutomationHost()` extension | library (net10.0) |
| `Skyline.DataMiner.Aspire.UdapiProxy` | REST proxy executable | `tools/net10.0/` |
| `Skyline.DataMiner.Aspire.UdapiProxy.Hosting` | `AddUdapiProxy()` extension | library (net10.0) |
| `Skyline.DataMiner.Aspire.DllWatcher` | File watcher executable | `tools/net10.0/` |
| `Skyline.DataMiner.Aspire.DllWatcher.Hosting` | `AddDllWatcher()` extension | library (net10.0) |

### Building Packages

```powershell
# Example for UdapiProxy (same pattern for DllWatcher)
cd dataminer-aspire-integration/UdapiProxy/src/Skyline.DataMiner.Aspire.UdapiProxy
dotnet publish -c Release
dotnet pack -c Release --no-build -o C:\Users\Tim\source\nugets

# AutomationHost (net48 — requires MSBuild/Visual Studio)
cd dataminer-aspire-integration/AutomationHost/src/Skyline.DataMiner.Aspire.AutomationHost
dotnet publish -c Release
dotnet pack -c Release --no-build -o C:\Users\Tim\source\nugets
```

## YAML Input Format

Uses the same YAML format as other pipeline stages. Only the `solution` block is read:

```yaml
solution:
  name: "SDMWorldEvent"
  apiRoute: "worldeventmanager"
  apiName: "World Event Manager API"
```

## Pipeline Position

This is **Stage 5** in the full SDM pipeline:

1. Model Analyzer → extracts domain models from documents
2. DevPack Builder → generates C# models + NuGet package
3. Backend Builder → generates UDAPI + GQI + installer
4. Frontend Builder → generates React + Vite SPA
5. **Aspire Integration** → generates local dev environment ← you are here
