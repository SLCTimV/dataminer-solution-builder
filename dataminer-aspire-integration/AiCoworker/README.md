# Skyline.DataMiner.Aspire.AiCoworker

Local AI chat assistant powered by Foundry Local that provides natural-language interaction with DataMiner SDM solutions. Fetches live data from UdapiProxy and supports CRUD operations through server-side orchestration.

## Projects

| Project | Description |
|---------|-------------|
| `Skyline.DataMiner.Aspire.AiCoworker` | The executable web app (net10.0) — chat backend + UI |
| `Skyline.DataMiner.Aspire.AiCoworker.Hosting` | Aspire hosting extension (`AddAiCoworker()`) |

## Architecture

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│   Browser Chat   │────▶│  AI Coworker     │────▶│  Foundry Local  │
│   (port 5190)   │     │  (ASP.NET)       │     │  phi-4-mini     │
└─────────────────┘     │                  │     │  (port 49994)   │
                        │  Reads config    │     └─────────────────┘
                        │  from JSON       │
                        │                  │────▶┌─────────────────┐
                        │  Pre-fetches     │     │  UdapiProxy     │
                        │  live data       │     │  (port 5180)    │
                        └──────────────────┘     └─────────────────┘
```

## Configuration (Option C — Hybrid)

The AI Coworker reads domain-specific context from a generated `aicoworker-config.json`:

```json
{
  "solutionName": "SDMWorldEvent",
  "apiName": "World Event Manager API",
  "apiRoute": "worldeventmanager",
  "systemPromptPrefix": "You are an AI assistant for the World Event Manager...",
  "models": [
    {
      "name": "WorldEvent",
      "properties": [
        { "name": "Name", "type": "string", "description": "Event name" },
        { "name": "Status", "type": "enum", "description": "Current status" }
      ],
      "enumValues": {
        "WorldEventType": ["Basic", "Pro", "Advanced"],
        "WorldEventStatus": ["Requested", "Processing", "Done"]
      }
    }
  ]
}
```

This config is generated per-solution by `New-AspireIntegration.cs` from the YAML domain model.

## Usage in AppHost

```csharp
using Skyline.DataMiner.Aspire.AiCoworker.Hosting;

builder.AddAiCoworker("aicoworker", options =>
{
    options.Port = 5190;
    options.UdapiUrl = "http://localhost:5180";
    options.FoundryEndpoint = "http://127.0.0.1:49994/v1";
    options.FoundryModel = "phi-4-mini-instruct-openvino-gpu:2";
    options.ConfigPath = "./aicoworker-config.json";
});
```

## Prerequisites

| Tool | Notes |
|------|-------|
| Foundry Local | `winget install Microsoft.FoundryLocal` |
| phi-4-mini model | `foundry model run phi-4-mini` (downloads on first use) |

Get the correct model ID with: `foundry models list`

## Packaging

```powershell
cd src\Skyline.DataMiner.Aspire.AiCoworker

# 1. Publish
dotnet publish -c Release

# 2. Pack
dotnet pack -c Release --no-build -o C:\Users\Tim\source\nugets
```

For the hosting extension:

```powershell
cd src\Skyline.DataMiner.Aspire.AiCoworker.Hosting
dotnet pack -c Release -o C:\Users\Tim\source\nugets
```

## Key Design Decisions

### Server-Side CRUD Orchestration

Small models (phi-4-mini) hallucinate identifiers and field values. The AI Coworker uses server-side merge:

1. Model outputs an ACTION block with only the Name and changed fields
2. Server looks up the real record by Name in live data
3. Server merges changed fields onto the real record
4. Server uses the real Identifier for the API call

### No AI SDK — Raw HttpClient

The OpenAI .NET SDK sends schema fields that cause Foundry Local to return 500. Uses raw `HttpClient` with hand-crafted JSON instead.

### Resilience Handler Bypass

Aspire's `AddServiceDefaults()` applies a 30-second timeout via resilience handlers. AI inference takes 30-120+ seconds. The AI Coworker calls `.RemoveAllResilienceHandlers()` on both the Foundry and UdapiProxy `HttpClient` registrations.

## How the hosting extension resolves the exe

`AiCoworkerExtensions.AddAiCoworker()` looks for the executable in this order:

1. `builder.Configuration["AiCoworker:ExePath"]` — explicit override
2. `~/.nuget/packages/skyline.dataminer.aspire.aicoworker/<version>/tools/net10.0/AiCoworker.exe`
