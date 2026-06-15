# Stage 4 — Frontend Builder

Generates a **static DataMiner low-code app** (React + Vite) from the `openapi.yaml` produced by the Backend Builder. The app is deployed inside DataMiner and communicates with the UDAPI via the DataMiner JSON Web Services API.

---

## Responsibility

- Read `openapi.yaml` from the Backend Builder output.
- Scaffold a Skyline-styled React/Vite single-page application.
- Generate CRUD UI components for every resource defined in the spec.
- Call the UDAPI Automation Script via the DataMiner JSON Web Services API (not direct HTTP).
- Produce a build-ready `index.html` + static assets folder deployable inside DataMiner.
- Return the build folder path, `AppId`, and `AppName` to the orchestrator.

---

## Output

```
<AppId>Frontend/
├── index.html          # Entry point (served by DataMiner or ApiService)
├── assets/             # Bundled JS + CSS
└── ...
```

The `AppId` is the domain name without the `SDM` prefix (e.g. `Event` for `SDMEvent`).  
The frontend is accessible at: `http://<dm-host>/public/<AppId>/index.html`

---

## DataMiner Integration Pattern

The frontend does **not** make direct HTTP calls to the UDAPI route.  
Instead it calls:

```
POST /API/v1/Json.asmx/ExecuteAutomationScriptWithOutput
```

with the UDAPI Automation Script name (e.g. `SDMEventUDAPI`) and a JSON request body.  
This is the same endpoint used by the Aspire `ApiService` sidecar.

---

## Agent

This stage delegates to the **DataMiner App Builder** agent.

The `Application Vibe Coder` custom agent handles:
- Greenfield SPA scaffolding (React + Vite)
- Skyline-styled components
- iframe-safe UI patterns
- DataMiner Web Services integration

---

## Reference Implementation

The `SDMEvent` frontend is located at:

```
C:\Users\Tim\source\repos\AspireSDMIntegration\sdmEventExample\EventFrontend\
```

It serves as the canonical example for structure, styling, and API calling patterns.

---

## TODO / Next Steps

- [ ] Define the agent SKILL.md for this folder
- [ ] Document the exact DataMiner JSON Web Services call format
- [ ] Add a reference example `index.html` derived from the SDMEvent frontend
- [ ] Add `.dmapp` packaging step for the frontend (to upload to DataMiner)
