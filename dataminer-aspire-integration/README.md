# Stage 5 — Aspire Integration

Adds a local **.NET Aspire** development environment to the generated solution, allowing developers to run and test the DataMiner SDM app without a full DataMiner agent installation.

---

## Responsibility

- Copy reusable Aspire components (AppHost, ApiService, ServiceDefaults, ScriptHost) into a `.aspire/` folder inside the target workspace.
- Auto-detect the UDAPI script name, DLL path, and frontend static files path.
- Write `appsettings.Development.json` with the correct paths for the workspace.
- Build the ScriptHost sidecar (net48 x86).
- Start the Aspire AppHost and confirm the dashboard is accessible.

---

## Architecture

```
Aspire AppHost
├── ApiService (ASP.NET Core, .NET 8/10)
│   ├── POST /API/v1/Json.asmx/ExecuteAutomationScriptWithOutput
│   │       → forwards to ScriptHost via JSON-RPC
│   ├── GET  /auth/login   → sets mock DMAConnection cookie
│   └── Static files       → serves <FrontendPath>/index.html
└── ScriptHost sidecar (.NET Framework 4.8 x86)
    ├── JSON-RPC server
    ├── Loads UDAPI DLL via Assembly.Load(bytes)   (file not locked)
    ├── MockEngine (in-memory DOM state)
    └── dom-state.json  (persists DOM data across restarts)
```

---

## Quick Start

```powershell
# From the SDM workspace root — auto-detects everything:
pwsh C:\AspireSDMIntegration\Add-AspireIntegration.ps1

# With explicit parameters:
pwsh C:\AspireSDMIntegration\Add-AspireIntegration.ps1 `
  -WorkspacePath "C:\MySDMApp" `
  -ScriptName    "MyAppUDAPI" `
  -FrontendPath  "C:\MySDMApp\MyAppFrontend" `
  -UdapiDllPath  "C:\MySDMApp\MyAppUDAPI\MyAppUDAPI\bin\Debug\net48\MyAppUDAPI.dll"

# Start Aspire after setup:
dotnet run --project .aspire/AspireSDMIntegration.AppHost --launch-profile http
```

---

## Configuration

All paths are written to `.aspire/AspireSDMIntegration.ApiService/appsettings.Development.json`:

```json
{
  "ScriptHost": {
    "ExePath": "<WorkspacePath>/.aspire/ScriptHost/bin/Debug/net48/ScriptHost.exe",
    "Scripts": {
      "<ScriptName>": "<UdapiDllPath>"
    }
  },
  "Frontend": {
    "StaticFilesPath": "<FrontendPath>"
  }
}
```

---

## Hot Reload

| Component | Hot reload behaviour |
|-----------|---------------------|
| Frontend (`index.html`) | Edit file → refresh browser (served directly from source) |
| UDAPI DLL | Rebuild project → ScriptHost restarts automatically via `FileSystemWatcher` |
| ScriptHost.exe | Requires stopping Aspire, rebuild, restart |

---

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| .NET 10 SDK | For AppHost and ApiService |
| .NET Framework 4.8 Developer Pack | For ScriptHost sidecar |
| Visual Studio Build Tools | net48 x86 builds |
| PowerShell 7+ (`pwsh`) | For `Add-AspireIntegration.ps1` |

---

## Reference Implementation

```
C:\Users\Tim\source\repos\AspireSDMIntegration\
├── Add-AspireIntegration.ps1        # Setup script (copy into any SDM workspace)
├── src/
│   ├── AspireSDMIntegration.AppHost/     # Aspire orchestrator
│   ├── AspireSDMIntegration.ApiService/  # ASP.NET Core API + static files
│   └── AspireSDMIntegration.ServiceDefaults/
└── testingnet8toframework/
    └── ScriptHost/                       # net48 x86 JSON-RPC sidecar
        ├── Program.cs                    # JSON-RPC server + script executor
        └── MockEngine.cs                 # IEngine with in-memory DOM
```

---

## Agent

This stage is handled by the **Add Aspire DataMiner Integration** custom agent.

---

## TODO / Next Steps

- [ ] Define the agent SKILL.md for this folder
- [ ] Template `Add-AspireIntegration.ps1` so it references a local copy of the Aspire components instead of the external repo
- [ ] Add Azure deployment support (see `AspireSDMIntegration/deployingtoazure/`)
- [ ] Document the JSON-RPC protocol between ApiService and ScriptHost
