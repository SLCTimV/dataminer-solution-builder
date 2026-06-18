# DataMiner DocFX Builder

## Purpose

Generates a DocFX documentation website for a DataMiner SDM solution. The site documents the application overview, devpack API methods, and UDAPI (Web API) usage. The generated site uses the Skyline Communications template and style (based on `internal-docs`).

## Prerequisites

- [DocFX](https://dotnet.github.io/docfx/) installed (`dotnet tool install -g docfx`)
- The **DevPack NuGet** must already be built (provides the API metadata for auto-documentation)
- The **UDAPI** project must exist (provides the OpenAPI spec for Web API docs)
- The **YAML input file** and **SolutionDescription.md** from the model analyzer

## Output Structure

```
<OutputDir>/<SolutionName>Docs/
├── docfx.json
├── toc.yml
├── index.md                          # Landing page (application overview)
├── articles/
│   ├── toc.yml
│   ├── getting-started.md            # Quick-start guide
│   └── architecture.md              # High-level architecture overview
├── devpack/
│   ├── toc.yml
│   ├── index.md                      # Devpack API introduction
│   └── api/                          # Auto-generated from XML docs via docfx metadata
├── webapi/
│   ├── toc.yml
│   ├── index.md                      # UDAPI introduction & authentication
│   ├── endpoints.md                  # Endpoint reference (from openapi.yaml)
│   └── examples.md                   # Request/response examples
├── templates/
│   └── skyline/
│       ├── favicon.ico
│       ├── logo.svg
│       ├── global.json               # { "improveThisDoc": "Propose changes" }
│       ├── layout/
│       │   └── _master.tmpl          # Custom master template
│       └── public/
│           └── main.css              # Skyline branded CSS (light + dark theme)
└── images/
    └── logo.svg
```

## Steps to Implement

### Step 1 — Scaffold the DocFX project

Create the `docfx.json` configuration with:

```json
{
  "build": {
    "content": [
      { "files": ["*.md", "toc.yml"] },
      { "files": ["articles/**.md", "articles/**/toc.yml"] },
      { "files": ["devpack/**.md", "devpack/**/toc.yml"] },
      { "files": ["webapi/**.md", "webapi/**/toc.yml"] }
    ],
    "resource": [
      { "files": ["images/**", "templates/skyline/favicon.ico", "templates/skyline/logo.svg"] }
    ],
    "dest": "_site",
    "globalMetadata": {
      "_appTitle": "<SolutionName> Documentation",
      "_appFooter": "© Skyline Communications",
      "_enableSearch": true,
      "_enableNewTab": true
    },
    "template": ["default", "modern", "templates/skyline"],
    "postProcessors": ["ExtractSearchIndex"]
  }
}
```

### Step 2 — Copy the Skyline template

Replicate the `templates/skyline/` folder from `internal-docs`:

| File | Purpose |
|------|---------|
| `global.json` | Sets "Propose changes" text for edit links |
| `layout/_master.tmpl` | Custom HTML master layout with theme switcher |
| `public/main.css` | Skyline-branded CSS variables (light & dark mode) |
| `favicon.ico` | Skyline favicon |
| `logo.svg` | Skyline logo |

### Step 3 — Generate the landing page (`index.md`)

Use the `SolutionDescription.md` from the model analyzer to create a landing page that explains:

- What the application does (business context)
- Key features / models managed
- Links to the devpack API docs and Web API docs
- Quick-start instructions

### Step 4 — Document the Devpack API (`devpack/`)

Generate API reference documentation from the devpack NuGet library:

1. Add a `metadata` section to `docfx.json` pointing at the devpack `.csproj` or compiled DLL:
   ```json
   "metadata": [{
     "src": [{ "files": ["**/*.csproj"], "src": "../<SolutionName>/<SolutionName>" }],
     "dest": "devpack/api"
   }]
   ```
2. Run `docfx metadata` to extract XML documentation into YAML API files.
3. Create `devpack/index.md` with an introduction explaining:
   - What the devpack provides (models, API helpers, enums)
   - How to install the NuGet package
   - Basic usage patterns (get, create, update, delete via `I{Model}ApiHelper`)

### Step 5 — Document the Web API / UDAPI (`webapi/`)

Generate Web API documentation from the `openapi.yaml` produced by the UDAPI builder:

1. **`webapi/index.md`** — Overview of the UDAPI:
   - Base URL and authentication (bearer token via DataMiner connection info)
   - Content types (JSON)
   - Error handling conventions
2. **`webapi/endpoints.md`** — Per-endpoint reference:
   - HTTP method + route
   - Request body schema
   - Response schema
   - Query parameters (pagination, filtering)
3. **`webapi/examples.md`** — Practical curl/fetch examples for common operations:
   - List all items
   - Get by ID
   - Create new item
   - Update existing item
   - Delete item

### Step 6 — Create the root `toc.yml`

```yaml
- name: Home
  href: index.md
- name: Articles
  href: articles/toc.yml
- name: Devpack API
  href: devpack/toc.yml
- name: Web API (UDAPI)
  href: webapi/toc.yml
```

### Step 7 — Build and verify

```powershell
cd <OutputDir>/<SolutionName>Docs
docfx build docfx.json
docfx serve _site
```

Open `http://localhost:8080` to verify the site renders correctly with the Skyline theme.

## Template Details (from `internal-docs`)

The Skyline template extends DocFX `default` + `modern` with:

- **CSS variables** for light/dark themes (Skyline blue `#0096e7` primary, dark mode background `#2d2d30`)
- **Alert styling** — custom colors for note/tip/warning/caution/important blocks
- **Code highlighting** — themed syntax colors matching Visual Studio color schemes
- **Theme switcher** — localStorage-based auto/light/dark toggle in the master template

## Integration with the Pipeline

This builder runs as **Stage 5** (after frontend generation):

```
Model Analyzer → DevPack Builder → Backend Builder → Frontend Builder → DocFX Builder
```

Inputs consumed:
- `<Domain>Input.yaml` — model definitions for generating API docs structure
- `<Domain>SolutionDescription.md` — business context for the landing page
- `openapi.yaml` — endpoint details for Web API documentation
- DevPack `.csproj` — source for `docfx metadata` API extraction
