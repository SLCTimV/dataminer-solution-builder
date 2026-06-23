# TODO — DataMiner Backend Builder

## Check template usage across skills and .cs files

Verify whether the different skills and `.cs` generator scripts reference/use the relevant Visual Studio DataMiner templates (e.g. `dotnet new dataminer-*`) instead of manually scaffolding project structures from scratch.

### Audit Results (2026-06-23)

#### Available DataMiner Templates (installed)

| Template | Short Name | Purpose |
|----------|-----------|---------|
| DataMiner Ad Hoc Data Source | `dataminer-gqi-ad-hoc-data-source-project` | GQI ad-hoc sources |
| DataMiner Automation Library | `dataminer-automation-library-project` | Shared libraries |
| DataMiner Automation Project | `dataminer-automation-project` | Generic automation scripts |
| DataMiner NuGet Project | `dataminer-nuget-project` | NuGet libraries |
| DataMiner Package Project | `dataminer-package-project` | .dmapp installers |
| DataMiner Test Package | `dataminer-test-package-project` | QAOps test packages |
| **DataMiner User-Defined API** | `dataminer-user-defined-api-project` | **Dedicated UDAPI template** |

#### Current usage vs. ideal template

| File | Currently Uses | Should Use | Status |
|------|---------------|-----------|--------|
| `New-Backend.cs` | `dotnet new sln` | `dotnet new sln` | ✅ OK |
| `New-Udapi.cs` | `dataminer-automation-project` + patches | **`dataminer-user-defined-api-project`** | 🔴 Upgrade |
| `New-Adhoc.cs` | `dataminer-automation-project` + full .csproj overwrite | **`dataminer-gqi-ad-hoc-data-source-project`** | 🔴 Upgrade |
| `New-BackendInstaller.cs` | `dataminer-package-project` | `dataminer-package-project` | ✅ OK |
| `New-DevPack.cs` | `dataminer-nuget-project` + `dataminer-package-project` | Same | ✅ OK |

### Action Items

- [ ] **`New-Udapi.cs`** — Switch from `dataminer-automation-project` to `dataminer-user-defined-api-project`. This dedicated template likely includes `GenerateOpenApi`, proper UDAPI SDK references, and the `ApiTriggerInput` script parameter by default — eliminating all post-generation `.csproj` and XML patching.
- [ ] **`New-Adhoc.cs`** — Switch from `dataminer-automation-project` to `dataminer-gqi-ad-hoc-data-source-project`. This should eliminate the manual `.csproj` overwrite entirely. Only add post-generation patches for solution-specific properties not covered by the template.
- [ ] **Verify template defaults** — Before switching, scaffold each new template in a temp dir and compare the generated `.csproj` / XML against what the scripts currently produce. Document any gaps that still need post-generation patching.
- [ ] **Update `dataminer-sdk` skill** — Add the newer templates (`dataminer-user-defined-api-project`, `dataminer-gqi-ad-hoc-data-source-project`) to the skill's template catalog if not already listed.

### Goal

Where a relevant DataMiner SDK template exists, the generator scripts should use it (via `dotnet new`) rather than hand-writing `.csproj` files and boilerplate. This ensures:
- Correct SDK versions and PackageReferences stay up-to-date
- `.csproj` properties (`DataMinerType`, `GenerateDataMinerPackage`, etc.) match current SDK expectations
- CatalogInformation, XML descriptors, and other required files are included by default
