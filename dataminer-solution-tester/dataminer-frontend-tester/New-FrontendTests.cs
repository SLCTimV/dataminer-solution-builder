#!/usr/bin/env dotnet-run
#:sdk Microsoft.NET.Sdk
#:property Nullable=enable
#:property ImplicitUsings=enable
#:package YamlDotNet@16.*

using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

// ---------------------------------------------------------------------------
// Argument parsing
// ---------------------------------------------------------------------------
string? inputYaml = null;
string? frontendDir = null;
string? outputDir = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--input-yaml" or "-i" when i + 1 < args.Length:
            inputYaml = args[++i];
            break;
        case "--frontend" or "-f" when i + 1 < args.Length:
            frontendDir = args[++i];
            break;
        case "--output-dir" or "-o" when i + 1 < args.Length:
            outputDir = args[++i];
            break;
        case "--help" or "-h":
            PrintUsage();
            return 0;
    }
}

if (inputYaml is null)
{
    Console.Error.WriteLine("Error: --input-yaml is required.");
    PrintUsage();
    return 1;
}

if (frontendDir is null)
{
    Console.Error.WriteLine("Error: --frontend is required.");
    PrintUsage();
    return 1;
}

if (!File.Exists(inputYaml))
{
    Console.Error.WriteLine($"Error: file not found: {inputYaml}");
    return 1;
}

if (!Directory.Exists(frontendDir))
{
    Console.Error.WriteLine($"Error: frontend directory not found: {frontendDir}");
    return 1;
}

// Resolve paths
inputYaml = Path.GetFullPath(inputYaml);
frontendDir = Path.GetFullPath(frontendDir);

// ---------------------------------------------------------------------------
// Parse YAML
// ---------------------------------------------------------------------------
var deserializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();

var yamlContent = File.ReadAllText(inputYaml);
var config = deserializer.Deserialize<SolutionConfig>(yamlContent);

if (config?.Solution is null)
{
    Console.Error.WriteLine("Error: YAML must have a 'solution:' section.");
    return 1;
}

var solutionName = config.Solution.Name;
var apiName = config.Solution.ApiName;
var apiRoute = config.Solution.ApiRoute;

// Resolve output directory: default to sibling of frontend → <SolutionName>Tester/e2etests
var testerFolderName = $"{solutionName}Tester";
var parentDir = Path.GetDirectoryName(frontendDir)!;
outputDir = outputDir is not null
    ? Path.GetFullPath(outputDir)
    : Path.Combine(parentDir, testerFolderName, "e2etests");

var models = config.Models?.Count > 0
    ? config.Models
    : config.MainModel is not null
        ? new List<ModelConfig> { config.MainModel }
        : new List<ModelConfig>();

var enums = config.Enums ?? new List<EnumConfig>();

// Use the first model as the primary model for page objects
var primaryModel = models.FirstOrDefault();

Console.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine($"║  Frontend Tester — Playwright Scaffolding                    ║");
Console.WriteLine($"╠══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║  Solution : {solutionName,-48}║");
Console.WriteLine($"║  App      : {apiName,-48}║");
Console.WriteLine($"║  Route    : {apiRoute,-48}║");
Console.WriteLine($"║  Models   : {models.Count,-48}║");
Console.WriteLine($"║  Output   : {outputDir,-48}║");
Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Create output structure
// ---------------------------------------------------------------------------
Directory.CreateDirectory(outputDir);
Directory.CreateDirectory(Path.Combine(outputDir, "fixtures"));
Directory.CreateDirectory(Path.Combine(outputDir, "pages"));
Directory.CreateDirectory(Path.Combine(outputDir, "tests"));

// ---------------------------------------------------------------------------
// package.json
// ---------------------------------------------------------------------------
Console.WriteLine("[1/7] Writing package.json...");
WriteFile(Path.Combine(outputDir, "package.json"), $$"""
{
  "name": "{{solutionName.ToLowerInvariant()}}-e2e-tests",
  "private": true,
  "scripts": {
    "test": "playwright test",
    "test:headed": "playwright test --headed",
    "test:ui": "playwright test --ui",
    "report": "playwright show-report"
  },
  "devDependencies": {
    "@playwright/test": "^1.48.0",
    "dotenv": "^16.4.0"
  }
}
""");

// ---------------------------------------------------------------------------
// playwright.config.ts
// ---------------------------------------------------------------------------
Console.WriteLine("[2/7] Writing playwright.config.ts...");
WriteFile(Path.Combine(outputDir, "playwright.config.ts"), """
import { defineConfig } from '@playwright/test';
import dotenv from 'dotenv';

dotenv.config();

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  reporter: process.env.CI ? 'github' : 'html',
  timeout: Number(process.env.TEST_TIMEOUT || 30000),
  use: {
    baseURL: process.env.BASE_URL || 'http://localhost:5173',
    ignoreHTTPSErrors: process.env.DM_IGNORE_HTTPS_ERRORS === 'true',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { browserName: 'chromium' },
    },
  ],
});
""");

// ---------------------------------------------------------------------------
// .env + .env.example
// ---------------------------------------------------------------------------
Console.WriteLine("[3/7] Writing .env files...");
WriteFile(Path.Combine(outputDir, ".env"), """
# Local Aspire (default)
BASE_URL=http://localhost:5173
DM_USERNAME=admin
DM_PASSWORD=admin
TEST_TIMEOUT=30000
""");

WriteFile(Path.Combine(outputDir, ".env.example"), """
# Target URL — Aspire local or deployed DataMiner
BASE_URL=http://localhost:5173
# BASE_URL=https://my-dma.company.com/APP_ID/index.html

# DataMiner credentials
DM_USERNAME=admin
DM_PASSWORD=admin

# Certificate errors (set true for self-signed HTTPS)
DM_IGNORE_HTTPS_ERRORS=false

# Timeouts (ms) — increase for deployed systems
TEST_TIMEOUT=30000
""");

// ---------------------------------------------------------------------------
// fixtures/app.fixture.ts
// ---------------------------------------------------------------------------
Console.WriteLine("[4/7] Writing fixtures/app.fixture.ts...");
WriteFile(Path.Combine(outputDir, "fixtures", "app.fixture.ts"), $$"""
import { test as base, type Page } from '@playwright/test';

export const test = base.extend<{ authenticatedPage: Page }>({
  authenticatedPage: async ({ page }, use) => {
    await page.goto('/');

    // Fill login form
    await page.fill('#username', process.env.DM_USERNAME || 'admin');
    await page.fill('#password', process.env.DM_PASSWORD || 'admin');
    await page.click('button[type="submit"]');

    // Wait for the data table to confirm successful login
    await page.waitForSelector('.data-table', {
      timeout: Number(process.env.TEST_TIMEOUT || 30000),
    });

    await use(page);
  },
});

export { expect } from '@playwright/test';
""");

// ---------------------------------------------------------------------------
// pages/login.page.ts
// ---------------------------------------------------------------------------
Console.WriteLine("[5/7] Writing pages/login.page.ts...");
WriteFile(Path.Combine(outputDir, "pages", "login.page.ts"), $$"""
import { type Page, type Locator } from '@playwright/test';

export class LoginPage {
  readonly page: Page;
  readonly usernameInput: Locator;
  readonly passwordInput: Locator;
  readonly submitButton: Locator;
  readonly heading: Locator;

  constructor(page: Page) {
    this.page = page;
    this.usernameInput = page.locator('#username');
    this.passwordInput = page.locator('#password');
    this.submitButton = page.locator('button[type="submit"]');
    this.heading = page.locator('h2');
  }

  async goto() {
    await this.page.goto('/');
  }

  async login(username: string, password: string) {
    await this.usernameInput.fill(username);
    await this.passwordInput.fill(password);
    await this.submitButton.click();
  }
}
""");

// ---------------------------------------------------------------------------
// pages/table.page.ts
// ---------------------------------------------------------------------------
Console.WriteLine("[6/7] Writing pages/table.page.ts...");

var fieldHelpers = new StringBuilder();
if (primaryModel?.Properties is not null)
{
    foreach (var prop in primaryModel.Properties)
    {
        var camelName = char.ToLowerInvariant(prop.Name[0]) + prop.Name[1..];
        fieldHelpers.AppendLine($"  /** Column: {prop.Name} ({prop.Type}) */");
        fieldHelpers.AppendLine($"  get {camelName}Column() {{ return this.page.locator('[data-field=\"{prop.Name}\"]'); }}");
        fieldHelpers.AppendLine();
    }
}

WriteFile(Path.Combine(outputDir, "pages", "table.page.ts"), $$"""
import { type Page, type Locator } from '@playwright/test';

export class TablePage {
  readonly page: Page;
  readonly table: Locator;
  readonly createButton: Locator;
  readonly rows: Locator;
  readonly searchInput: Locator;
  readonly filterButton: Locator;

  constructor(page: Page) {
    this.page = page;
    this.table = page.locator('.data-table');
    this.createButton = page.locator('button:has-text("Create"), button:has-text("Add"), button:has-text("New")');
    this.rows = page.locator('.data-table tbody tr, .data-table .table-row');
    this.searchInput = page.locator('input[placeholder*="Search"], input[placeholder*="Filter"]');
    this.filterButton = page.locator('button:has-text("Filter")');
  }

  async waitForTable() {
    await this.table.waitFor({ state: 'visible' });
  }

  async getRowCount() {
    return await this.rows.count();
  }

  async clickCreate() {
    await this.createButton.click();
  }

  async getRowByText(text: string) {
    return this.rows.filter({ hasText: text });
  }

  async clickEditOnRow(text: string) {
    const row = this.rows.filter({ hasText: text });
    await row.locator('button:has-text("Edit"), .edit-btn, [title="Edit"]').click();
  }

  async clickDeleteOnRow(text: string) {
    const row = this.rows.filter({ hasText: text });
    await row.locator('button:has-text("Delete"), .delete-btn, [title="Delete"]').click();
  }

{{fieldHelpers}}}
""");

// ---------------------------------------------------------------------------
// pages/modal.page.ts
// ---------------------------------------------------------------------------
Console.WriteLine("[7/7] Writing pages/modal.page.ts...");

var modalFields = new StringBuilder();
if (primaryModel?.Properties is not null)
{
    foreach (var prop in primaryModel.Properties)
    {
        var camelName = char.ToLowerInvariant(prop.Name[0]) + prop.Name[1..];
        if (prop.Type == "enum")
        {
            modalFields.AppendLine($"  async select{prop.Name}(value: string) {{");
            modalFields.AppendLine($"    const select = this.modal.locator('select[name=\"{prop.Name}\"], [data-field=\"{prop.Name}\"] select');");
            modalFields.AppendLine($"    await select.selectOption(value);");
            modalFields.AppendLine($"  }}");
        }
        else if (prop.Type == "bool")
        {
            modalFields.AppendLine($"  async toggle{prop.Name}() {{");
            modalFields.AppendLine($"    const checkbox = this.modal.locator('input[name=\"{prop.Name}\"], [data-field=\"{prop.Name}\"] input[type=\"checkbox\"]');");
            modalFields.AppendLine($"    await checkbox.click();");
            modalFields.AppendLine($"  }}");
        }
        else if (prop.Type == "ref")
        {
            modalFields.AppendLine($"  async select{prop.Name}(value: string) {{");
            modalFields.AppendLine($"    const select = this.modal.locator('select[name=\"{prop.Name}\"], [data-field=\"{prop.Name}\"] select');");
            modalFields.AppendLine($"    await select.selectOption({{ label: value }});");
            modalFields.AppendLine($"  }}");
        }
        else
        {
            modalFields.AppendLine($"  async fill{prop.Name}(value: string) {{");
            modalFields.AppendLine($"    const input = this.modal.locator('input[name=\"{prop.Name}\"], [data-field=\"{prop.Name}\"] input, textarea[name=\"{prop.Name}\"]');");
            modalFields.AppendLine($"    await input.fill(value);");
            modalFields.AppendLine($"  }}");
        }
        modalFields.AppendLine();
    }
}

WriteFile(Path.Combine(outputDir, "pages", "modal.page.ts"), $$"""
import { type Page, type Locator } from '@playwright/test';

export class ModalPage {
  readonly page: Page;
  readonly modal: Locator;
  readonly submitButton: Locator;
  readonly cancelButton: Locator;
  readonly closeButton: Locator;

  constructor(page: Page) {
    this.page = page;
    this.modal = page.locator('.modal, [role="dialog"], .overlay');
    this.submitButton = this.modal.locator('button[type="submit"], button:has-text("Save"), button:has-text("Create"), button:has-text("Submit")');
    this.cancelButton = this.modal.locator('button:has-text("Cancel")');
    this.closeButton = this.modal.locator('button:has-text("Close"), .close-btn, [aria-label="Close"]');
  }

  async waitForModal() {
    await this.modal.waitFor({ state: 'visible' });
  }

  async submit() {
    await this.submitButton.click();
  }

  async cancel() {
    await this.cancelButton.click();
  }

  async close() {
    await this.closeButton.click();
  }

  async isVisible() {
    return await this.modal.isVisible();
  }

{{modalFields}}}
""");

// ---------------------------------------------------------------------------
// tests/.gitkeep
// ---------------------------------------------------------------------------
WriteFile(Path.Combine(outputDir, "tests", ".gitkeep"), "");

// ---------------------------------------------------------------------------
// .gitignore
// ---------------------------------------------------------------------------
WriteFile(Path.Combine(outputDir, ".gitignore"), """
node_modules/
test-results/
playwright-report/
blob-report/
.env
""");

// ---------------------------------------------------------------------------
// tsconfig.json
// ---------------------------------------------------------------------------
WriteFile(Path.Combine(outputDir, "tsconfig.json"), """
{
  "compilerOptions": {
    "target": "ES2020",
    "module": "ESNext",
    "moduleResolution": "bundler",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "forceConsistentCasingInFileNames": true
  },
  "include": ["**/*.ts"]
}
""");

// ---------------------------------------------------------------------------
// Done
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine($"✅ Playwright test scaffold generated at: {outputDir}");
Console.WriteLine();
Console.WriteLine("Next steps:");
Console.WriteLine($"  1. cd \"{outputDir}\"");
Console.WriteLine("  2. npm install");
Console.WriteLine("  3. npx playwright install chromium");
Console.WriteLine("  4. Use the frontend-tester SKILL.md to write test specs");
Console.WriteLine();
Console.WriteLine($"Output structure:");
Console.WriteLine($"  {Path.Combine(parentDir, testerFolderName)}/");
Console.WriteLine($"    └── e2etests/       ← Playwright (this output)");
Console.WriteLine($"    └── udapitests/     ← k6/Schemathesis (dataminer-udapi-tester)");
Console.WriteLine();

return 0;

// ═══════════════════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════════════════

void WriteFile(string path, string content)
{
    File.WriteAllText(path, content, new UTF8Encoding(false));
}

void PrintUsage()
{
    Console.WriteLine("""
    Usage: dotnet run New-FrontendTests.cs -- [options]

    Options:
      --input-yaml, -i   Path to the domain input YAML file (required)
      --frontend, -f     Path to the frontend project root (required)
      --output-dir, -o   Output directory (default: <SolutionName>Tester/e2etests alongside frontend)
      --help, -h         Show this help
    """);
}

// ═══════════════════════════════════════════════════════════════════════════
// YAML model classes
// ═══════════════════════════════════════════════════════════════════════════

class SolutionConfig
{
    public SolutionMeta? Solution { get; set; }
    public ModelConfig? MainModel { get; set; }
    public List<ModelConfig>? Models { get; set; }
    public List<EnumConfig>? Enums { get; set; }
}

class SolutionMeta
{
    public string Name { get; set; } = "";
    public string ApiRoute { get; set; } = "";
    public string ApiName { get; set; } = "";
    public string ApiDescription { get; set; } = "";
    public string DomModuleId { get; set; } = "";
    public string NugetPackageId { get; set; } = "";
}

class ModelConfig
{
    public string Name { get; set; } = "";
    public List<PropertyConfig>? Properties { get; set; }
    public List<ListConfig>? Lists { get; set; }
}

class PropertyConfig
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "string";
    public string? Enum { get; set; }
    public string? Ref { get; set; }
    public bool Required { get; set; }
}

class ListConfig
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
}

class EnumConfig
{
    public string Name { get; set; } = "";
    public List<string>? Values { get; set; }
}
