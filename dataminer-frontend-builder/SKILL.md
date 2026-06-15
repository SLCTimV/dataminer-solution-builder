---
name: dataminer-frontend-builder
description: >
  Generates a DataMiner frontend (React + Vite SPA) for an SDM domain by delegating
  to the DataMiner App Builder agent. Produces a ready-to-build frontend project
  placed in <OutputDir>/<SolutionName>Frontend/.
  USE FOR: "generate frontend", "create UI for DataMiner solution", "build the
  frontend app", Stage 4 of the SDM pipeline.
  DO NOT USE FOR: backend generation (use Backend Builder), model analysis (use
  Model Analyzer), deploying to DataMiner (use Aspire integration).
argument-hint: "Provide --input-yaml and --output-dir, or describe the domain."
disable-model-invocation: false
allowed-tools:
  - Read
  - Grep
  - Glob
  - Edit
  - PowerShell
---

You are the **DataMiner Frontend Builder**. Your job is to produce a complete
React + Vite single-page application that serves as the management UI for an SDM
solution. You delegate the actual code generation to the **DataMiner App Builder**
agent by collecting and providing the right context files.

---

## Inputs

You need the following files (all produced by earlier pipeline stages):

| File | Source | Purpose |
|------|--------|---------|
| `<Domain>Input.yaml` | Model Analyzer | Domain model definitions (models, enums, sub-objects, refs) |
| `<Domain>SolutionDescription.md` | Model Analyzer | Human-readable solution overview, business rules, relationships |
| `openapi.yaml` | UDAPI Builder | Full OpenAPI spec with all routes, request/response schemas |
| `SetupContent/adhocs/*.md` | Assistant MD Files Builder | GQI ad-hoc data source descriptions |
| `SetupContent/scripts/*.md` | Assistant MD Files Builder | UDAPI script description |
| `SetupContent/skills/<name>/SKILL.md` | Assistant MD Files Builder | Skill definition for the solution |

---

## Output Structure

```
<OutputDir>/
└── <SolutionName>Frontend/
    ├── index.html
    ├── package.json
    ├── vite.config.js
    └── src/
        ├── main.jsx
        ├── App.jsx
        ├── api.js
        ├── components/
        │   ├── LoginPage.jsx
        │   ├── <Model>View.jsx        (one per main model)
        │   ├── <Model>Modal.jsx        (create/edit modal per model)
        │   ├── FilterPanel.jsx
        │   └── Navigation.jsx          (sidebar/tabs for multi-model)
        └── styles/
            └── app.css
```

The output folder is placed **next to** the `<SolutionName>` and `<SolutionName>Backend` folders.

---

## Architecture Rules

### 1. DataMiner JSON Web Services — NOT direct HTTP

The frontend does **not** make direct HTTP calls to the UDAPI route.
Instead, all API calls go through:

```
POST /API/v1/Json.asmx/ExecuteAutomationScriptWithOutput
```

with the UDAPI Automation Script name (e.g., `SDMWorldEventUDAPI`) and an `ApiTriggerInput` parameter.

### 2. api.js Pattern

The `api.js` file must follow this exact pattern:

```javascript
const JSON_API = `${window.location.protocol}//${window.location.host}/API/v1/Json.asmx`;
const STORAGE_KEY = 'dmConnection';
const SCRIPT_NAME = '<SolutionName>UDAPI';  // e.g. 'SDMWorldEventUDAPI'
const SCRIPT_FOLDER = '';

// Auth functions: login(), restoreSession(), logout()
// Uses ConnectAppAndInfo for login, GetSecurityInfo for session restore

// UDAPI wrapper: callUdapiScript(connection, requestMethod, rawBody, queryParameters)
// requestMethod: 1=GET, 3=POST, 4=PUT, 5=DELETE
// Reads ScriptOutput key 'ApiTriggerOutput', parses ResponseCode + ResponseBody

// Per-model CRUD functions:
// get<Models>(connection, filter?) → callUdapiScript(connection, 1, '', queryParams)
// create<Model>(connection, data) → callUdapiScript(connection, 3, JSON.stringify(data))
// update<Model>(connection, data) → callUdapiScript(connection, 4, JSON.stringify(data))
// delete<Model>(connection, id) → callUdapiScript(connection, 5, '', {id})
```

### 3. ExecuteAutomationScriptWithOutput Payload

```javascript
{
  connection,
  script: {
    __type: 'Skyline.DataMiner.Web.Common.v1.DMAAutomationScript',
    Name: SCRIPT_NAME,
    Folder: SCRIPT_FOLDER,
    Parameters: [{
      __type: 'Skyline.DataMiner.Web.Common.v1.DMAAutomationScriptParameter',
      ParameterId: 2,
      Name: 'ApiTriggerInput',
      Value: JSON.stringify({
        RequestMethod: requestMethod,  // 1=GET, 3=POST, 4=PUT, 5=DELETE
        Route: '<apiRoute>',           // from solution config
        RawBody: rawBody,
        Parameters: {},
        Context: { TokenId: '00000000-0000-0000-0000-000000000000' },
        QueryParameters: queryParameters,
      }),
    }],
    Dummies: [],
    MemoryFiles: [],
    Settings: {
      __type: 'Skyline.DataMiner.Web.Common.v1.DMAAutomationScriptSettings',
      RequireInteractive: false,
      HasFindInteractiveClient: false,
    },
  },
  scriptOptions: {
    __type: 'Skyline.DataMiner.Web.Common.v1.DMAAutomationScriptOptions',
    WaitForScript: true,
    CheckSets: true,
    LockElements: false,
    ForceLockElements: false,
    WaitWhenLocked: true,
    IsInUse: false,
    AskForConfirmation: false,
    GenerateStartedInfoEvent: true,
    customSuccessMessage: null,
    hideSuccessPopup: true,
    skipPresetsIfComplete: true,
    hidePresets: true,
    popupIsMinimizable: false,
    popupType: 1,
    ClientTimeZone: { Type: 0, Info: null },
  },
}
```

### 4. Response Parsing

```javascript
const outputEntry = result?.ScriptOutput?.find(o => o.Key === 'ApiTriggerOutput');
const parsed = JSON.parse(outputEntry.Value);
// parsed.ResponseCode (200, 201, 404, etc.)
// parsed.ResponseBody (JSON string of the actual data)
return JSON.parse(parsed.ResponseBody);
```

### 5. Styling — Skyline Dark Theme

Use CSS custom properties matching the Skyline DataMiner dark palette:

```css
:root {
  --bg:            #14171e;
  --bg-surface:    #1e2230;
  --bg-card:       #252b3b;
  --bg-input:      #1a1f2d;
  --border:        #303650;
  --border-focus:  #00b4d8;
  --text:          #e2e8f0;
  --text-muted:    #8892a4;
  --accent:        #00b4d8;
  --accent-hover:  #0090b5;
  --danger:        #e53e3e;
  --success:       #2ec4b6;
  --warning:       #f6ad55;
}
```

### 6. UI Components

For each main model, generate:

- **ListView** — Table with sortable columns, infinite scroll, filter panel, create/edit/delete actions
- **Modal** — Create/Edit form with proper field types:
  - `string` → text input
  - `DateTime` → datetime-local input
  - `bool` → checkbox
  - `enum` → select dropdown with enum values
  - `ref` → select dropdown populated by fetching the referenced model's list
  - `lists` (sub-objects) → inline editable table within the modal
- **Navigation** — For multi-model apps, a sidebar or tab bar to switch between model views
- **LoginPage** — Username/password form using DataMiner ConnectAppAndInfo

### 7. Vite Configuration

```javascript
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  base: './',
  build: { outDir: 'dist', emptyOutDir: true },
});
```

### 8. Package.json

```json
{
  "name": "<solution-name-lowercase>-frontend",
  "version": "1.0.0",
  "private": true,
  "scripts": {
    "dev": "vite",
    "build": "vite build",
    "preview": "vite preview"
  },
  "dependencies": {
    "react": "^18.3.1",
    "react-dom": "^18.3.1"
  },
  "devDependencies": {
    "@vitejs/plugin-react": "^4.3.1",
    "vite": "^5.4.0"
  }
}
```

---

## Delegation to DataMiner App Builder Agent

When invoked, collect the input files listed above and pass them to the
**DataMiner App Builder** agent with the following prompt structure:

```
Create a DataMiner frontend application with the following specifications:

## Solution Info
- Solution Name: <SolutionName>
- UDAPI Script Name: <SolutionName>UDAPI
- API Route: <apiRoute>
- App Title: <ApiName or human-friendly title>
- Output Directory: <OutputDir>/<SolutionName>Frontend

## Solution Description
<contents of SolutionDescription.md>

## OpenAPI Specification
<contents of openapi.yaml>

## Data Source Descriptions
<contents of each adhoc/*.md file>

## Script Description
<contents of scripts/*.md file>

## Architecture Requirements
- Use the DataMiner JSON Web Services API pattern (ExecuteAutomationScriptWithOutput)
- Script name: <SolutionName>UDAPI
- API route: <apiRoute>
- Follow Skyline dark theme CSS variables
- React + Vite SPA with no external UI libraries
- One view component per main model with full CRUD
- Login via ConnectAppAndInfo
- Enum fields as dropdowns, ref fields as async-loaded dropdowns
- Sub-object lists as inline editable tables in modals
- Sortable table columns with infinite scroll
- Filter panel with OData-style filter strings

## Reference Pattern
The api.js must use the exact ExecuteAutomationScriptWithOutput payload format
documented in the skill. RequestMethod codes: 1=GET, 3=POST, 4=PUT, 5=DELETE.
```

---

## Running the Builder

### Full Pipeline (3 steps)

#### Step 1 — Collect context (New-UiBuilder.cs)

```bash
dotnet run dataminer-frontend-builder/dataminer-ui-builder/New-UiBuilder.cs -- \
  --input-yaml <path-to-input.yaml> \
  --output-dir <output-directory> \
  [--solution-description <path-to-SolutionDescription.md>]
```

This script:
1. Reads the input YAML to determine solution name, models, api route, etc.
2. Locates the Backend output folder for `openapi.yaml` and `SetupContent/` files
3. Collects all context files into a structured `AGENT_PROMPT.md`
4. Writes the prompt to `<OutputDir>/<SolutionName>Frontend/AGENT_PROMPT.md`

#### Step 2 — Generate frontend code (DataMiner App Builder agent)

Invoke the **DataMiner App Builder** agent with the contents of `AGENT_PROMPT.md`.
The agent generates the full React + Vite SPA source code in `<OutputDir>/<SolutionName>Frontend/`.

After the agent completes:
- Run `npm install` in the frontend folder
- Run `npm run build` to produce the `dist/` output

#### Step 3 — Package the frontend (New-UiInstaller.cs)

```bash
dotnet run dataminer-frontend-builder/dataminer-ui-installer/New-UiInstaller.cs -- \
  --input-yaml <path-to-input.yaml> \
  --output-dir <output-directory>
```

This script:
1. Creates a new DataMiner package project (`<SolutionName>Frontend.Package/`)
2. Copies `dist/*` into `PackageContent/CompanionFiles/Skyline DataMiner/Webpages/Public/<SolutionName>/`
3. Builds the package project

The resulting `.dmapp` deploys the frontend to `http://<dm-host>/public/<SolutionName>/index.html`.

### Re-packaging after changes

If the frontend code is modified after initial generation:
1. Run `npm run build` in the frontend folder to regenerate `dist/`
2. Re-run `New-UiInstaller.cs` — it cleans and re-copies the build output to the package

---

## Reference Implementation

The canonical example frontend is at:

```
C:\Users\Tim\source\repos\DataMinerFrontEnd\event-manager\
```

This demonstrates:
- Skyline dark theme styling
- DataMiner Web Services API integration
- CRUD operations via ExecuteAutomationScriptWithOutput
- Login/session management
- Sortable table with infinite scroll
- Create/edit modals with enum dropdowns and sub-object lists
- Filter panel
