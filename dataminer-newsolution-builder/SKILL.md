---
name: dataminer-newsolution-builder
description: >
  End-to-end orchestrator that builds a complete DataMiner SDM solution from a user
  prompt or requirements document. Runs the full pipeline in order: Model Analyzer →
  DevPack Builder → Backend Builder → Frontend Builder → Documentation Builder →
  Aspire Integration → Solution Tester.
  USE FOR: "build a new DataMiner solution", "create a full SDM app for...",
  "scaffold everything from scratch", "generate a complete solution", "new solution
  from requirements".
  DO NOT USE FOR: running individual pipeline stages (invoke the sub-skill directly),
  connector development (use dataminer-connector-core), deploying to a live system
  (use deploy tools after testing passes).
argument-hint: >
  Describe the domain in natural language or provide a path to a requirements document.
  Example: "Create a DataMiner solution for managing world events with name, dates,
  type, status, and multilingual audio delegations"
disable-model-invocation: false
---

# New Solution Builder Skill

## Purpose

Build a complete DataMiner SDM solution from scratch — from a user prompt or
requirements document all the way through to a tested, locally-runnable application.

## Pipeline Overview

```
User Prompt / Document
        │
        ▼
┌─────────────────────┐
│  1. Model Analyzer  │  → YAML model + SolutionDescription.md
└─────────────────────┘
        │
        ▼
┌─────────────────────┐
│  2. DevPack Builder │  → NuGet package (models, API helpers, DomMapper)
└─────────────────────┘
        │
        ▼
┌─────────────────────┐
│  3. Backend Builder │  → .slnx + UDAPI + GQI + Installer (.dmapp)
└─────────────────────┘
        │
        ▼
┌─────────────────────┐
│  4. Frontend Builder│  → React + Vite SPA
└─────────────────────┘
        │
        ▼
┌──────────────────────────┐
│  5. Documentation Builder│  → DocFX site + .dmapp package
└──────────────────────────┘
        │
        ▼
┌─────────────────────┐
│  6. Aspire Integration │  → AppHost + hot-reload local dev environment
└─────────────────────┘
        │
        ▼
┌─────────────────────┐
│  7. Solution Tester │  → k6 smoke + Playwright E2E (validates everything)
└─────────────────────┘
```

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| User prompt or document | Yes | Natural language domain description, or path to .docx/.pdf/.txt |
| Output directory | No | Where to generate the solution (default: current directory) |
| Solution name | No | Inferred from domain if not provided (e.g. `SDMWorldEvent`) |

## Step-by-Step Execution

### Step 1: Model Analyzer

**Skill**: `dataminer-model-analyzer`
**Script**: `dataminer-model-analyzer/` (no .cs — the skill itself does the analysis)

**Action**: Analyze the user's prompt/document and produce:
- `<Domain>Input.yaml` — structured YAML domain model
- `<Domain>SolutionDescription.md` — human-readable solution summary

**Validation**: Confirm the YAML has at least one model with properties and the solution
name is sensible. Present to user for approval before proceeding.

```
Output: tests/TestFolder/<Domain>Input.yaml
        tests/ModelAnalyzerTest/<Domain>SolutionDescription.md
```

---

### Step 2: DevPack Builder

**Skill**: `dataminer-devpack-builder`
**Script**: `dataminer-devpack-builder/New-DevPack.cs`

**Action**: Generate the .NET solution with model classes, enums, sub-objects, API
helpers, DomMapper, and DOM installer. Build the NuGet package.

**Input**: The YAML from Step 1
**Validation**: NuGet package builds successfully (`dotnet pack` exits 0)

```
Output: <OutputDir>/SDM<Domain>/
        <OutputDir>/SDM<Domain>/bin/Release/*.nupkg
```

---

### Step 3: Backend Builder

**Skill**: `dataminer-backend-builder`
**Script**: `dataminer-backend-builder/New-Backend.cs` (orchestrates sub-scripts)

**Action**: Generate the full backend automation solution:
- UDAPI automation script (REST controller)
- GQI ad-hoc data sources
- Assistant markdown files (agent-guided: one skill per user flow, one agent per user role)
- Installer package (DOM + UDAPI registration)

**Input**: YAML from Step 1 + DevPack NuGet from Step 2
**Validation**: `dotnet build` of the backend .slnx succeeds

```
Output: <OutputDir>/SDM<Domain>Backend/
```

---

### Step 4: Frontend Builder

**Skill**: `dataminer-frontend-builder`

**Action**: Generate the React + Vite SPA with:
- CRUD pages for each model
- Data table with filtering/sorting
- Modal forms with proper field types
- Sub-object management
- DateTime handling (omit nulls, guard 0001-01-01)

**Input**: YAML from Step 1 + SolutionDescription.md + UDAPI route info from backend
**Validation**: `npm run build` succeeds (no TypeScript/lint errors)

```
Output: <OutputDir>/SDM<Domain>Frontend/
```

---

### Step 5: Documentation Builder

**Skill**: `dataminer-documentation-builder`
**Scripts**:
- `dataminer-documentation-builder/dataminer-docfx-builder/New-DocfxBuilder.cs`
- `dataminer-documentation-builder/dataminer-documentation-installer/New-DocumentationInstaller.cs`

**Action**: Generate the DocFX documentation site and package it:
1. Scaffold the documentation structure (folders, `docfx.json`, Skyline template)
2. Fill in content from SolutionDescription.md, YAML model, and openapi.yaml
3. Build the site: `docfx build docfx.json`
4. Package into a `.dmapp` installer

**Input**: YAML from Step 1 + SolutionDescription.md + openapi.yaml from backend
**Validation**: `docfx build` succeeds and `_site/` folder is produced

```powershell
# Scaffold
dotnet run dataminer-documentation-builder/dataminer-docfx-builder/New-DocfxBuilder.cs -- \
  -i <yaml> -o <output-dir>

# Fill content (agent-driven — see docfx-builder SKILL.md)

# Build site
cd <OutputDir>/SDM<Domain>Documentation && docfx build docfx.json && cd -

# Package
dotnet run dataminer-documentation-builder/dataminer-documentation-installer/New-DocumentationInstaller.cs -- \
  -i <yaml> -o <output-dir>
```

```
Output: <OutputDir>/SDM<Domain>Documentation/
        <OutputDir>/SDM<Domain>Documentation/_site/
        <OutputDir>/SDM<Domain>Documentation/<Name>Documentation.Package/
```

---

### Step 6: Aspire Integration

**Skill**: `dataminer-aspire-integration`
**Script**: `dataminer-aspire-integration/New-AspireIntegration.cs`

**Action**: Generate the .NET Aspire AppHost that orchestrates:
- AutomationHost (runs UDAPI DLLs in .NET Framework 4.8)
- UdapiProxy (REST API + OpenAPI/Scalar)
- DllWatcher (hot-reload on build)
- ApiService (mock DataMiner Web API + auth mock + script proxy)
- Vite frontend dev server
- DocFX documentation site (if `_site` exists)
- Foundry Local + AI Coworker (local AI assistant)

**Input**: YAML + backend DLLs + devpack DLLs + openapi.yaml + frontend path
**Validation**: `dotnet build` of the AppHost succeeds

```
Output: <OutputDir>/SDM<Domain>Aspire/
```

---

### Step 7: Solution Tester

**Skill**: `dataminer-solution-tester`

**Action**: Start Aspire, then run the full test suite:

1. **Start Aspire**:
   ```powershell
   dotnet run --project <OutputDir>/SDM<Domain>Aspire/AspireSDM.AppHost --launch-profile http
   ```
   Wait for all services to be healthy (~15s).

2. **Scaffold tests** (if not already present):
   ```powershell
   # UDAPI tests
   dotnet script dataminer-solution-tester/dataminer-udapi-tester/New-UdapiTests.cs -- \
     --input-yaml <yaml> --backend <backend-dir>

   # Frontend tests
   dotnet script dataminer-solution-tester/dataminer-frontend-tester/New-FrontendTests.cs -- \
     --input-yaml <yaml> --frontend <frontend-dir>
   ```

3. **Run UDAPI smoke** (Phase 1):
   ```powershell
   cd <OutputDir>/SDM<Domain>Tester/udapitests
   .\run-tests.ps1 -Url http://localhost:5180 -Type smoke
   ```
   If fails → fix using `dataminer-udapi-tester` skill, re-run.

4. **Run frontend E2E** (Phase 3):
   ```powershell
   cd <OutputDir>/SDM<Domain>Tester/e2etests
   npm ci && npx playwright install chromium
   npx playwright test --reporter=list
   ```
   If fails → fix using `dataminer-frontend-tester` skill, re-run.

5. **Stop Aspire** when all tests pass.

**Validation**: k6 smoke all checks pass + Playwright all tests pass.

```
Output: <OutputDir>/SDM<Domain>Tester/
        ├── udapitests/
        └── e2etests/
```

---

## Final Solution Structure

```
<OutputDir>/
├── SDM<Domain>/              ← DevPack (NuGet models + API helpers)
├── SDM<Domain>Backend/       ← UDAPI + GQI + Installer (.dmapp)
├── SDM<Domain>Frontend/      ← React + Vite SPA
├── SDM<Domain>Documentation/ ← DocFX site + .dmapp installer
├── SDM<Domain>Aspire/        ← Aspire AppHost (local dev)
└── SDM<Domain>Tester/        ← Test suite
    ├── udapitests/           ← k6 smoke/load + schemathesis fuzz
    └── e2etests/             ← Playwright E2E
```

## Error Recovery

| Stage | Common Failure | Recovery |
|-------|---------------|----------|
| Model Analyzer | Ambiguous requirements | Ask user to clarify, regenerate YAML |
| DevPack Builder | Compile error in generated models | Fix model class, rebuild NuGet |
| Backend Builder | Missing NuGet reference | Ensure devpack .nupkg path is correct in nuget.config |
| Frontend Builder | Incorrect field types in modal | Regenerate with corrected YAML types |
| Aspire Integration | Port conflict | Kill orphaned processes, retry |
| Solution Tester | Null DateTime crash | Apply DateTime rule: omit instead of null |

## Key Rules

1. **Never skip a stage** — each depends on the previous stage's output
2. **Validate each stage** before moving to the next
3. **Present the YAML model** to the user after Step 1 for approval
4. **DateTime rule**: Never send `null` DateTimes — omit the field entirely
5. **Aspire auth**: The mock accepts all credentials — auth-related test failures on localhost are expected
6. **Stop on build failure** — if any `dotnet build` or `npm run build` fails, fix before continuing
