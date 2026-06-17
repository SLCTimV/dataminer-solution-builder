# Adding Foundry Local & AI Coworker to the Aspire Solution

This document describes every step taken to add a local AI chat assistant ("AI Coworker") to the World Event Manager Aspire solution, powered by **Foundry Local** with the **phi-4-mini** model.

---

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | 10.0+ | All Aspire projects target `net10.0` |
| Aspire SDK | 13.4.4 | `Aspire.AppHost.Sdk/13.4.4` |
| Foundry Local | 0.8.119+ | Install via `winget install Microsoft.FoundryLocal` |
| Node.js | 18+ | For the frontend npm dev server |

### Verify Foundry Local is running

```bash
foundry models list            # should show available models
foundry model run phi-4-mini   # downloads & starts phi-4-mini if not cached
```

Foundry Local serves an OpenAI-compatible API at `http://127.0.0.1:49994/v1`.

---

## Step 1: Add the Foundry Hosting Package to AppHost

Add the preview Aspire Foundry hosting NuGet package to the AppHost project:

```xml
<!-- SDMWorldEvent.AppHost.csproj -->
<PackageReference Include="Aspire.Hosting.Foundry" Version="13.4.0-preview.1.26281.18" />
```

> This is a preview package. The API may change in future Aspire releases.

---

## Step 2: Register Foundry Local in AppHost.cs

Add the Foundry resource and model deployment to `AppHost.cs`:

```csharp
using Aspire.Hosting.Foundry;

// AI: Foundry Local with Phi-4-mini for local AI assistant
var foundry = builder.AddFoundry("foundry")
    .RunAsFoundryLocal();

var chat = foundry.AddDeployment("chat", FoundryModel.Local.Phi4Mini);
```

This tells Aspire to manage Foundry Local as a resource visible in the Aspire Dashboard.

---

## Step 3: Create the AI Coworker Web Project

Create a new ASP.NET minimal API project alongside the existing Aspire projects:

```
SDMWorldEvent.AiCoworker/
├── Program.cs
├── SDMWorldEvent.AiCoworker.csproj
└── wwwroot/
    └── index.html
```

### 3a. Project File (`SDMWorldEvent.AiCoworker.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <ProjectReference Include="..\SDMWorldEvent.ServiceDefaults\SDMWorldEvent.ServiceDefaults.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <NoWarn>$(NoWarn);EXTEXP0001</NoWarn>
  </PropertyGroup>

</Project>
```

Key points:
- References `ServiceDefaults` for Aspire telemetry/health checks
- **No AI NuGet packages needed** — uses raw `HttpClient` to call Foundry's OpenAI-compatible API
- `EXTEXP0001` suppression required for `RemoveAllResilienceHandlers()` (see gotchas)

### 3b. Backend (`Program.cs`)

The backend has three responsibilities:

1. **Fetch live data** from UdapiProxy on every chat request
2. **Inject data into the system prompt** so the model always has context
3. **Forward to Foundry Local** for AI inference and return the response

```csharp
// Two named HttpClients
builder.Services.AddHttpClient("udapi", client => {
    client.BaseAddress = new Uri(udapiUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
}).RemoveAllResilienceHandlers();  // CRITICAL: bypass 30s resilience timeout

builder.Services.AddHttpClient("foundry", client => {
    client.BaseAddress = new Uri(foundryEndpoint.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(180);
}).RemoveAllResilienceHandlers();  // CRITICAL: bypass 30s resilience timeout
```

The chat endpoint:
1. Fetches all world events from UdapiProxy (`GET /worldeventmanager`)
2. Builds a system prompt with the data model description + live JSON data
3. Sends the full conversation to Foundry's `POST /v1/chat/completions`
4. If the model outputs an `ACTION` block (CREATE/UPDATE/DELETE), the server executes it against UdapiProxy with server-side validation, then asks the model to summarize the result
5. Returns the model's text response to the browser

### 3c. Frontend (`wwwroot/index.html`)

A self-contained single-page chat UI with:
- Dark Skyline-themed design (`--bg: #14171e`, `--accent: #00b4d8`)
- Chat bubbles for user and assistant messages
- Markdown rendering via `marked.js` CDN
- Thinking animation dots while waiting for AI response
- Health check indicator (polls `/health` every 15s)
- 180-second fetch timeout (`AbortSignal.timeout(180_000)`)

---

## Step 4: Wire the AI Coworker into AppHost

### 4a. Add the project reference to AppHost.csproj

```xml
<ProjectReference Include="..\SDMWorldEvent.AiCoworker\SDMWorldEvent.AiCoworker.csproj" />
```

### 4b. Register the resource in AppHost.cs

```csharp
builder.AddProject<Projects.SDMWorldEvent_AiCoworker>("aicoworker")
    .WithEnvironment("UdapiProxy__Url", "http://localhost:5180")
    .WithEnvironment("Foundry__Endpoint", "http://127.0.0.1:49994/v1")
    .WithEnvironment("Foundry__Model", "phi-4-mini-instruct-openvino-gpu:2")
    .WithHttpEndpoint(port: 5190)
    .WithExternalHttpEndpoints();
```

The AI Coworker runs on port **5190** and connects to:
- **UdapiProxy** on port 5180 for world events CRUD
- **Foundry Local** on port 49994 for AI inference

---

## Step 5: Start the Solution

```bash
cd SDMWorldEventAspire
dotnet run --project SDMWorldEvent.AppHost --launch-profile http \
    -- --UdapiProxy:ExePath="<path-to-UdapiProxy.exe>"
```

Then open:
- **AI Coworker**: http://localhost:5190
- **Aspire Dashboard**: http://localhost:15146

---

## Architecture

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│                  │     │                  │     │                 │
│   Browser Chat   │────▶│  AI Coworker     │────▶│  Foundry Local  │
│   (port 5190)   │     │  (ASP.NET)       │     │  phi-4-mini     │
│                  │     │                  │     │  (port 49994)   │
└─────────────────┘     │                  │     └─────────────────┘
                        │  Pre-fetches     │
                        │  live data ──────│────▶┌─────────────────┐
                        │                  │     │  UdapiProxy     │
                        └──────────────────┘     │  (port 5180)    │
                                                 └─────────────────┘
```

Every chat request:
1. User sends message → AI Coworker backend
2. Backend fetches current world events from UdapiProxy
3. Backend builds system prompt with live data + conversation history
4. Backend calls Foundry Local's chat completions API
5. If the model outputs an ACTION block, the server executes it (see CRUD flow below)
6. Model response returned to browser, rendered as markdown

---

## CRUD Operations (Server-Side Orchestration)

The AI Coworker supports full **Create, Read, Update, Delete** operations on world events. Because phi-4-mini cannot reliably produce structured tool calls (see Gotcha #5), mutations use a **server-side orchestration** pattern:

### How it works

1. The system prompt instructs the model to output a JSON `ACTION` block when the user requests a mutation
2. The model outputs something like:
   ```json
   {"ACTION":"UPDATE","DATA":{"Name":"Summer Music Festival","Status":"Done"}}
   ```
3. The server **parses** the ACTION block from the model's text output
4. The server **validates** the action against live data (finds the real event by Name)
5. For **UPDATE**: the server merges the model's partial changes onto the real event object (preserving Identifier, Languages, etc.)
6. For **DELETE**: the server resolves the real Identifier from the event name
7. The server executes the CRUD call against UdapiProxy
8. The server re-fetches updated data and asks the model to summarize the result

### Why server-side merge?

Small models like phi-4-mini hallucinate field values — they invent fake Identifiers and omit fields when asked to provide a full object. By having the server:
- Look up the real event by Name (fuzzy matching)
- Merge only the changed fields onto the real event
- Use the real Identifier for the API call

...we get reliable CRUD without depending on the model to produce exact data.

### Supported actions

| Action | Model outputs | Server does |
|--------|--------------|-------------|
| **READ** | Formats live data as markdown tables | N/A (data already injected) |
| **UPDATE** | `{"ACTION":"UPDATE","DATA":{"Name":"...","Status":"Done"}}` | Finds event by name, merges fields, PUTs full object |
| **CREATE** | `{"ACTION":"CREATE","DATA":{"Name":"...","Description":"...",...}}` | POSTs the new event directly |
| **DELETE** | `{"ACTION":"DELETE","DATA":{"Name":"..."}}` | Finds event by name, DELETEs by real Identifier |

---

## Gotchas & Lessons Learned

### 1. Model ID — Use the Full Identifier

The alias `"phi-4-mini"` does **not** work as the model ID in API calls. Foundry Local returns `400 Bad Request: No OpenAIService provider found`. You must use the full model identifier:

```
phi-4-mini-instruct-openvino-gpu:2
```

Get the correct ID with: `foundry models list`

### 2. OpenAI .NET SDK is Incompatible with Foundry Local

The official `OpenAI` NuGet package (v2.x) sends extra JSON schema fields in tool definitions (`strict`, `additionalProperties`, `required` on nested objects) that cause Foundry Local to return `HTTP 500`. 

**Solution**: Use raw `HttpClient` with hand-crafted JSON payloads instead of any SDK.

### 3. Aspire ServiceDefaults Resilience Handler — 30-Second Timeout

`AddServiceDefaults()` registers a **standard resilience handler** via `ConfigureHttpClientDefaults` that applies a **30-second total request timeout** to all `HttpClient` instances. This overrides any `HttpClient.Timeout` you set.

For AI inference (which can take 30–120 seconds), this causes `TaskCanceledException` with the message:
```
The operation didn't complete within the allowed timeout of '00:00:30'
```

**Solution**: Call `.RemoveAllResilienceHandlers()` on HttpClient registrations that need longer timeouts:

```csharp
builder.Services.AddHttpClient("foundry", client => {
    client.Timeout = TimeSpan.FromSeconds(180);
}).RemoveAllResilienceHandlers();
```

This requires suppressing `EXTEXP0001` in the csproj:
```xml
<NoWarn>$(NoWarn);EXTEXP0001</NoWarn>
```

### 4. HttpClient BaseAddress Must End with `/`

When using relative URIs with `HttpClient.PostAsync("chat/completions", ...)`, the `BaseAddress` must end with a trailing slash. Otherwise `new Uri(baseAddress, relativeUri)` drops the last path segment:

```csharp
// WRONG: PostAsync sends to http://127.0.0.1:49994/chat/completions (missing /v1/)
client.BaseAddress = new Uri("http://127.0.0.1:49994/v1");

// CORRECT: PostAsync sends to http://127.0.0.1:49994/v1/chat/completions
client.BaseAddress = new Uri("http://127.0.0.1:49994/v1/");
```

### 5. Tool Calling Doesn't Work Reliably with phi-4-mini

With `tool_choice: "auto"`, phi-4-mini writes tool calls as **text content** (e.g., `` ```getWorldEvents()``` ``) instead of emitting structured `tool_calls` in the API response. Only `tool_choice: "required"` forces actual tool calls, but that breaks non-data queries.

**Solution**: Don't rely on the model for tool calling. Instead, use **server-side data injection**:
- Always pre-fetch data from UdapiProxy before calling the model
- Inject the live data into the system prompt as JSON
- Let the model format/summarize the data in its response

This approach is far more reliable with small local models.

### 6. Frontend Fetch Needs a Long Timeout

The default browser `fetch()` has no explicit timeout, but users see a spinning indicator. Add an `AbortSignal` timeout to match the backend:

```javascript
const response = await fetch('/api/chat', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ messages: history }),
    signal: AbortSignal.timeout(180_000),  // 3 minutes
});
```

### 7. Small Models Hallucinate Identifiers and Field Values

When asked to produce a full event JSON for an update, phi-4-mini invents fake GUIDs and omits fields — even when the correct data is right there in the system prompt. This caused updates to create orphaned records with null fields.

**Solution**: Use **server-side merge** — the model only needs to specify the event Name and which fields to change. The server looks up the real event, merges the changes onto it, and uses the real Identifier for the API call. See the "CRUD Operations" section above.

---

## Files Changed / Created

| File | Action | Purpose |
|------|--------|---------|
| `SDMWorldEvent.AppHost/SDMWorldEvent.AppHost.csproj` | Modified | Added `Aspire.Hosting.Foundry` package + AiCoworker project reference |
| `SDMWorldEvent.AppHost/AppHost.cs` | Modified | Added Foundry Local resource + AI Coworker resource registration |
| `SDMWorldEvent.AiCoworker/SDMWorldEvent.AiCoworker.csproj` | **Created** | Minimal web project with ServiceDefaults reference |
| `SDMWorldEvent.AiCoworker/Program.cs` | **Created** | Chat endpoint, Foundry client, UdapiProxy data fetching |
| `SDMWorldEvent.AiCoworker/wwwroot/index.html` | **Created** | Self-contained dark-themed chat UI |
