# `UseKestrel(port)` silently ignores port parameter on derived `WebApplicationFactory` from `WithWebHostBuilder()`

## Summary

`WebApplicationFactory<T>.UseKestrel(int port)` correctly applies the port when called on the base factory or a subclass, but silently ignores the port parameter when called on a factory derived via `WithWebHostBuilder()`. The server falls back to Kestrel's default port (5000) instead of using the specified port.

## Repro

https://github.com/danroth27/waf-usekestrel-port-repro

**WebApp/Program.cs** — minimal web app:
```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/", () => "Hello World!");
app.Run();
```

**Test demonstrating the bug:**
```csharp
[Fact]
public async Task UseKestrel_WithSpecificPort_OnDerivedFactory_PortIsIgnored()
{
    var expectedPort = FindFreePort(); // e.g., 54630
    await using var factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(_ => { }); // creates DelegatedWebApplicationFactory
    factory.UseKestrel(expectedPort);

    var server = factory.Services.GetRequiredService<IServer>();
    var addresses = server.Features.Get<IServerAddressesFeature>()!.Addresses;
    var actualPort = new Uri(addresses.First()).Port;

    Assert.Equal(expectedPort, actualPort); // FAILS: Expected 54630, Actual 5000
}
```

The same code works correctly on the base factory (without `WithWebHostBuilder`):
```csharp
[Fact]
public async Task UseKestrel_WithSpecificPort_OnBaseFactory_Works()
{
    var expectedPort = FindFreePort();
    await using var factory = new WebApplicationFactory<Program>();
    factory.UseKestrel(expectedPort);

    var actualPort = GetServerPort(factory);

    Assert.Equal(expectedPort, actualPort); // PASSES
}
```

## Steps

```bash
git clone https://github.com/danroth27/waf-usekestrel-port-repro
cd waf-usekestrel-port-repro
dotnet test
```

**Expected:** All 3 tests pass — `UseKestrel(port)` applies the port on both base and derived factories.

**Actual:** 2 of 3 tests pass. The test on the base factory passes. The two tests on derived factories (specific port + dynamic port 0) demonstrate that the port parameter is silently ignored.

## Root cause

`TryConfigureServerPort` — the method that applies `_kestrelPort` to `IServerAddressesFeature` — runs inside `CreateHost()`:

```csharp
// WebApplicationFactory.cs
protected virtual IHost CreateHost(IHostBuilder builder)
{
    var host = builder.Build();
    TryConfigureServerPort(() => GetServerAddressFeature(host)); // reads this._kestrelPort
    host.Start();
    return host;
}
```

When `WithWebHostBuilder()` creates a `DelegatedWebApplicationFactory`, it captures the **parent** factory's `CreateHost` as a delegate:

```csharp
// DelegatedWebApplicationFactory
protected override IHost CreateHost(IHostBuilder builder) => _createHost(builder);
// _createHost was captured from the parent: `CreateHost` (parent's method)
```

When the derived factory starts the server, `ConfigureHostBuilder` calls `CreateHost(hostBuilder)`, which delegates to the **parent's** `CreateHost`. Inside that method, `TryConfigureServerPort` reads `this._kestrelPort` — but `this` is the **parent** factory instance. Since `UseKestrel(port)` was called on the **derived** factory, the parent's `_kestrelPort` is null, and `TryConfigureServerPort` no-ops:

```csharp
private void TryConfigureServerPort(...)
{
    if (_kestrelPort.HasValue) // parent._kestrelPort is null → skips
    { ... }
}
```

Meanwhile, the derived factory's `ConfigureHostBuilder` does check `_useKestrel` (which IS true on the derived factory), so Kestrel is correctly configured as the server — but the port is never applied.

## Workaround

Use the subclass approach (override `ConfigureWebHost`) instead of `WithWebHostBuilder`. On a subclass, `CreateHost` is not delegated, so `TryConfigureServerPort` correctly reads the subclass's `_kestrelPort`:

```csharp
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace services as needed
        });
    }
}

// Usage:
var factory = new CustomWebApplicationFactory();
factory.UseKestrel(0); // ✅ works — dynamic port correctly assigned
var address = factory.Services.GetRequiredService<IServer>()
    .Features.Get<IServerAddressesFeature>()!.Addresses.First();
```

## Version info

- .NET SDK: 11.0.100-preview.2.26159.112
- `Microsoft.AspNetCore.Mvc.Testing`: 10.0.5 (also affects current `main` — same code path)
- OS: Windows 11
