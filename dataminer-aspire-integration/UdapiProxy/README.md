# Skyline.DataMiner.Aspire.UdapiProxy

REST HTTP proxy that translates incoming requests to `ApiTriggerInput`, forwards them to the AutomationHost, and returns `ApiTriggerOutput` as HTTP responses. Includes Scalar OpenAPI UI when an OpenAPI spec is configured.

## Projects

| Project | Description |
|---------|-------------|
| `Skyline.DataMiner.Aspire.UdapiProxy` | The executable proxy (net10.0) |
| `Skyline.DataMiner.Aspire.UdapiProxy.Hosting` | Aspire hosting extension (`AddUdapiProxy()`) |

## Packaging

The executable package ships a self-contained publish in `tools/net10.0/` inside the `.nupkg`. The hosting extension resolves the exe from the NuGet global packages cache at runtime.

### Build & Pack

```powershell
cd src\Skyline.DataMiner.Aspire.UdapiProxy

# 1. Publish first (populates bin\Release\net10.0\publish\)
dotnet publish -c Release

# 2. Pack (picks up published output via Content items in csproj)
dotnet pack -c Release --no-build -o C:\Users\Tim\source\nugets
```

For the hosting extension:

```powershell
cd src\Skyline.DataMiner.Aspire.UdapiProxy.Hosting
dotnet pack -c Release -o C:\Users\Tim\source\nugets
```

### After updating the NuGet cache

If consumers already have the old package cached, clear it before restoring:

```powershell
Remove-Item "$env:USERPROFILE\.nuget\packages\skyline.dataminer.aspire.udapiproxy\1.0.0" -Recurse -Force
```

## How the hosting extension resolves the exe

`UdapiProxyExtensions.AddUdapiProxy()` looks for the executable in this order:

1. `builder.Configuration["UdapiProxy:ExePath"]` — explicit override in `appsettings.json`
2. `~/.nuget/packages/skyline.dataminer.aspire.udapiproxy/<version>/tools/net10.0/UdapiProxy.exe`

## Configuration (AppHost appsettings.json)

Only needed if the NuGet resolution doesn't work:

```json
{
  "UdapiProxy": {
    "ExePath": "C:/path/to/UdapiProxy.exe"
  }
}
```

## Version Bump Checklist

1. Update `<Version>` in both `.csproj` files
2. Run `dotnet publish -c Release` on the executable project
3. Run `dotnet pack -c Release --no-build -o C:\Users\Tim\source\nugets` on the executable project
4. Run `dotnet pack -c Release -o C:\Users\Tim\source\nugets` on the hosting project
5. Clear the old version from NuGet cache if needed
6. Test with `dotnet run --project <AppHost>`
