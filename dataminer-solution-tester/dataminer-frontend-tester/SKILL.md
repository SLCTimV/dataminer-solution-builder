---
name: dataminer-frontend-tester
description: >
  Writes Playwright E2E tests for a scaffolded DataMiner SDM frontend test project.
  Uses a running Aspire instance to verify tests pass, then iterates until green.
  Tests are endpoint-agnostic: they work against both local Aspire (BASE_URL=http://localhost:5173)
  and deployed DataMiner (BASE_URL=https://<host>/APP_ID/index.html).
  USE FOR: "write frontend tests", "create E2E tests", "test the UI", "verify CRUD works",
  "run playwright tests", "fix failing test".
  DO NOT USE FOR: scaffolding the test project (use New-FrontendTests.cs), writing backend/API
  tests (use dataminer-udapi-tester), generating the frontend itself (use dataminer-frontend-builder).
argument-hint: >
  Provide the frontend path and optionally the target URL. Example:
  "write CRUD tests for SDMWorldEventFrontend against http://localhost:5173"
---

# Frontend Tester Skill

## Purpose

Write Playwright test specs for a scaffolded E2E test project, verify they pass against
a running endpoint (Aspire or deployed DataMiner), and iterate until all tests are green.

## Prerequisites

1. **Test project scaffolded** — `<frontend>/tests/e2e/` exists with:
   - `playwright.config.ts`
   - `fixtures/app.fixture.ts`
   - `pages/*.page.ts`

   If missing, run the scaffolder first:
   ```powershell
   dotnet run dataminer-solution-tester/dataminer-frontend-tester/New-FrontendTests.cs -- \
     --input-yaml <yaml-path> --frontend <frontend-path>
   ```
   This creates `<SolutionName>Tester/e2etests/` as a sibling of the frontend folder.
   Then install dependencies:
   ```powershell
   cd <SolutionName>Tester/e2etests
   npm install
   npx playwright install chromium
   ```

2. **Aspire running** (for local verification) OR a deployed DataMiner URL available
3. **YAML input** available to understand model fields and enums

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Frontend path | Yes | Path to the frontend project (e.g. `<workspace>/SDMWorldEventFrontend`) |
| YAML path | Yes | Path to the solution YAML (for model field knowledge) |
| Target URL | No | `BASE_URL` to test against (default: `http://localhost:5173`) |
| Credentials | No | `DM_USERNAME` / `DM_PASSWORD` (default: `admin`/`admin`) |

## Workflow

### Step 1: Gather Context

Read the following to understand the UI structure:
- `<frontend>/src/components/*.jsx` — understand selectors, button labels, field names
- `<SolutionName>Tester/e2etests/pages/*.page.ts` — available page object methods
- `<SolutionName>Tester/e2etests/fixtures/app.fixture.ts` — how login works
- YAML input — model fields, enum values, required fields

### Step 2: Write Test Specs

Create test files in `<SolutionName>Tester/e2etests/tests/`:

#### `login.spec.ts`
```typescript
import { test, expect } from '../fixtures/app.fixture';

test.describe('Login', () => {
  test('should show data table after login', async ({ authenticatedPage }) => {
    await expect(authenticatedPage.locator('.data-table')).toBeVisible();
  });

  test('should display app title on login page', async ({ page }) => {
    await page.goto('/');
    await expect(page.locator('h2')).toContainText('<AppTitle>');
  });
});
```

#### `crud.spec.ts`
```typescript
import { test, expect } from '../fixtures/app.fixture';

const TEST_PREFIX = `PW-${Date.now()}`;

test.describe('CRUD Operations', () => {
  let createdName: string;

  test.afterEach(async ({ authenticatedPage }) => {
    // Cleanup: delete any test items created
    // Use page interactions or direct API calls
  });

  test('should create a new item', async ({ authenticatedPage }) => {
    createdName = `${TEST_PREFIX}-Create`;
    // Click create button → fill form → submit → assert row exists
  });

  test('should update an existing item', async ({ authenticatedPage }) => {
    // Create item → click edit → modify fields → submit → assert updated
  });

  test('should delete an item', async ({ authenticatedPage }) => {
    // Create item → click delete → confirm → assert row gone
  });
});
```

### Step 3: Run Tests Against Aspire

Execute the tests and capture the output:

```powershell
cd <SolutionName>Tester/e2etests
$env:BASE_URL = "<target-url>"   # http://localhost:5173 or deployed URL
npx playwright test --reporter=line 2>&1
```

### Step 4: Analyze Failures

If tests fail:
1. Read the Playwright error output (selector not found, timeout, assertion mismatch)
2. Identify the issue:
   - **Selector wrong** → inspect the actual component JSX for correct selectors
   - **Timing issue** → add `waitForSelector` or increase timeout
   - **Data mismatch** → check what the API actually returns
   - **Login failed** → verify credentials or check if `/auth/login` redirect happened
   - **API returns `{}` (empty object)** → the frontend is sending invalid data to the backend (see below)
3. Fix the test code OR the frontend code if the bug is there
4. Re-run → repeat until green

#### Known Frontend Bugs to Watch For

These are common issues in generated frontends that cause `ApiTriggerOutput: "{}"` (silent backend failure):

| Symptom | Root Cause | Fix |
|---------|-----------|-----|
| Create/Edit returns `{}` | `null` DateTime in RawBody (e.g. `"Start": null`) | Omit empty DateTime fields: `delete payload.Start` instead of `null` |
| Edit returns `{}` | Year-0001 date sent as ISO string (e.g. `"0000-12-31T23:24:30.000Z"`) | Treat `.NET default` `0001-01-01T00:00:00` as "no date" — return `''` from `toDatetimeLocal()` |
| Aspire mock accepts all credentials | AuthN mock always returns a connection | Skip invalid-credential tests with `test.skip(baseUrl.includes('localhost'))` |

If the API returns `{}` for a request that works from curl/PowerShell, intercept the request body with:
```typescript
const [req] = await Promise.all([
  page.waitForRequest(r => r.url().includes('ExecuteAutomation')),
  page.click('button[type="submit"]'),
]);
console.log(JSON.parse(req.postData()!).script.Parameters[0].Value);
```
Then compare with a known-good payload.

### Step 5: Verify Cross-Environment Compatibility

Once tests pass locally, verify the test code does not contain:
- Hardcoded `localhost` URLs (must use `baseURL` from config)
- Assumptions about empty database (deployed systems have existing data)
- Missing cleanup in `afterEach` (critical for deployed)
- Hardcoded credentials (must read from `process.env`)

## Test Writing Rules

### Selectors (priority order)
1. `data-testid` attributes (if present)
2. Accessible roles: `page.getByRole('button', { name: 'Sign In' })`
3. Label text: `page.getByLabel('Username')`
4. CSS class selectors: `page.locator('.data-table')`
5. Text content: `page.getByText('No events found')`

### Assertions
- Use `expect(locator).toBeVisible()` over `toHaveCount(1)`
- Use `expect(locator).toContainText()` over exact text matches
- Never assert exact row counts — assert presence/absence of specific items
- Use `toHaveCount(0)` only for confirming deletion of a specific test item

### Test Data
- Always prefix with `PW-{timestamp}` to isolate from real data
- Never depend on data from a previous test (each test is self-contained)
- Clean up in `afterEach` — use the Delete button through the UI

### Timeouts
- Default action timeout: 10s (override with `TEST_TIMEOUT` env var)
- Navigation timeout: 30s
- For deployed DataMiner: multiply defaults by 2

### Dialog Handling
```typescript
// Handle confirmation dialogs (e.g., delete confirmation)
page.on('dialog', dialog => dialog.accept());
```

## Switching Endpoints

The agent can verify tests work against different targets:

```powershell
# Verify against Aspire (local mock)
$env:BASE_URL = "http://localhost:5173"
npx playwright test

# Verify against deployed DataMiner
$env:BASE_URL = "https://my-dma.skyline.be/APP_ID/index.html"
$env:DM_USERNAME = "admin"
$env:DM_PASSWORD = "admin"
$env:DM_IGNORE_HTTPS_ERRORS = "true"
npx playwright test
```

The same test code must pass in both environments. If a test passes locally
but fails on deployed, the issue is likely:
- Timing (add explicit waits)
- Data conflicts (improve test isolation)
- Auth flow difference (handle redirect)

## Output

After successful execution, the test project contains:

```
<SolutionName>Tester/
├── e2etests/           ← Playwright E2E (this skill)
│   ├── fixtures/
│   ├── pages/
│   └── tests/
│       ├── login.spec.ts       # Auth flow verification
│       ├── crud.spec.ts        # Full CRUD lifecycle
│       └── filter.spec.ts      # (optional) filter/sort verification
└── udapitests/         ← k6/Schemathesis (dataminer-udapi-tester)
```

All tests:
- Pass against the Aspire local dev environment
- Are structured to pass against deployed DataMiner (same code, different BASE_URL)
- Clean up after themselves
- Use page objects for maintainability
