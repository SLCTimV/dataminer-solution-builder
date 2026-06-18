---
name: dataminer-docfx-builder
description: >
  Generates a DocFX documentation website for an SDM solution with Skyline branding.
  Scaffolds the folder structure, docfx.json, Skyline template, and placeholder
  markdown files. After scaffolding, an agent fills in actual content using the
  SolutionDescription.md, YAML model, and openapi.yaml as source material.
  USE FOR: "generate documentation", "create docs site", "build docfx project",
  "generate API docs", "create documentation website", Stage 5 of the SDM pipeline.
  DO NOT USE FOR: backend generation (use dataminer-backend-builder), frontend
  generation (use dataminer-frontend-builder), deploying to DataMiner.
argument-hint: "Provide --input-yaml and --output-dir."
---

# DocFX Builder Skill

## Purpose

Generate a complete DocFX documentation website for a DataMiner SDM solution.
This is a two-phase process:

1. **Scaffolding** (automated via `New-DocfxBuilder.cs`) — creates the folder
   structure, `docfx.json`, Skyline template, and placeholder markdown files.
2. **Content filling** (agent-driven via this SKILL) — replaces all `<!-- TODO -->`
   placeholders with real content derived from the solution artifacts.

## Script

```
New-DocfxBuilder.cs
```

A single-file .NET 10 program. Run directly with:

```powershell
dotnet run New-DocfxBuilder.cs -- --input-yaml <path-to-yaml> [--output-dir <root>]
```

### Parameters

| Parameter          | Required | Default    | Description |
|--------------------|----------|------------|-------------|
| `-i, --input-yaml` | Yes      | —          | Path to the YAML domain model definition file |
| `-o, --output-dir` | No       | `C:\temp`  | Root directory where `<SolutionName>Documentation/` is created |

## Content Filling Instructions

After the scaffold script has run, use the following source files to fill in
the placeholder content in each generated markdown file.

### Source Files Required

| File | Location | Purpose |
|------|----------|---------|
| `<Domain>Input.yaml` | Model Analyzer output | Domain model definitions |
| `<Domain>SolutionDescription.md` | Model Analyzer output | Business context and solution overview |
| `openapi.yaml` | `<BackendSolution>/UDAPI/bin/Debug/net48/openapi/openapi.yaml` | Full OpenAPI spec |
| DevPack `.csproj` | `<OutputDir>/<SolutionName>/<SolutionName>/` | Source for `docfx metadata` API extraction |

---

## File-by-File Content Guide

### `index.md` — Landing Page

**Source:** `SolutionDescription.md`

Replace the TODO placeholder with:

1. **Application overview** — 2-3 paragraphs explaining what the application does,
   its business context, and target audience. Pull from the "Overview" or
   "Description" section of `SolutionDescription.md`.
2. **Key features** — Already generated as a bullet list per model. Expand each
   bullet with 1-2 sentences describing what operations are available.
3. **Quick Start** — Verify the NuGet package name and API route are correct.

### `articles/getting-started.md` — Getting Started Guide

**Sources:** `SolutionDescription.md`, `openapi.yaml`

Fill in:

1. **Prerequisites** — List DataMiner version requirements, any DxM dependencies,
   required user permissions.
2. **Installation** — Step-by-step instructions for:
   - Installing the NuGet devpack in a Visual Studio project
   - Deploying the `.dmapp` backend package to DataMiner
   - Configuring the UDAPI bearer token (reference the UDAPI token setup procedure)
3. **Basic Usage** — Expand the code sample with a realistic example using actual
   property names from the YAML model. Show at least: get all, get by ID, create.

### `articles/architecture.md` — Architecture Overview

**Sources:** `SolutionDescription.md`, YAML model

Fill in:

1. **Overview** — Verify the component table is complete. Add any additional components
   (e.g., GQI data sources, Aspire orchestration).
2. **Data Flow** — Replace the ASCII diagram with a more detailed flow if the solution
   has multiple models with relationships.
3. **Domain Models** — Already generated from YAML. Add a 1-sentence description for
   each property explaining its business meaning.

### `devpack/index.md` — Devpack API Overview

**Source:** YAML model, DevPack project XML docs

Fill in:

1. **Package Contents** — Verify namespace table. Add any additional namespaces if the
   devpack has sub-namespaces (e.g., for sub-objects).
2. **Models** — Add 1-2 sentences per model describing its role in the domain.
3. **Enums** — Add a description for each enum value explaining when it's used.

### `devpack/<model>-usage.md` — Per-Model Usage Guide

**Source:** YAML model, DevPack source code

Fill in:

1. **Properties table** — Add a description column entry for each property.
2. **CRUD Operations** — Expand examples with:
   - Realistic property values (not just "Sample X")
   - Error handling patterns
   - Filtering examples (if the API helper supports filtering)
3. **Advanced patterns** — Add sections for:
   - Working with references (linking models together)
   - Working with lists/collections
   - Bulk operations (if supported)

### `webapi/index.md` — UDAPI Overview

**Source:** `openapi.yaml`

Fill in:

1. **Authentication** — Describe the exact steps to obtain a bearer token:
   - DataMiner connection info endpoint
   - Token format and lifetime
   - Refresh procedure
2. **Error Handling** — Add example error response JSON bodies.
3. **Available Endpoints** — Verify routes match the actual `openapi.yaml` paths.

### `webapi/endpoints.md` — Endpoint Reference

**Source:** `openapi.yaml`

Fill in:

1. **Request/Response schemas** — Replace placeholder JSON with actual schemas
   from `openapi.yaml`. Include all properties, not just the first 3.
2. **Query Parameters** — Add actual filter/sort parameters from the OpenAPI spec.
3. **Response codes** — Add any model-specific validation errors.

### `webapi/examples.md` — Request Examples

**Source:** `openapi.yaml`, domain knowledge

Fill in:

1. **curl examples** — Replace placeholder property values with realistic data
   matching the domain (e.g., real event names, status values from enums).
2. **JavaScript examples** — Add examples for all CRUD operations, not just list.
3. **Error scenarios** — Add examples showing error responses (401, 404, 400).

---

## Running DocFX Metadata

After filling content, run metadata extraction for the devpack API reference:

```powershell
cd <OutputDir>/<SolutionName>Documentation
docfx metadata docfx.json
```

This generates YAML API reference files in `devpack/api/` from the C# XML documentation
in the devpack project. These files are automatically included in the build.

## Building the Site

```powershell
cd <OutputDir>/<SolutionName>Documentation
docfx build docfx.json
docfx serve _site
```

Open `http://localhost:8080` to preview.

## Pipeline Position

```
Model Analyzer → DevPack Builder → Backend Builder → Frontend Builder → DocFX Builder
                                                                         ▲ (this)
```

This skill runs as **Stage 5** (after frontend generation). All source artifacts
must exist before content can be filled.

## Quality Checklist

Before marking documentation as complete, verify:

- [ ] All `<!-- TODO -->` comments have been replaced with real content
- [ ] Code examples compile and use correct property names from the YAML model
- [ ] API routes in webapi/ match the actual `openapi.yaml`
- [ ] `docfx build` completes without errors
- [ ] Navigation (toc.yml) links resolve correctly
- [ ] Dark mode renders correctly (check alerts, code blocks, tables)
- [ ] Search index generates (`_site/index.json` exists after build)
