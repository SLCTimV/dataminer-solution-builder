---
name: dataminer-udapi-tester
description: >
  Writes and runs k6 smoke/load tests and Schemathesis fuzz tests for a DataMiner UDAPI endpoint.
  Verifies against Aspire (smoke only — load testing is unnecessary for local mock) and deployed
  DataMiner (full suite). Tests use pure REST against the UDAPI proxy endpoint.
  USE FOR: "write API tests", "test the UDAPI", "run smoke test", "verify CRUD via REST",
  "load test the API", "fuzz test against openapi".
  DO NOT USE FOR: frontend/browser testing (use dataminer-frontend-tester), writing the UDAPI
  itself (use dataminer-backend-builder), connector testing (use dataminer-unit-testing).
argument-hint: >
  Provide the test folder path and target URL. Example:
  "run smoke tests for SDMWorldEvent against http://localhost:5180"
---

# UDAPI Tester Skill

## Purpose

Write and execute API-level tests (k6 smoke, k6 load, Schemathesis fuzz) against a UDAPI
endpoint, verify they pass, and iterate until green.

## Prerequisites

1. **Test project scaffolded** — `<SolutionName>Tester/udapitests/` exists with:
   - `tests/k6/smoke.js`
   - `tests/k6/load.js`
   - `run-tests.ps1`
   - `openapi.yaml`

   If missing, run the scaffolder first:
   ```powershell
   dotnet run dataminer-solution-tester/dataminer-udapi-tester/New-UdapiTests.cs -- \
     --input-yaml <yaml-path> --backend <backend-dir>
   ```
   This creates `<SolutionName>Tester/udapitests/` as a sibling of the backend folder.

2. **k6 installed**: `winget install GrafanaLabs.k6`
3. **Schemathesis installed** (for fuzz): `pip install schemathesis`
4. **Endpoint available**: Aspire running (`http://localhost:5180`) or a deployed DataMiner URL

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test folder | Yes | Path to `<SolutionName>Tester/udapitests/` |
| Target URL | No | Base URL (default: `http://localhost:5180`) |
| Bearer token | No | For deployed DataMiner (not needed for Aspire) |
| API route | No | Route segment (default: from `.env`) |

## Endpoint Modes

### Aspire (Local Development)

```
URL: http://localhost:5180
Auth: None (placeholder token)
Route: /<apiRoute>  (pure REST: GET, POST, PUT, DELETE)
Tests: smoke ONLY (load testing a mock is pointless)
```

The Aspire UDAPI proxy translates HTTP methods directly to the AutomationHost.
No DataMiner authentication is needed.

### Deployed DataMiner

```
URL: https://<host>/api/custom
Auth: Bearer token (from DataMiner API key)
Route: /<apiRoute>  (same REST interface via DataMiner UDAPI)
Tests: smoke + load + fuzz (full suite)
```

## Workflow

### Step 1: Verify Connectivity

```powershell
# Aspire
Invoke-RestMethod -Uri "http://localhost:5180/<route>" -Method GET

# Deployed
Invoke-RestMethod -Uri "https://<host>/api/custom/<route>" -Method GET `
  -Headers @{ Authorization = "Bearer <token>" }
```

If this returns an array (even empty `[]`), the endpoint is reachable.

### Step 2: Run Smoke Test

```powershell
cd <SolutionName>Tester/udapitests
.\run-tests.ps1 -Url http://localhost:5180 -Type smoke
```

Or directly with k6:
```powershell
k6 run tests/k6/smoke.js --env "API_BASE_URL=http://localhost:5180" --env "API_ROUTE=<route>"
```

Expected result: all checks pass (POST 201, GET 200, PUT 200, DELETE 204, verify deleted).

### Step 3: Analyze Failures

If the smoke test fails:

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| `POST returns 201: ✗` | Null DateTime in payload | Remove null DateTime fields from sample payload |
| `POST response has Identifier: ✗` | Backend returned `{}` | Check if payload has invalid field types |
| `PUT returns 200: ✗` returning 204 | Backend returns no-content for PUT | Accept both: `r.status === 200 || r.status === 204` |
| `DELETE returns 200 or 204: ✗` | Wrong query parameter name | Check if `?modelId=` or `?id=` |
| `GET after delete: Deleted item not in list: ✗` | Eventual consistency | Add `sleep(1)` before verification GET |
| `Connection refused` | Aspire not running | Start Aspire first |
| `401 Unauthorized` | Missing/expired token | Refresh bearer token |
| `API accepts invalid authentication` (fuzz) | Aspire mock ignores auth | Accepted on Aspire — real bug on deployed |
| `Missing header not rejected` (fuzz) | Aspire mock ignores auth | Accepted on Aspire — real bug on deployed |

Fix the k6 script or the endpoint, then re-run.

### Step 4: Run Load Test (Deployed Only)

```powershell
.\run-tests.ps1 -Url https://<host>/api/custom -Token <bearer> -Type load
```

Skip load testing against Aspire — the mock has no realistic performance profile.

### Step 5: Run Fuzz Test (Optional)

```powershell
.\run-tests.ps1 -Url http://localhost:5180 -Type fuzz
```

Requires `openapi.yaml` and `schemathesis` installed. Reports any:
- 500 Internal Server Errors
- Schema violations in responses
- Unexpected status codes for valid inputs

### Accepted Fuzz Failures (Aspire Only)

The following schemathesis failure categories are **expected** when running against the Aspire
mock and should be ignored. They only indicate real bugs on a deployed DataMiner system:

| Failure Category | Reason (Aspire) |
|-----------------|-----------------|
| API accepts invalid authentication | Mock has no auth enforcement |
| API accepts requests without authentication | Mock has no auth enforcement |
| Missing header not rejected (Authorization) | Mock has no auth enforcement |
| Missing Content-Type header (on error responses) | Aspire error handler doesn't set headers |

Do NOT treat these as bugs when the target URL is `localhost`. On a deployed system, all four
must return `401 Unauthorized` — if they don't, that's a real security issue to fix.

## Test Customization

### Adding model-specific assertions

The scaffolded `smoke.js` has a generic payload. The agent should customize it based on:
- Required fields that can't be empty
- DateTime fields that must be omitted (not null)
- Enum values that must match exactly
- Sub-object arrays with proper structure

### Known DateTime Rule

**CRITICAL**: The .NET backend cannot deserialize `null` DateTime values.
- In k6 payloads: either provide a valid ISO date or **omit the field entirely**
- NEVER send `"Start": null` — this crashes the backend (returns `{}`)
- If a DateTime field is optional, just don't include it in the payload

### Filter/OrderBy Testing

```javascript
// OData filter
const filter = encodeURIComponent("Status eq 'Requested'");
http.get(`${BASE_URL}/${ROUTE}?filter=${filter}`, params);

// OrderBy
http.get(`${BASE_URL}/${ROUTE}?orderby=Name desc`, params);
```

## Switching Endpoints

```powershell
# Aspire — smoke only, no auth
.\run-tests.ps1 -Url http://localhost:5180 -Type smoke

# Deployed — full suite with auth
.\run-tests.ps1 -Url https://dma.company.com/api/custom -Token "abc123" -Type all

# Just fuzz
.\run-tests.ps1 -Url http://localhost:5180 -Type fuzz
```

## Output

After successful execution:

```
<SolutionName>Tester/
├── e2etests/           ← Playwright (dataminer-frontend-tester)
└── udapitests/         ← This skill
    ├── openapi.yaml
    ├── run-tests.ps1
    ├── .env / .env.example
    └── tests/
        ├── k6/
        │   ├── smoke.js
        │   ├── load.js
        │   └── results/
        │       ├── smoke-result.json
        │       └── load-result.json
        └── schemathesis/
            └── results/
                └── report.xml
```

## CI/CD

```yaml
# Aspire smoke test
- name: UDAPI Smoke Test (Aspire)
  run: |
    dotnet run --project ${{ env.ASPIRE_APPHOST }} --launch-profile http &
    sleep 15
    cd ${{ env.TESTER_DIR }}/udapitests
    k6 run tests/k6/smoke.js --env "API_BASE_URL=http://localhost:5180"
    kill %1

# Deployed full suite
- name: UDAPI Tests (deployed)
  run: |
    cd ${{ env.TESTER_DIR }}/udapitests
    pwsh -File run-tests.ps1 -Url ${{ secrets.DMA_API_URL }} -Token ${{ secrets.DMA_TOKEN }} -Type all
```
