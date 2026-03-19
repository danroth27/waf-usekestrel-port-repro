using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Sockets;

namespace WebApp.Tests;

/// <summary>
/// Demonstrates that UseKestrel(port) works on the base WebApplicationFactory
/// but does NOT work on derived factories created by WithWebHostBuilder().
/// The port parameter is silently ignored on derived factories.
/// </summary>
public class UseKestrelPortTests
{
    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static int GetServerPort(WebApplicationFactory<Program> factory)
    {
        var server = factory.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()!.Addresses;
        Assert.NotEmpty(addresses);
        return new Uri(addresses.First()).Port;
    }

    /// <summary>
    /// UseKestrel(port) on the base factory correctly binds to the specified port.
    /// </summary>
    [Fact]
    public async Task UseKestrel_WithSpecificPort_OnBaseFactory_Works()
    {
        var expectedPort = FindFreePort();
        await using var factory = new WebApplicationFactory<Program>();
        factory.UseKestrel(expectedPort);

        var actualPort = GetServerPort(factory);

        Assert.Equal(expectedPort, actualPort); // ✅ Works on base factory
    }

    /// <summary>
    /// UseKestrel(port) on a derived factory (from WithWebHostBuilder) silently ignores
    /// the port parameter. The server binds to Kestrel's default port instead.
    /// This is the bug: most real-world usage requires WithWebHostBuilder to override services.
    /// </summary>
    [Fact]
    public async Task UseKestrel_WithSpecificPort_OnDerivedFactory_PortIsIgnored()
    {
        var expectedPort = FindFreePort();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(_ => { }); // creates DelegatedWebApplicationFactory
        factory.UseKestrel(expectedPort);

        var actualPort = GetServerPort(factory);

        // BUG: port is silently ignored — server listens on Kestrel default (5000) instead
        Assert.NotEqual(expectedPort, actualPort); // demonstrates the bug
    }

    /// <summary>
    /// UseKestrel(0) on a derived factory should assign a dynamic port,
    /// but the port parameter is ignored so the server uses Kestrel's default (5000).
    /// </summary>
    [Fact]
    public async Task UseKestrel_WithDynamicPort_OnDerivedFactory_FallsBackToDefault()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(_ => { });
        factory.UseKestrel(0); // should pick a random port

        var actualPort = GetServerPort(factory);

        // BUG: dynamic port (0) is ignored — defaults to 5000
        Assert.Equal(5000, actualPort); // demonstrates the bug
    }
}
