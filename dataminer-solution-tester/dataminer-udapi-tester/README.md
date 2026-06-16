# Stage 6 — UDAPI Tester (k6 + Schemathesis)

Two-phase approach to API-level testing for DataMiner UDAPI endpoints:

1. **Scaffolding** (`New-UdapiTests.cs`) — generates k6 smoke/load scripts, run-tests.ps1, openapi.yaml
2. **Test implementation** (`SKILL.md`) — guides an agent to customize tests and verify against Aspire/deployed

Tests run against **any endpoint** via parameters:
- Local Aspire: `http://localhost:5180` (smoke only — load testing a mock is pointless)
- Deployed DataMiner: `https://<host>/api/custom` with bearer token (full suite)

---

## Scaffolding (`New-UdapiTests.cs`)

```powershell
dotnet run dataminer-solution-tester/dataminer-udapi-tester/New-UdapiTests.cs -- \
  --input-yaml <path>         # YAML domain model (extracts fields, enums for sample payloads)
  --backend <path>            # Backend dir (discovers openapi.yaml from build output)
  --output-dir <path>         # Optional, defaults to <SolutionName>Tester/udapitests
```

### What it generates

```
<SolutionName>Tester/udapitests/
├── .env / .env.example       # API_BASE_URL, API_TOKEN, API_ROUTE
├── .gitignore
├── openapi.yaml              # Copied from backend build or stub generated
├── run-tests.ps1             # Unified runner: -Url -Token -Type smoke|load|fuzz|all
└── tests/
    ├── k6/
    │   ├── smoke.js          # Full CRUD cycle (POST→GET→PUT→DELETE→verify)
    │   ├── load.js           # 80/20 read/write ramp (5→10 VUs over 2min)
    │   └── results/          # JSON output from k6
    └── schemathesis/
        └── results/          # JUnit XML from schemathesis
```

---

## Test Tools

| Tool | Test type | What it validates |
|------|-----------|------------------|
| **k6** | Smoke + load | CRUD cycle, status codes, response shapes, latency under concurrent load |
| **Schemathesis** | Property-based fuzzing | Schema conformance, no 500 errors, stateful create→read→update→delete chains |
| **CATS** | Contract + fuzz | Missing required fields, wrong types, boundary values, extra fields, security headers |

---

## Quick Start

```powershell
# Aspire (smoke only — no auth needed)
.\run-tests.ps1 -Url http://localhost:5180 -Type smoke

# Deployed DataMiner (full suite)
.\run-tests.ps1 -Url https://<host>/api/custom -Token <bearer> -Type all

# Just fuzz
.\run-tests.ps1 -Url http://localhost:5180 -Type fuzz
```

---

## Test Types

| Type | Duration | Description |
|------|----------|-------------|
| `smoke` | ~3 s | POST → GET → GET(filtered) → PUT → DELETE; asserts status codes and response shape |
| `load` | ~2 min | Ramp to 10 concurrent users (80/20 read/write); asserts <5% error rate, p95 < 3 s |
| `fuzz` | ~5-8 min | Random valid/invalid payloads via Schemathesis; asserts no 500s, schema conformance |
| `all` | ~8-11 min | smoke + fuzz (default) |

---

## Prerequisites

| Tool | Install |
|------|---------|
| k6 | `winget install GrafanaLabs.k6` |
| Schemathesis | `pip install schemathesis` |
| CATS (optional) | Download from [github.com/Endava/cats/releases](https://github.com/Endava/cats/releases) (requires Java 11+) |

---

## Results

| Test | Output file | Format |
|------|-------------|--------|
| k6 smoke | `tests/k6/results/smoke-result.json` | JSON |
| k6 load | `tests/k6/results/load-result.json` | JSON |
| Schemathesis | `tests/schemathesis/results/report.xml` | JUnit XML |
| CATS | `tests/cats/results/` | HTML report |

---

## Environment Differences

| Aspect | Local (Aspire) | Deployed (DataMiner) |
|--------|---------------|---------------------|
| URL | `http://localhost:5180` | `https://<host>/api/custom` |
| Auth | None (placeholder) | Bearer token |
| HTTPS | No | Yes |
| Tests to run | **smoke only** | smoke + load + fuzz |
| Latency | <50ms | 200ms–2s |
| Data | Ephemeral (resets on restart) | Persistent (cleanup required) |

---

## Test Implementation (SKILL.md → Agent)

After scaffolding, an agent uses `SKILL.md` to:

1. Verify endpoint connectivity
2. Run k6 smoke test against Aspire
3. If tests fail → read k6 output → fix payload/assertions → re-run
4. Iterate until green
5. Optionally run fuzz/load against deployed

See [SKILL.md](SKILL.md) for the full agent workflow.

---

## TODO / Next Steps

- [ ] Add CI/CD workflow (GitHub Actions)
- [ ] Investigate Schemathesis stateful link support for CRUD chains
- [ ] Add CATS integration (requires Java 11+)
