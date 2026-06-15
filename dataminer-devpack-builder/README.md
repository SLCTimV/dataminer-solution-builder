# Stage 2 — Devpack Builder

Scaffolds the Visual Studio **.NET solution skeleton** for a DataMiner SDM domain — project files, folder structure, and NuGet package references — before any code generation takes place.

---

## Responsibility

- Create the `.sln` and `.csproj` files for all projects in the solution.
- Add the fixed NuGet package references required for each project type.
- Configure build targets (`.NET Framework 4.8` for DataMiner compatibility).
- Output a build-ready skeleton that the Backend Builder can populate with generated C# code.

---

## Solution Structure Produced

```
SDM<DomainName>/
├── SDM<DomainName>.sln
├── SDM<DomainName>/            # Domain library (models, helpers, exposers)
│   └── SDM<DomainName>.csproj
├── SDM<DomainName>.Package/    # .dmapp installer project
│   └── SDM<DomainName>.Package.csproj
├── SDM<DomainName>UDAPI/       # User Defined API automation script
│   └── SDM<DomainName>UDAPI.csproj
└── SDM<DomainName>GQI/         # GQI ad-hoc data source (optional)
    └── SDM<DomainName>GQI.csproj
```

---

## NuGet Dependencies

### Domain library (`SDM<DomainName>`)

| Package | Purpose |
|---------|---------|
| `Skyline.DataMiner.Dev.Common` | DataMiner SDK |
| `Skyline.DataMiner.SDM` | SDM DOM abstractions |

### Package project (`SDM<DomainName>.Package`)

| Package | Purpose |
|---------|---------|
| `Skyline.DataMiner.Dev.Automation` | Automation Script SDK |
| `Skyline.DataMiner.Utils.DOM` | DOM utilities |
| `Skyline.DataMiner.SDM.SourceGenerator.Runtime` | Source generator support |
| `Skyline.DataMiner.Utils.SecureCoding` | Secure coding helpers |

### UDAPI project (`SDM<DomainName>UDAPI`)

| Package | Purpose |
|---------|---------|
| `Skyline.DataMiner.Dev.Automation` | Automation Script SDK |
| `Skyline.DataMiner.SDM.UserDefinedApi` | UDAPI + OData support |
| `Skyline.DataMiner.Utils.SDM<DomainName>` | Generated NuGet from domain library |

### GQI project (`SDM<DomainName>GQI`)

| Package | Purpose |
|---------|---------|
| `Skyline.DataMiner.GQI.Core` | GQI data source SDK |
| `Skyline.DataMiner.Utils.SDM<DomainName>` | Domain library NuGet |

---

## Reference Implementation

The generator scripts in `AICreateSDMBackendAndUDAPI` perform this step inline:

- `C:\Users\Tim\source\repos\AICreateSDMBackendAndUDAPI\Generator\Generate-DataMinerBackend.ps1`
- `C:\Users\Tim\source\repos\AICreateSDMBackendAndUDAPI\Generator\Generate-UDAPI.ps1`

Preferable see if this can be written in dotnet instead of powershell

---

## Usage

```powershell
.\New-DevPack.ps1 -InputYaml .\ExampleInput.yaml [-OutputDir C:\temp] [-IncludeGqi]
```

See [SKILL.md](SKILL.md) for full documentation.

## TODO / Next Steps

- [x] Define the agent SKILL.md for this folder → `SKILL.md`
- [x] Extract the project scaffolding logic from `Generate-DataMinerBackend.ps1` into a standalone step → `New-DevPack.ps1`
- [x] Add support for the GQI project scaffolding → `New-DevPack.ps1 -IncludeGqi`
- [ ] Consider NuGet version pinning strategy — see note in SKILL.md; currently uses latest
