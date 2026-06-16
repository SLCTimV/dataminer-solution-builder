---
name: dataminer-solution-tester
description: >
  Orchestrates full solution testing: first runs UDAPI API tests (smoke validation + fixes),
  then runs frontend E2E tests (Playwright). Ensures the backend is correct before verifying
  the UI layer.
  USE FOR: "test the solution", "run all tests", "validate the generated solution",
  "test backend and frontend", "run full test suite".
  DO NOT USE FOR: writing the backend/frontend themselves (use dataminer-backend-builder,
  dataminer-frontend-builder), connector testing (use dataminer-unit-testing), scaffolding
  the test project (use New-UdapiTests.cs / New-FrontendTests.cs).
argument-hint: >
  Provide the solution tester path and target URL. Example:
  "test the SDMWorldEvent solution against Aspire on localhost"
---

# Solution Tester Skill

## Purpose

Run the full test suite for a generated DataMiner solution in the correct order:
1. **UDAPI tests first** — validates the API contract is healthy before testing the UI
2. **Frontend tests second** — verifies the UI works against a known-good backend

## Prerequisites

1. **Aspire running** or deployed DataMiner endpoint available
2. **Test project scaffolded** — `<SolutionName>Tester/` exists with both:
   - `udapitests/` (k6 smoke/load + schemathesis fuzz)
   - `e2etests/` (Playwright)
3. **Tools installed**: k6, Node.js (for Playwright), optionally schemathesis (for fuzz)

## Workflow

### Phase 1: UDAPI Smoke Test

**Goal**: Confirm the REST API CRUD contract is correct before involving the browser.

1. Navigate to `<SolutionName>Tester/udapitests/`
2. Run the smoke test:
   ```powershell
   .\run-tests.ps1 -Url http://localhost:5180 -Type smoke
   ```
3. **If all checks pass** → proceed to Phase 2
4. **If checks fail** → diagnose using the `dataminer-udapi-tester` skill:
   - Load the skill: read `dataminer-solution-tester/dataminer-udapi-tester/SKILL.md`
   - Follow Step 3 (Analyze Failures) to identify root cause
   - Fix the smoke script or the backend endpoint
   - Re-run until all checks pass

### Phase 2: Fuzz Test (Optional)

If `openapi.yaml` exists and schemathesis is installed:

```powershell
.\run-tests.ps1 -Url http://localhost:5180 -Type fuzz
```

Review failures. On Aspire, the following are **accepted** (not bugs):
- API accepts invalid authentication
- API accepts requests without authentication
- Missing header not rejected (Authorization)
- Missing Content-Type header (on error responses)

Any schema violations or 500 errors are real bugs to fix before proceeding.

### Phase 3: Frontend E2E Tests

**Goal**: Verify the UI correctly implements CRUD operations via the browser.

1. Navigate to `<SolutionName>Tester/e2etests/`
2. Install dependencies (first time):
   ```powershell
   npm ci
   npx playwright install chromium
   ```
3. Run the tests:
   ```powershell
   npx playwright test --reporter=list
   ```
4. **If all pass** → solution is validated
5. **If tests fail** → diagnose using the `dataminer-frontend-tester` skill:
   - Load the skill: read `dataminer-solution-tester/dataminer-frontend-tester/SKILL.md`
   - Follow its Step 3–5 workflow (fix assertions, check selectors, handle timing)
   - Re-run until green

## Why This Order?

| Order | Rationale |
|-------|-----------|
| UDAPI first | If the API returns wrong data (e.g. `{}`, nulls, 500s), the frontend tests will fail with misleading errors. Fix the source first. |
| Frontend second | Once the API contract is verified, UI failures are isolated to rendering/interaction bugs — much easier to diagnose. |

## Endpoint Modes

### Aspire (default)

| Service | URL |
|---------|-----|
| UDAPI proxy | `http://localhost:5180` |
| Frontend (Vite) | `http://localhost:5173` |
| ApiService | `http://localhost:5000` |

No authentication needed. Start Aspire first:
```powershell
dotnet run --project <SolutionName>Aspire/AspireSDM.AppHost --launch-profile http
```

### Deployed DataMiner

| Service | URL |
|---------|-----|
| UDAPI | `https://<host>/api/custom/<route>` |
| Frontend | `https://<host>/<APP_ID>/index.html` |

Bearer token required for UDAPI tests. Frontend tests use the deployed login page.

## Quick Run (All Tests)

```powershell
# From the tester root
cd <SolutionName>Tester

# Phase 1: API
cd udapitests
.\run-tests.ps1 -Url http://localhost:5180 -Type smoke
cd ..

# Phase 3: Frontend
cd e2etests
npx playwright test --reporter=list
```

## Expected Final State

```
✓ k6 smoke: all checks pass (5/5)
✓ Playwright: all tests pass (N passed, ≤1 skipped for Aspire-only auth tests)
```

If both pass, the generated solution is validated end-to-end.
