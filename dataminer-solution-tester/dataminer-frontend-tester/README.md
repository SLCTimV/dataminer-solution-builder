# Stage 6b — Frontend Tester (Playwright)

Two-phase approach to E2E testing for DataMiner SDM frontend applications:

1. **Scaffolding** (`New-FrontendTests.cs`) — generates the test project skeleton with config, fixtures, and page objects
2. **Test implementation** (`SKILL.md`) — guides an agent to write the actual test specs, using Aspire to verify they pass

Tests run against **any endpoint** via the `BASE_URL` environment variable:
- Local Aspire: `http://localhost:5173`
- Deployed DataMiner: `https://<dma-host>/APP_ID/index.html`

---

## Scaffolding (`New-FrontendTests.cs`)

The generator script creates the test infrastructure — it does NOT write test logic.

### What it generates

```
<SolutionName>Tester/
├── e2etests/                        ← Playwright (generated here)
│   ├── package.json                 # @playwright/test + dotenv dependencies
│   ├── playwright.config.ts         # Reads BASE_URL from env, configures browsers
│   ├── .env                         # Default: BASE_URL=http://localhost:5173
│   ├── .env.example                 # Documented template for all settings
│   ├── fixtures/
│   │   └── app.fixture.ts           # Login helper + page factory (reads DM_USERNAME/DM_PASSWORD)
│   ├── pages/
│   │   ├── login.page.ts            # LoginPage POM (username, password, submit)
│   │   ├── table.page.ts            # TablePage POM (rows, create btn, edit/delete btns)
│   │   └── modal.page.ts            # ModalPage POM (fields, dropdowns, submit, close)
│   └── tests/
│       └── .gitkeep                 # Tests are written by the agent (see SKILL.md)
└── udapitests/                      ← k6/Schemathesis (dataminer-udapi-tester)
```

### Parameters

```
dotnet run dataminer-solution-tester/dataminer-frontend-tester/New-FrontendTests.cs -- \
  --input-yaml <path>            # YAML domain model (extracts field names, enums)
  --frontend <path>              # Frontend project root (where package.json lives)
  --output-dir <path>            # Optional, defaults to <SolutionName>Tester/e2etests alongside frontend
```

### What it reads from YAML

| Variable | Source | Used for |
|----------|--------|----------|
| `solutionName` | `solution.name` | Test data prefixes, login page title assertion |
| `appTitle` | `solution.apiName` | Login page heading selector |
| `modelFields` | `models[0].properties` | Page object field helpers |
| `enumFields` | properties with `enum` values | Dropdown option lists in POM |
| `requiredFields` | properties with `required: true` | Form validation test hints |

### Key design: endpoint-agnostic

The `playwright.config.ts` resolves the target URL entirely from environment:

```typescript
import { defineConfig } from '@playwright/test';
import dotenv from 'dotenv';
dotenv.config();

export default defineConfig({
  use: {
    baseURL: process.env.BASE_URL || 'http://localhost:5173',
    ignoreHTTPSErrors: process.env.DM_IGNORE_HTTPS_ERRORS === 'true',
  },
  timeout: Number(process.env.TEST_TIMEOUT || 30000),
});
```

The `app.fixture.ts` reads credentials from env:

```typescript
import { test as base } from '@playwright/test';

export const test = base.extend<{ authenticatedPage: Page }>({
  authenticatedPage: async ({ page }, use) => {
    await page.goto('/');
    await page.fill('#username', process.env.DM_USERNAME || 'admin');
    await page.fill('#password', process.env.DM_PASSWORD || 'admin');
    await page.click('button[type="submit"]');
    await page.waitForSelector('.data-table');
    await use(page);
  },
});
```

### Environment presets

```env
# .env (local Aspire — default)
BASE_URL=http://localhost:5173
DM_USERNAME=admin
DM_PASSWORD=admin
TEST_TIMEOUT=30000

# .env.deployed (real DataMiner)
BASE_URL=https://my-dma.company.com/APP_ID/index.html
DM_USERNAME=DOMAIN\user
DM_PASSWORD=secret
DM_IGNORE_HTTPS_ERRORS=true
TEST_TIMEOUT=60000
```

Switch environments by setting `BASE_URL`:
```powershell
# Aspire (default)
npx playwright test

# Deployed DataMiner
$env:BASE_URL="https://dma.company.com/APP_123/index.html"; npx playwright test
```

---

## Test Implementation (SKILL.md → Agent)

After scaffolding, an agent uses `SKILL.md` to:

1. Read the page objects and YAML model
2. Write `login.spec.ts` and `crud.spec.ts` test files
3. Run `npx playwright test` against the running Aspire instance
4. If tests fail → read the error output → fix the test code → re-run
5. Iterate until all tests pass
6. The same tests then work against a deployed DataMiner (just change `BASE_URL`)

See [SKILL.md](SKILL.md) for the full agent workflow.

---

## Environment Differences

| Aspect | Local (Aspire) | Deployed (DataMiner) |
|--------|---------------|---------------------|
| `BASE_URL` | `http://localhost:5173` | `https://<host>/APP_ID/index.html` |
| Auth | Mock API (any credentials) | Real DataMiner credentials |
| HTTPS | No | Yes (`DM_IGNORE_HTTPS_ERRORS=true`) |
| Login flow | Form → `ConnectAppAndInfo` | Same form, or `/auth/login` redirect |
| Data isolation | Fresh on each restart | Shared — use `PW-` prefixed test data |
| Cleanup | Optional | **Mandatory** |
| Timeout | 30s | 60s (network latency) |

### Design Rules

1. **Always clean up** — `test.afterEach` deletes created items (even on failure)
2. **Unique test data** — names prefixed with `PW-{timestamp}` to avoid collisions
3. **No empty-state assumptions** — assert presence/absence, never exact row counts
4. **Configurable timeouts** — `TEST_TIMEOUT` env var for slower deployed systems
5. **Certificate handling** — `DM_IGNORE_HTTPS_ERRORS=true` for self-signed certs

---

## Running Tests

```powershell
# Against Aspire (must be running)
cd <SolutionName>Tester/e2etests
npx playwright test

# Against deployed DataMiner
$env:BASE_URL = "https://my-dma.company.com/APP_ID/index.html"
$env:DM_USERNAME = "DOMAIN\user"
$env:DM_PASSWORD = "secret"
$env:DM_IGNORE_HTTPS_ERRORS = "true"
npx playwright test

# Debugging
npx playwright test --ui
npx playwright test --headed
npx playwright show-report
```

---

## CI/CD

```yaml
# Local Aspire verification
- name: E2E tests (Aspire)
  run: |
    dotnet run --project ${{ env.ASPIRE_APPHOST }} --launch-profile http &
    sleep 15
    cd ${{ env.TESTER_DIR }}/e2etests && npx playwright test --reporter=github
    kill %1

# Deployed DataMiner verification
- name: E2E tests (deployed)
  env:
    BASE_URL: ${{ secrets.DMA_APP_URL }}
    DM_USERNAME: ${{ secrets.DMA_USERNAME }}
    DM_PASSWORD: ${{ secrets.DMA_PASSWORD }}
    DM_IGNORE_HTTPS_ERRORS: "true"
  run: cd ${{ env.TESTER_DIR }}/e2etests && npx playwright test --reporter=github
```

---

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `@playwright/test` | ^1.50 | Test framework + assertions |
| `dotenv` | ^16.0 | Load `.env` files for endpoint config |

---

## TODO

- [ ] Implement `New-FrontendTests.cs` scaffolding script
- [ ] Create `SKILL.md` agent workflow
- [ ] Handle DataMiner `/auth/login` cookie redirect flow in fixture
- [ ] Add screenshot-on-failure support
- [ ] Add accessibility checks (`@axe-core/playwright`)
- [ ] Document how to obtain APP_ID for deployed custom apps
