# DataMiner Solution Builder

An AI-agent pipeline that generates a complete DataMiner SDM solution from a natural-language prompt or requirements document — including backend, frontend, documentation, local dev environment, and automated tests.

---

## Prerequisites

### Required SDKs

| SDK | Version | Install | Used By |
|-----|---------|---------|---------|
| .NET SDK | 10+ | `winget install Microsoft.DotNet.SDK.10` | All scaffolder scripts, Aspire AppHost, ServiceDefaults, UdapiProxy, DllWatcher |
| .NET Framework 4.8 Dev Pack | 4.8 | [Download](https://dotnet.microsoft.com/download/dotnet-framework/net48) | AutomationHost, UDAPI project, GQI project (net48 targets) |
| Node.js + npm | 18+ LTS | `winget install OpenJS.NodeJS` | Frontend (Vite + React), Playwright tests |

### Required .NET Global Tools

| Tool | Install Command | Used By |
|------|----------------|---------|
| DataMiner Templates | `dotnet new install Skyline.DataMiner.VisualStudioTemplates` | DevPack Builder, Backend Builder (automation/package project templates) |
| SDM CLI | `dotnet tool install --global skyline.dataminer.sdm.tools` | DevPack Builder (generates `DomMapper.g.cs` files) |
| DocFX | `dotnet tool install --global docfx` | Documentation Builder (`docfx metadata` + `docfx build`) |
| Aspire CLI | `dotnet tool install --global aspire` | Solution Tester (start/stop/resource management) |

### Required External Tools

| Tool | Install Command | Used By | Notes |
|------|----------------|---------|-------|
| Foundry Local | `winget install Microsoft.AI.Foundry.Local` | Aspire Integration (AI Coworker component) | Must be running (`foundry service start`) |
| k6 | `winget install GrafanaLabs.k6` | UDAPI Tester (smoke + load tests) | |
| Schemathesis | `pip install schemathesis` | UDAPI Tester (fuzz tests) | Requires Python 3.11+ |
| Playwright | `npx playwright install` | Frontend Tester (E2E browser tests) | Installed per-project via npm |
| Git | `winget install Git.Git` | Branch management | |
| PowerShell 7+ | `winget install Microsoft.PowerShell` | Script execution | Included in Windows 11 |

### Required NuGet Packages (Local Feed)

The Aspire Integration requires 8 custom NuGet packages to be pre-built and available
in a local NuGet feed (default: `C:\Users\Tim\source\nugets`):

| Package | Version | Content |
|---------|---------|---------|
| `Skyline.DataMiner.Aspire.AutomationHost` | 1.0.0 | .NET Framework 4.8 script runner executable |
| `Skyline.DataMiner.Aspire.AutomationHost.Hosting` | 1.0.0 | `AddAutomationHost()` Aspire extension (net10.0) |
| `Skyline.DataMiner.Aspire.UdapiProxy` | 1.0.0 | REST proxy executable with Scalar UI (net10.0) |
| `Skyline.DataMiner.Aspire.UdapiProxy.Hosting` | 1.0.0 | `AddUdapiProxy()` Aspire extension (net10.0) |
| `Skyline.DataMiner.Aspire.DllWatcher` | 1.0.0 | File watcher for hot-reload (net10.0) |
| `Skyline.DataMiner.Aspire.DllWatcher.Hosting` | 1.0.0 | `AddDllWatcher()` Aspire extension (net10.0) |
| `Skyline.DataMiner.Aspire.AiCoworker` | 1.0.0 | AI assistant integration component |
| `Skyline.DataMiner.Aspire.AiCoworker.Hosting` | 1.0.0 | `AddAiCoworker()` Aspire extension (net10.0) |

Build from source in this repo:

```powershell
# UdapiProxy
cd dataminer-aspire-integration/UdapiProxy/src/Skyline.DataMiner.Aspire.UdapiProxy
dotnet publish -c Release && dotnet pack -c Release --no-build -o C:\Users\Tim\source\nugets

# DllWatcher
cd dataminer-aspire-integration/DllWatcher/src/Skyline.DataMiner.Aspire.DllWatcher
dotnet publish -c Release && dotnet pack -c Release --no-build -o C:\Users\Tim\source\nugets

# AutomationHost (net48)
cd dataminer-aspire-integration/AutomationHost/src/Skyline.DataMiner.Aspire.AutomationHost
dotnet publish -c Release && dotnet pack -c Release --no-build -o C:\Users\Tim\source\nugets
```

Repeat for each `*.Hosting` project in the same directories.

### Aspire AppHost Dependencies (auto-restored from nuget.org)

These are referenced by the generated AppHost project and restored automatically:

| Package | Version |
|---------|---------|
| `Aspire.AppHost.Sdk` | 13.4.4 |
| `Aspire.Hosting.NodeJs` | 9.5.2 |
| `Aspire.Hosting.Foundry` | 13.4.0-preview.1.26281.18 |
| `Microsoft.Extensions.Http.Resilience` | 10.2.0 |
| `Microsoft.Extensions.ServiceDiscovery` | 10.2.0 |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.15.3 |
| `OpenTelemetry.Extensions.Hosting` | 1.15.3 |
| `OpenTelemetry.Instrumentation.AspNetCore` | 1.15.2 |
| `OpenTelemetry.Instrumentation.Http` | 1.15.1 |
| `OpenTelemetry.Instrumentation.Runtime` | 1.15.1 |

### Quick Install (All Prerequisites)

```powershell
# SDKs
winget install Microsoft.DotNet.SDK.10
winget install OpenJS.NodeJS

# .NET global tools
dotnet new install Skyline.DataMiner.VisualStudioTemplates
dotnet tool install --global skyline.dataminer.sdm.tools
dotnet tool install --global docfx
dotnet tool install --global aspire

# External tools
winget install Microsoft.AI.Foundry.Local
winget install GrafanaLabs.k6
pip install schemathesis

# Playwright (per-project, run after npm install)
npx playwright install
```

---

## Pipeline Overview

```
Prompt / Document
       │
       ▼
┌──────────────────────────┐
│  1. Model Analyzer       │  Extracts domain → YAML + SolutionDescription + UserRoles + UserFlows
└──────────┬───────────────┘
           │
           ▼
┌──────────────────────────┐
│  2. DevPack Builder      │  Generates NuGet devpack (models, API helpers, DomMapper, DOM installer)
└──────────┬───────────────┘
           │
           ▼
┌──────────────────────────┐
│  3. Backend Builder      │  UDAPI + GQI + Assistant MD files + Backend installer
└──────────┬───────────────┘
           │
           ▼
┌──────────────────────────┐
│  4. Frontend Builder     │  React + Vite SPA + UI installer (.dmapp)
└──────────┬───────────────┘
           │
           ▼
┌──────────────────────────┐
│  5. Documentation Builder│  DocFX site + Documentation installer (.dmapp)
└──────────┬───────────────┘
           │
           ▼
┌──────────────────────────┐
│  6. Aspire Integration   │  Local dev environment (AutomationHost, UdapiProxy, DllWatcher)
└──────────┬───────────────┘
           │
           ▼
┌──────────────────────────┐
│  7. Solution Tester      │  k6 smoke/load tests + Schemathesis fuzz + Playwright E2E
└──────────────────────────┘
```

Each stage is implemented as an independent agent with its own SKILL.md and scaffolder script.
The top-level orchestrator (`dataminer-solution-builder.agent.md`) routes new solutions through
the full pipeline and routes change requests to the correct layer.

---

## Features

### Stage 1: Model Analyzer

- Parses natural language descriptions, Word/PDF/text documents into structured output
- Produces canonical YAML domain model spec consumed by all downstream stages
- Generates `SolutionDescription.md` (human-readable solution overview)
- Generates `UserRoles.md` (roles + permissions for DataMiner Assistant agents)
- Generates `UserFlows.md` (user workflows for DataMiner Assistant skills)

### Stage 2: DevPack Builder

- Scaffolds complete .NET solution with SDK-style projects
- Generates C# model classes (`SdmObject<T>`, enums, sub-objects)
- Generates API helper interfaces and implementations
- Runs SDM DomMapper CLI to produce `DomMapper.g.cs` files
- Generates DOM installer (section definitions, field descriptors, DOM definitions)
- Generates `Package.cs` installer entry point
- Builds and outputs NuGet package (`.nupkg`)

### Stage 3: Backend Builder

Orchestrates 5 sub-scripts in sequence:

| Sub-step | Script | Output |
|----------|--------|--------|
| 3a. Solution | `New-Backend.cs` | Empty `{Name}Backend.slnx` |
| 3b. UDAPI | `New-Udapi.cs` | Automation project with REST controllers + `openapi.yaml` |
| 3c. GQI | `New-Adhoc.cs` | Ad-hoc data sources (one per model + sub-object) |
| 3d. Installer | `New-BackendInstaller.cs` | `.Package` project with DOM + UDAPI registration |
| 3e. Assistant | `New-AssistantMdFiles.cs` + agent | DataMiner Assistant context files (see below) |

**Assistant MD Files** (agent-guided):
- Ad-hoc data source descriptions (one `.md` per GQI source)
- Script tool description for the UDAPI
- Skills (one per user flow from `UserFlows.md`)
- Agents (one per user role from `UserRoles.md`)
- Enforces platform constraints (8192 char limit, name rules, folder matching)

### Stage 4: Frontend Builder

- Delegates to `DataMiner App Builder` agent for React + Vite SPA generation
- CRUD pages per model with data tables, modals, filtering/sorting
- Sub-object management within parent entities
- DateTime handling (omit nulls, guard `0001-01-01`)
- Packages frontend into `.dmapp` via UI installer

### Stage 5: Documentation Builder

- Scaffolds DocFX site with Skyline branding and dark-mode template
- Fills content from SolutionDescription.md, YAML model, and openapi.yaml
- Extracts C# API reference via `docfx metadata` from devpack XML docs
- Builds static HTML site with `docfx build`
- Packages into `.dmapp` installer for deployment to DataMiner `Webpages/Public/Documentation/`

### Stage 6: Aspire Integration

Local development environment with no DataMiner agent required:

| Component | Purpose |
|-----------|---------|
| **AutomationHost** | .NET Framework 4.8 script runner (executes backend DLL code) |
| **UdapiProxy** | REST proxy translating HTTP → ApiTriggerInput/Output; includes Scalar OpenAPI UI |
| **DllWatcher** | Monitors DLL changes, triggers hot-reload and AutomationHost restart |
| **ApiService** | Mock DataMiner Web API + frontend static file hosting |
| **Frontend dev server** | Vite dev server with HMR |
| **Documentation site** | `docfx serve _site` for local documentation preview |

All components are distributed as NuGet tool packages and orchestrated by a single AppHost.

### Stage 7: Solution Tester

- **UDAPI tests**: k6 smoke tests, load tests, Schemathesis fuzz tests against REST endpoints
- **Frontend tests**: Playwright E2E tests (CRUD operations, navigation, error states)
- Tests run against both local Aspire (`localhost`) and deployed DataMiner
- Iterates until all tests pass, fixing issues in the appropriate layer

### Top-Level Orchestrator

The `dataminer-solution-builder.agent.md` handles two request types:

| Type | Action |
|------|--------|
| **New solution** | Runs full pipeline (Stages 1–7) + tutorial generation with screenshots |
| **Change request** | Routes to correct layer, validates with tests, updates docs automatically |

**Post-change automation** (after any modification):
- Runs relevant tests (mapped by change type)
- Updates documentation and rebuilds DocFX site
- Rebuilds affected installer packages
- Restarts Aspire resources as needed

---

## Subfolders

| Folder | Stage | Description |
|--------|-------|-------------|
| [`dataminer-model-analyzer/`](dataminer-model-analyzer/) | 1 | Parse domain descriptions into structured YAML spec |
| [`dataminer-devpack-builder/`](dataminer-devpack-builder/) | 2 | Generate NuGet devpack (models, API helpers, DomMapper, DOM installer) |
| [`dataminer-backend-builder/`](dataminer-backend-builder/) | 3 | Generate UDAPI + GQI + Assistant MD files + backend installer |
| [`dataminer-frontend-builder/`](dataminer-frontend-builder/) | 4 | Build React + Vite SPA + UI installer |
| [`dataminer-documentation-builder/`](dataminer-documentation-builder/) | 5 | Generate DocFX site + documentation installer |
| [`dataminer-aspire-integration/`](dataminer-aspire-integration/) | 6 | .NET Aspire local dev environment (AutomationHost, UdapiProxy, DllWatcher) |
| [`dataminer-solution-tester/`](dataminer-solution-tester/) | 7 | k6 smoke/load + Schemathesis fuzz + Playwright E2E tests |
| [`dataminer-newsolution-builder/`](dataminer-newsolution-builder/) | — | Full pipeline orchestrator skill |

---

## YAML Spec — Canonical Format

All stages communicate through a single YAML domain spec.
The Model Analyzer writes it; all downstream stages read it.

```yaml
solution:
  name: "SDMEvent"                            # SDM<DomainName>
  domModuleId: "exampleeventmgmt"             # example<domainname>mgmt
  nugetPackageId: "Skyline.DataMiner.Utils.SDMEvent"
  apiRoute: "eventmanager/events"             # <domainname>manager/<domainname>s
  apiName: "Example Event Manager API"
  apiDescription: "The webapi for the example event manager."

models:
  - name: "Event"
    properties:
      - { name: "Name",        type: "string" }
      - { name: "Description", type: "string" }
      - { name: "Start",       type: "DateTime" }
      - { name: "End",         type: "DateTime" }
      - { name: "Type",        type: "enum",  enum: "EventType" }
      - { name: "Status",      type: "enum",  enum: "EventStatus" }
    lists:
      - { name: "Languages",   type: "Language" }

enums:
  - name: "EventStatus"
    values: ["Requested", "Processing", "Done"]
  - name: "EventType"
    values: ["Basic", "Pro", "Advanced"]
  - name: "LanguageAudioType"
    values: ["Stereo", "Surround", "Mono"]

subObjects:
  - name: "Language"
    properties:
      - { name: "Name",                 type: "string" }
      - { name: "AudioType",            type: "enum", enum: "LanguageAudioType" }
      - { name: "CcSupplierCompanyName",type: "string" }

openApiSpec: "openapi.yaml"
```

### Supported Property Types

| YAML type | C# / DataMiner type | Notes |
|-----------|---------------------|-------|
| `string` | `string` | |
| `DateTime` | `DateTime` | |
| `TimeSpan` | `TimeSpan` | |
| `Int64` | `long` | |
| `double` | `double` | |
| `bool` | `bool` | |
| `enum` | `GenericEnum<int>` | Requires matching `enums[]` entry |
| `ref` | `Guid` stored as `{Name}Id` | Reference to another model |

---

## Naming Conventions

Derived automatically from the domain name — never manually specified:

| Concept | Convention | Example |
|---------|-----------|---------|
| Solution name | `SDM<DomainName>` | `SDMEvent` |
| DOM module ID | `example<domainname>mgmt` | `exampleeventmgmt` |
| NuGet package ID | `Skyline.DataMiner.Utils.SDM<DomainName>` | `Skyline.DataMiner.Utils.SDMEvent` |
| API route | `<domainname>manager/<domainname>s` | `eventmanager/events` |
| UDAPI script name | `SDM<DomainName>UDAPI` | `SDMEventUDAPI` |
| Backend solution | `SDM<DomainName>Backend` | `SDMEventBackend` |
| Frontend folder | `SDM<DomainName>Frontend` | `SDMEventFrontend` |
| Documentation folder | `SDM<DomainName>Documentation` | `SDMEventDocumentation` |
| Aspire folder | `SDM<DomainName>Aspire` | `SDMEventAspire` |
| Tester folder | `SDM<DomainName>Tester` | `SDMEventTester` |

---

## Quick Start

```powershell
# Set paths
$yaml = "path/to/DomainInput.yaml"
$out  = "C:\temp\output"

# Stage 1: Model Analyzer (agent-driven — no script, follows SKILL.md)

# Stage 2: DevPack Builder
dotnet run dataminer-devpack-builder/New-DevPack.cs -- -i $yaml -o $out

# Stage 2b: Register local NuGet feed (required before backend)
dotnet nuget add source "$out/SDM<Domain>/SDM<Domain>/bin/Debug" --name LocalSDM

# Stage 3: Backend Builder (5 sub-steps)
dotnet run dataminer-backend-builder/New-Backend.cs -- -i $yaml -o $out
dotnet run dataminer-backend-builder/dataminer-udapi-builder/New-Udapi.cs -- -i $yaml -o $out
dotnet run dataminer-backend-builder/dataminer-adhoc-builder/New-Adhoc.cs -- -i $yaml -o $out
dotnet run dataminer-backend-builder/dataminer-backend-installer/New-BackendInstaller.cs -- -i $yaml -o $out
dotnet run dataminer-backend-builder/dataminer-assistant-mdfiles/New-AssistantMdFiles.cs -- -i $yaml -o $out
# Then follow dataminer-assistant-mdfiles/SKILL.md to create actual content

# Stage 4: Frontend Builder (agent-driven — delegates to DataMiner App Builder)

# Stage 5: Documentation Builder
dotnet run dataminer-documentation-builder/dataminer-docfx-builder/New-DocfxBuilder.cs -- -i $yaml -o $out
# Fill content (agent-driven)
cd $out/SDM<Domain>Documentation
docfx metadata docfx.json
docfx build docfx.json
dotnet run dataminer-documentation-builder/dataminer-documentation-installer/New-DocumentationInstaller.cs -- -i $yaml -o $out

# Stage 6: Aspire Integration
dotnet run dataminer-aspire-integration/New-AspireIntegration.cs -- -i $yaml -o $out

# Stage 7: Solution Tester (agent-driven — starts Aspire, runs k6 + Playwright)
```

---

## Technology Stack

| Layer | Technology |
|-------|-----------|
| Scaffolder scripts | .NET 10 single-file programs (`#:sdk`, `#:package` directives) |
| Backend | .NET Framework 4.8 (DataMiner automation scripts) |
| Frontend | React 18 + Vite + TypeScript |
| Documentation | DocFX with Skyline dark-mode template |
| Local dev | .NET Aspire (AppHost orchestration) |
| API testing | k6 (smoke/load) + Schemathesis (fuzz) |
| E2E testing | Playwright |
| Package format | `.dmapp` (DataMiner Application Package) |
