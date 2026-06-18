---
name: dataminer-assistant-mdfiles
description: >
  Agent-guided creation of DataMiner Assistant context files: ad-hoc data source
  descriptions, script tool descriptions, skills (one per user flow), and agents
  (one per user role). Uses the input YAML, OpenAPI spec, UserFlows.md, and
  UserRoles.md as sources. Produces files in the backend installer's SetupContent
  directory for deployment to the DataMiner Assistant.
  USE FOR: "create assistant md files", "generate skills and agents", "create
  assistant context files", "generate ad-hoc descriptions", "generate script tool",
  Stage 3 Step 4 of the SDM pipeline.
  DO NOT USE FOR: creating the backend solution (use dataminer-backend-builder),
  generating the UDAPI or GQI code (those are earlier steps), deploying to DataMiner.
argument-hint: >
  Provide the path to the input YAML file and the output directory. The UDAPI must
  already be built (openapi.yaml must exist). UserRoles.md and UserFlows.md must
  exist in the YAML directory (produced by the model analyzer).
---

# Assistant MD Files Builder Skill

## Purpose

Create the DataMiner Assistant context files that enable the AI assistant to
interact with the solution's data and APIs. This step is **agent-guided** — the
agent reads the source materials and authors rich, context-aware descriptions
rather than generating formulaic templates.

## Prerequisites

Before running this step, these must exist:

| Artifact | Produced By | Location |
|----------|-------------|----------|
| Input YAML | Model Analyzer | `<yamlDir>/<Domain>Input.yaml` |
| OpenAPI spec | UDAPI Builder (Step 2) | `<backendDir>/<Name>UDAPI/bin/Debug/net48/openapi/openapi.yaml` |
| UserRoles.md | Model Analyzer (Step 7) | `<yamlDir>/<Domain>UserRoles.md` |
| UserFlows.md | Model Analyzer (Step 8) | `<yamlDir>/<Domain>UserFlows.md` |
| Backend Package project | Backend Installer (Step 5) | `<backendDir>/<Name>Backend.Package/` |

## Output Location

All files go into the backend package's SetupContent directory:

```
<backendDir>/<Name>Backend.Package/SetupContent/
├── adhocs/                              ← One .md per GQI data source
│   ├── get<model>s.md
│   └── get<subobject>s.md
├── scripts/                             ← One .md for the UDAPI script tool
│   └── <Name>UDAPI.md
├── skills/                              ← One folder per user flow
│   ├── <name>-<flow-slug>/
│   │   └── SKILL.md
│   └── ...
└── agents/                              ← One folder per user role
    ├── <guid>/
    │   └── agent.md
    └── ...
```

## Naming & Size Constraints

These constraints are enforced by the DataMiner Assistant platform:

| Rule | Applies To | Constraint |
|------|-----------|------------|
| Name format | All | Lowercase, letters/digits/hyphens only. Cannot start/end with hyphen. No `--`. |
| File size | Ad-hoc & Script tools | **Max 8192 characters** per file |
| Skill name | Skills | Max **64 characters** |
| Skill description | Skills | Max **1024 characters** |
| Agent name | Agents | Max **128 characters** |
| Agent description | Agents | Max **1024 characters** |
| Agent instructions | Agents | Max **32768 characters** |
| Skill folder | Skills | Folder name **must match** the skill `name` field |

## Pipeline (6 Steps)

### Step 1: Scaffold Directories

Run the scaffolder script to create the directory structure:

```powershell
dotnet run dataminer-backend-builder/dataminer-assistant-mdfiles/New-AssistantMdFiles.cs -- \
  -i <yaml> -o <output-dir>
```

This creates the `SetupContent/adhocs/`, `scripts/`, `skills/`, and `agents/`
directories and a stub `AboutThisFolder.md`.

### Step 2: Create Ad-Hoc Data Source Files

Create **one `.md` file per GQI data source** (one per model + one per sub-object).

**Source material**: Input YAML (model properties, types) + OpenAPI spec (for filtering hints).

**YAML frontmatter format**:

```yaml
---
name: "<SolutionName>.Get <PluralModel>"
description: "<What this data source returns>"
columns:
  - name: "<ColumnName>"
    type: "<String|DateTime|Int|Double|Boolean>"
    description: "<What this column contains>"
inputArguments:
  - name: "FilterRequest"
    type: "String"
    description: "OData filter expression"
    example: ""
---
```

**Body content**: Explain what the data source returns, list filterable fields, and
describe any relevant OData filter patterns. Reference the OpenAPI spec for route info.

**Sub-object adhocs**: Include an `Identifier` input argument for the parent object's
GUID. Explain that the parent identifier is required.

**Example** (from working deployment):

```markdown
---
name: GetEvents
description: 'Is able to list all events'
columns:
- name: Identifier
  type: string
- name: Name
  type: string
- name: Start
  type: date
inputArguments:
- name: FilterRequest
  type: string
  description: ''
  example: ''
---

gets all events
```

### Step 3: Create Script Tool File

Create **one `.md` file** for the UDAPI script tool.

**Source material**: Input YAML (models, enums, sub-objects) + OpenAPI spec (routes, methods).

**YAML frontmatter format**:

```yaml
---
name: <udapiProjectName>
description: "Script to execute create, update and delete actions on <API name>."
scriptName: <udapiProjectName>
sync: true
inputArguments:
  - name: "ApiTriggerInput"
    description: "<explanation of the ApiTriggerInput structure>"
    example: '<JSON example of a PUT request>'
---
```

**Body content must include**:
1. **ApiTriggerInput explanation** — RequestMethod enum (1=GET, 2=PUT, 3=POST, 4=DELETE),
   Route, RawBody, QueryParameters
2. **Model reference table** — fields, types, and descriptions for each model
3. **Available routes** — list each model's CRUD routes

**Example** (from working deployment):

```markdown
---
name: sdmeventudapi
description: Script to execute create, update and delete actions on an event.
scriptName: sdmeventudapi
sync: true
inputArguments:
- name: ApiTriggerInput
  example: '{"RequestMethod":2,"Route":"eventmanager","RawBody":"{...}","Parameters":{},"Context":{"TokenId":"00000000-0000-0000-0000-000000000000"},"QueryParameters":{}}'
---

## ApiTriggerInput Explanation
...

## Event Model
| Field | Type | Description |
|-------|------|-------------|
| Name | string | Event name |
...
```

### Step 4: Create Skills (One Per User Flow)

Create **one skill folder per user flow** from `UserFlows.md`.

**Source material**: `UserFlows.md` (flow name, actor, steps, outcomes) + Input YAML
(model fields for context) + the adhoc/script tool names from Steps 2-3.

**For each flow**:
1. Derive a skill name: `<solutionname>-<flow-slug>` (e.g. `sdmworldevent-request-new-event`)
2. Create folder: `skills/<skill-name>/`
3. Create `SKILL.md` inside

**YAML frontmatter format**:

```yaml
---
name: <skill-name>
description: "<1-2 sentence description of what this skill covers, max 1024 chars>"
---
```

**Body content must include**:
1. **When to use** — triggers that activate this skill (based on the flow's trigger)
2. **Steps** — the user flow steps adapted as assistant instructions
3. **Which tools to use** — reference the specific adhoc data source and/or script tool
4. **Confirmation rules** — when the flow involves mutations, require user confirmation
5. **Example interactions** — 2-3 example user prompts and how the assistant should respond

**Example**:

```markdown
---
name: sdmworldevent-request-new-event
description: Guides users through creating a new world event with proper validation
---

## When To Use

When a user asks to create a new event, schedule an event, or add an event.

## Steps

1. Ask for event details: name, description, start/end dates, type
2. Validate that end date is after start date
3. Show the event details in a summary table
4. Ask for confirmation before creating
5. Call the SDMWorldEventUDAPI script with POST to create the event
6. Show the created event with its new identifier

## Tools

- **SDMWorldEvent.Get WorldEvents** data source — to check for duplicate names
- **SDMWorldEventUDAPI** script — to POST the new event

## Example Interactions

**User**: "Create an event called Summer Gala on July 15th"
→ Ask for missing fields (description, end time, type), then confirm and create.
```

### Step 5: Create Agents (One Per User Role)

Create **one agent folder per user role** from `UserRoles.md`.

**Source material**: `UserRoles.md` (role name, description, permissions) + the skill
names from Step 4 + the tool names from Steps 2-3.

**For each role**:
1. Generate a GUID folder name
2. Create folder: `agents/<guid>/`
3. Create `agent.md` inside

**YAML frontmatter format**:

```yaml
---
name: "<SolutionName> <RoleName>"
description: "<What this agent does, tailored to the role, max 1024 chars>"
tools:
  - <udapi-script-name>
  - "<adhoc-tool-name-1>"
  - "<adhoc-tool-name-2>"
skills:
  - <skill-name-1>
  - <skill-name-2>
---
```

**Tool/skill selection per role**:
- Map the role's **permissions** to the relevant tools and skills
- A read-only role (e.g. Viewer) should only reference adhoc data sources and read-focused skills
- A full-access role (e.g. Admin) gets all tools and all skills
- Match each role to the flows that role can perform (from UserFlows.md actors)

**Body content**: Brief instructions describing the agent's personality, what it can do,
and any restrictions based on the role's permissions.

**Example** (from working deployment):

```markdown
---
name: Event Agent
description: You're an event manager that can give information about events and create events
tools:
- sdmeventudapi
- GetEvents
skills:
- event-manager-skill
---

You're an agent that answers questions about events.

- List the events, create events update and delete events
```

### Step 6: Review & Validate

After creating all files, review each one:

1. **Size check**: Verify ad-hoc and script files are ≤ 8192 characters. If over,
   trim the body content (keep the YAML frontmatter intact).
2. **Name validation**: Verify all `name` fields follow the naming rules (lowercase,
   letters/digits/hyphens, no leading/trailing hyphens, no `--`).
3. **Skill folder match**: Verify each skill folder name matches its `name` field.
4. **Tool references**: Verify each agent's `tools` and `skills` arrays reference
   files that actually exist in the SetupContent.
5. **Usefulness check**: For each file, assess:
   - Does the description add value beyond what the YAML frontmatter already conveys?
   - Are the example interactions realistic and helpful?
   - Would an LLM be able to use this file to correctly assist a user?
   - If a file is not useful, either improve it or remove it.
6. **Coverage check**: Ensure every user flow has a skill and every user role has an agent.

Present a summary table to the user:

```
| Type   | Name                                  | Size  | Status |
|--------|---------------------------------------|-------|--------|
| Adhoc  | SDMWorldEvent.Get WorldEvents         | 1.4KB | ✓ OK   |
| Script | SDMWorldEventUDAPI                    | 1.7KB | ✓ OK   |
| Skill  | sdmworldevent-request-new-event       | 2.1KB | ✓ OK   |
| Skill  | sdmworldevent-configure-audio         | 1.8KB | ✓ OK   |
| Agent  | SDMWorldEvent Admin                   | 1.2KB | ✓ OK   |
| Agent  | SDMWorldEvent Event Manager           | 1.0KB | ✓ OK   |
```

Ask the user to approve or request changes before proceeding.

## Complete Example Output

For a WorldEvent solution with 4 roles and 5 flows:

```
SetupContent/
├── adhocs/
│   ├── getworldevents.md              ← WorldEvent model
│   └── getlanguages.md                ← Language sub-object
├── scripts/
│   └── SDMWorldEventUDAPI.md          ← UDAPI script tool
├── skills/
│   ├── sdmworldevent-request-new-event/
│   │   └── SKILL.md                   ← Flow 1: Request a New Event
│   ├── sdmworldevent-configure-audio/
│   │   └── SKILL.md                   ← Flow 2: Configure Audio Languages
│   ├── sdmworldevent-progress-lifecycle/
│   │   └── SKILL.md                   ← Flow 3: Progress Event Through Lifecycle
│   ├── sdmworldevent-review-dashboard/
│   │   └── SKILL.md                   ← Flow 4: Review Event Dashboard
│   └── sdmworldevent-edit-cancel/
│       └── SKILL.md                   ← Flow 5: Edit or Cancel an Event
└── agents/
    ├── <guid-1>/
    │   └── agent.md                   ← Admin (all tools, all skills)
    ├── <guid-2>/
    │   └── agent.md                   ← Event Manager (CRUD tools, flows 1/3/5)
    ├── <guid-3>/
    │   └── agent.md                   ← Audio Engineer (CRUD tools, flow 2)
    └── <guid-4>/
        └── agent.md                   ← Viewer (adhoc only, flow 4)
```
