# Backend Builder

Orchestrates the DataMiner SDM backend as a multi-project solution (`{Name}Backend.slnx`) containing the UDAPI automation script and the installer package. The domain library (NuGet) is built separately by the DevPack Builder.

---

## Pipeline

The backend is built in 3 sequential steps, each a standalone .NET 10 single-file program:

| Step | Script | Location | Purpose |
|------|--------|----------|---------|
| 1 | `New-Backend.cs` | `dataminer-backend-builder/` | Creates the empty `{Name}Backend.slnx` |
| 2 | `New-Udapi.cs` | `dataminer-backend-builder/dataminer-udapi-builder/` | Adds UDAPI project to the backend solution |
| 3 | `New-BackendInstaller.cs` | `dataminer-backend-builder/dataminer-backend-installer/` | Adds `.Package` project with DOM + UDAPI installers |

**Prerequisite:** Run `New-DevPack.cs` (from `dataminer-devpack-builder/`) first to produce the NuGet library.

---

## Usage

```powershell
# Step 0: Build the DevPack NuGet
cd dataminer-devpack-builder
dotnet run New-DevPack.cs -- --input-yaml <path-to-yaml> --output-dir <output>

# Step 1: Create the backend solution
cd dataminer-backend-builder
dotnet run New-Backend.cs -- --input-yaml <path-to-yaml> --output-dir <output>

# Step 2: Add the UDAPI project
cd dataminer-backend-builder/dataminer-udapi-builder
dotnet run New-Udapi.cs -- --input-yaml <path-to-yaml> --output-dir <output>

# Step 3: Add the installer package
cd dataminer-backend-builder/dataminer-backend-installer
dotnet run New-BackendInstaller.cs -- --input-yaml <path-to-yaml> --output-dir <output>
```

All scripts accept:
- `-i, --input-yaml` — Path to the YAML domain model (required)
- `-o, --output-dir` — Root output directory (default: `C:\temp`)

---

## Output Structure

```
<output-dir>/
├── SDM<Name>/                    ← DevPack (NuGet library, from New-DevPack.cs)
│   ├── SDM<Name>.slnx
│   ├── SDM<Name>/               (models, helpers, DomMapper .g.cs files)
│   └── SDM<Name>.Package/       (DOM installer, builds .dmapp)
│
└── SDM<Name>Backend/             ← Backend solution (from New-Backend.cs)
    ├── SDM<Name>Backend.slnx
    ├── SDM<Name>UDAPI/          (controllers, entry point, builds automation script)
    └── SDM<Name>Backend.Package/ (DOM + UDAPI installers, builds .dmapp)
```

---

## New-Backend.cs (Step 1)

Creates an empty `{Name}Backend.slnx` solution file. Validates that the DevPack output already exists.

**Output:** `<output-dir>/{Name}Backend/{Name}Backend.slnx`

---

## New-Udapi.cs (Step 2)

Scaffolds the UDAPI automation project and adds it to the backend solution. Generates:

| File | Purpose |
|------|---------|
| `{Name}UDAPI.cs` | Entry point (`OnApiTrigger`) |
| `UserDefinedApiExtensions.cs` | DI registration for repositories |
| `ErrorResponse.cs` | JSON error model |
| `QueryParametersImpl.cs` | `IQueryParameters` implementation + converter |
| `Controllers/{Model}sController.cs` | CRUD controller per model (GET/POST/PUT/DELETE) |

Features:
- Multi-model: generates one controller per model with route `{apiRoute}/{modelName}s`
- Single-model: uses `apiRoute` directly
- Adds `GenerateOpenApi=true` to csproj for OpenAPI spec generation
- Copies `openapi.yaml` to the solution root after build

---

## New-BackendInstaller.cs (Step 3)

Scaffolds the `.Package` project and adds it to the backend solution. Generates:

| File | Purpose |
|------|---------|
| `DOM/DomInstaller.cs` | DOM module + section definitions installer |
| `DOM/{Model}.cs` | Per-model partial installer (sections, definitions) |
| `Installers/UDAPIInstaller.cs` | Registers API routes per controller |
| `{Name}Backend.Package.cs` | Package entry point (runs DOM + UDAPI installers) |

Features:
- Copies `.g.cs` DomMapper files from the DevPack's Models folder
- Multi-model: registers one UDAPI route per controller
- Checks SDM prerequisite before installation
- References the DevPack NuGet for model types

---

## Generated C# Components

| Component | Project | Purpose |
|-----------|---------|---------|
| `{Model}sController.cs` | UDAPI | CRUD REST controller with OData filter support |
| `UserDefinedApiExtensions.cs` | UDAPI | DI registration for repositories |
| `OnApiTrigger` | UDAPI | UDAPI entry point |
| `DomInstaller.cs` | Package | DOM module + section definition installer |
| `UDAPIInstaller.cs` | Package | UDAPI route registration |
| `Package.cs` | Package | Install entry point (orchestrates DOM + UDAPI) |

---

## TODO / Next Steps

- [ ] Define the agent SKILL.md for this folder
- [ ] Extract GQI generation into the generator pipeline (currently reference-only)
- [ ] Add `ref` type support to the generator (cross-model foreign keys)
- [ ] Pin NuGet package versions in the generator templates
