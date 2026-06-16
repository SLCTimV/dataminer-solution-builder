# Stage 6 — Solution Tester

Runs automated **smoke, load, and fuzz tests** against the generated UDAPI to verify correctness, API contract compliance, and resilience under load.

---

## Responsibility

- Validate the full CRUD cycle against the live (or locally Aspire-hosted) API.
- Check that the API response shapes conform to `openapi.yaml`.
- Probe edge cases with property-based fuzzing (random valid/invalid payloads).
- Report results in standard formats (JSON metrics, JUnit XML, HTML).

---

## Test Tools

| Tool | Test type | What it validates |
|------|-----------|------------------|
| **k6** | Smoke + load | CRUD cycle, status codes, response shapes, latency under concurrent load |
| **Schemathesis** | Property-based fuzzing | Schema conformance, no 500 errors, stateful create→read→update→delete chains |
| **CATS** | Contract + fuzz | Missing required fields, wrong types, boundary values, extra fields, security headers |

---

## Quick Start

```bash
# Windows — run all tests
run-tests.bat <endpoint-url> <bearer-token>

# Run only smoke test (fast CRUD validation, ~3 seconds)
run-tests.bat https://<host>/api/custom <token> smoke

# Run only fuzz tests (~5-8 minutes)
run-tests.bat https://<host>/api/custom <token> fuzz
```

For local Aspire testing use the ApiService URL (e.g. `http://localhost:5261/api/custom`) and omit the bearer token or use any placeholder.

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

## Reference Implementation

```
C:\Users\Tim\source\repos\APITestingSolution\
├── openapi.yaml              # Spec used by CATS and Schemathesis
├── run-tests.bat / .sh       # Entry-point scripts
├── tests/
│   ├── k6/
│   │   ├── smoke.js          # Full CRUD cycle
│   │   └── load.js           # 80/20 read/write ramp
│   ├── schemathesis/
│   │   └── run.bat / .sh
│   └── cats/
│       └── run.bat / .sh
└── .env.example              # API_BASE_URL, API_TOKEN
```

---

## Agent

The **test-to-tutorial-converter** custom agent can convert these automated tests into human-executable manual testing tutorials when needed.

---

## TODO / Next Steps

- [ ] Define the agent SKILL.md for this folder
- [ ] Template the test suite so it auto-configures from `openapi.yaml` (currently Event-specific)
- [ ] Add Aspire integration test step: start Aspire → run smoke → stop Aspire
- [ ] Add CI/CD integration (GitHub Actions workflow)
- [ ] Investigate CATS false positives and add a suppress list
