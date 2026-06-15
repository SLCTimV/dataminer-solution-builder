# DataMiner Solution Builder

An AI-agent pipeline that generates a complete DataMiner SDM solution from a natural-language prompt or an existing document.

---

## Pipeline Overview

```
Prompt / Document
       │
       ▼
┌─────────────────────┐
│  1. Model Analyzer  │  Extracts business models → YAML spec
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│  2. Devpack Builder │  Scaffolds the .NET solution skeleton
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│  3. Backend Builder │  Generates DOM backend + UDAPI + GQI data source
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│  4. Frontend Builder│  Builds a static DataMiner app (React/Vite)
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│  5. Aspire          │  Wires local .NET Aspire integration for dev/test
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│  6. Solution Tester │  Smoke, load & fuzz tests against the API
└─────────────────────┘
```

Each stage is implemented as an independent agent in its own subfolder.  
The top-level orchestrator (`DataMiner Solution Creator` skill) coordinates them in sequence.

---

## Subfolders

| Folder | Stage | Description |
|--------|-------|-------------|
| [`dataminer-model-analyzer/`](dataminer-model-analyzer/) | 1 | Parse domain descriptions (natural language or document) into a structured YAML spec |
| [`dataminer-devpack-builder/`](dataminer-devpack-builder/) | 2 | Scaffold the Visual Studio solution skeleton (`.sln`, project files, NuGet references) |
| [`dataminer-backend-builder/`](dataminer-backend-builder/) | 3 | Generate DOM backend, UDAPI automation script, GQI ad-hoc data source, and `openapi.yaml` |
| [`dataminer-frontend-builder/`](dataminer-frontend-builder/) | 4 | Build the static DataMiner low-code app frontend from `openapi.yaml` |
| [`dataminer-aspire-integration/`](dataminer-aspire-integration/) | 5 | Add `.NET Aspire` local dev integration (ScriptHost sidecar, ApiService, dashboard) |
| [`dataminer-solution-tester/`](dataminer-solution-tester/) | 6 | Run smoke, load and property-based fuzz tests (k6 + Schemathesis + CATS) |

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

### Supported property types

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

These are derived automatically from the domain name — never ask the user:

| Concept | Convention | Example |
|---------|-----------|---------|
| Solution name | `SDM<DomainName>` | `SDMEvent` |
| DOM module ID | `example<domainname>mgmt` | `exampleeventmgmt` |
| NuGet package ID | `Skyline.DataMiner.Utils.SDM<DomainName>` | `Skyline.DataMiner.Utils.SDMEvent` |
| API route | `<domainname>manager/<domainname>s` | `eventmanager/events` |
| UDAPI script name | `SDM<DomainName>UDAPI` | `SDMEventUDAPI` |
| Frontend folder | `<AppId>Frontend` | `EventFrontend` |
| AppId | Domain name (no `SDM` prefix) | `Event` |

---

## Reference Repositories

| Repository | Used by stage(s) | Purpose |
|------------|-----------------|---------|
| [`AICreateSDMBackendAndUDAPI`](../AICreateSDMBackendAndUDAPI) | 2, 3 | PowerShell generator scripts (`Generate-DataMinerBackend.ps1`), reference scripts, devpack builder |
| [`DataMinerEventSolution`](../DataMinerEventSolution) | 3 | Reference implementation including the `SDMEventGQI` ad-hoc data source |
| [`AspireSDMIntegration`](../AspireSDMIntegration) | 5 | Aspire AppHost, ApiService, ScriptHost sidecar, `Add-AspireIntegration.ps1` |
| [`APITestingSolution`](../APITestingSolution) | 6 | k6, Schemathesis, and CATS test suites |
| [`agents`](../agents) | orchestrator | Skill definitions (`DataMiner Solution Creator`, `DataMiner Backend Generator`, `DataMiner Domain Parser`) |

---

## Orchestrator Skill

The full pipeline is coordinated by the **DataMiner Solution Creator** skill located at:

```
C:\Users\Tim\source\repos\agents\skills\dataminer-solution-creator\SKILL.md
```

It delegates each stage to a specialist agent in sequence and produces a final summary table with all artifact paths, deployment URLs, and the UDAPI bearer token.

---

## Quick Start (Manual)

```powershell
# 1. Describe your domain as a YAML spec (or let the Model Analyzer generate one)
# 2. Run the backend generator
pwsh Generator\Generate-DataMinerBackend.ps1 -InputYaml .\MyDomainInput.yaml -OutputDir .\Output -MainLocation C:\temp\MyDomain

# 3. Build backend + UDAPI
pwsh .\Output\Generated_Backend.ps1
pwsh .\Output\Generated_UDAPI.ps1

# 4. Build frontend (via DataMiner App Builder agent)

# 5. Add Aspire for local testing
pwsh C:\AspireSDMIntegration\Add-AspireIntegration.ps1 -WorkspacePath C:\temp\MyDomain

# 6. Run tests
cd APITestingSolution
run-tests.bat https://<dm-host>/api/custom <bearer-token>
```
