# Skyline.DataMiner.Aspire.DllWatcher

Monitors automation script (UDAPI) and backend (DevPack) DLL files for changes. When a backend DLL changes, it "touches" the UDAPI DLL to cascade the reload. When a UDAPI DLL changes, it signals the AutomationHost to save state and exit (Aspire restarts it automatically).

## Projects

| Project | Description |
|---------|-------------|
| `Skyline.DataMiner.Aspire.DllWatcher` | The executable watcher service (net10.0) |
| `Skyline.DataMiner.Aspire.DllWatcher.Hosting` | Aspire hosting extension (`AddDllWatcher()`) |

## Packaging

The executable package ships a self-contained publish in `tools/net10.0/` inside the `.nupkg`. The hosting extension resolves the exe from the NuGet global packages cache at runtime.

### Build & Pack

```powershell
cd src\Skyline.DataMiner.Aspire.DllWatcher

# 1. Publish first (populates bin\Release\net10.0\publish\)
dotnet publish -c Release

# 2. Pack (picks up published output via Content items in csproj)
dotnet pack -c Release --no-build -o C:\Users\Tim\source\nugets
```

For the hosting extension:

```powershell
cd src\Skyline.DataMiner.Aspire.DllWatcher.Hosting
dotnet pack -c Release -o C:\Users\Tim\source\nugets
```

### After updating the NuGet cache

If consumers already have the old package cached, clear it before restoring:

```powershell
Remove-Item "$env:USERPROFILE\.nuget\packages\skyline.dataminer.aspire.dllwatcher\1.0.0" -Recurse -Force
```

## How the hosting extension resolves the exe

`DllWatcherExtensions.AddDllWatcher()` looks for the executable in this order:

1. `builder.Configuration["DllWatcher:ExePath"]` — explicit override in `appsettings.json`
2. `~/.nuget/packages/skyline.dataminer.aspire.dllwatcher/<version>/tools/net10.0/DllWatcher.exe`

## Configuration (AppHost appsettings.json)

Only needed if the NuGet resolution doesn't work:

```json
{
  "DllWatcher": {
    "ExePath": "C:/path/to/DllWatcher.exe"
  }
}
```

## Environment Variables (set by hosting extension)

| Variable | Description |
|----------|-------------|
| `DllWatcher__AutomationHostUrl` | URL of the AutomationHost to signal on reload |
| `DllWatcher__ScriptDlls` | Semicolon-separated UDAPI DLL paths to watch |
| `DllWatcher__BackendDlls` | Semicolon-separated DevPack DLL paths to watch |

## Version Bump Checklist

1. Update `<Version>` in both `.csproj` files
2. Run `dotnet publish -c Release` on the executable project
3. Run `dotnet pack -c Release --no-build -o C:\Users\Tim\source\nugets` on the executable project
4. Run `dotnet pack -c Release -o C:\Users\Tim\source\nugets` on the hosting project
5. Clear the old version from NuGet cache if needed
6. Test with `dotnet run --project <AppHost>`
