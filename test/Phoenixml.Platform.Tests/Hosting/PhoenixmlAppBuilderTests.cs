using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Phoenixml.Platform.Hosting;
using Xunit;

namespace Phoenixml.Platform.Tests.Hosting;

public class PhoenixmlAppBuilderTests
{
    [Fact]
    public void CreateBuildReturnsNonNullHost()
    {
        var host = PhoenixmlAppBuilder.Create("test", []).Build();
        Assert.NotNull(host);
    }

    [Fact]
    public void BuiltHostContainsCommandRegistry()
    {
        var host = PhoenixmlAppBuilder.Create("test", []).Build();
        var registry = host.Services.GetService<CommandRegistry>();
        Assert.NotNull(registry);
    }

    [Fact]
    public void BuiltHostContainsSettingsService()
    {
        var host = PhoenixmlAppBuilder.Create($"test-{Guid.NewGuid():N}", []).Build();
        var settings = host.Services.GetService<SettingsService>();
        Assert.NotNull(settings);
    }

    [Fact]
    public void ServicesPropertyAllowsAdditionalRegistrations()
    {
        var builder = PhoenixmlAppBuilder.Create($"test-{Guid.NewGuid():N}", []);
        builder.Services.AddSingleton<string>("hello-service");
        var host = builder.Build();

        var value = host.Services.GetService<string>();
        Assert.Equal("hello-service", value);
    }

    [Fact]
    public void CommandsRegisteredBeforeBuildAreAvailableAfterBuild()
    {
        var builder = PhoenixmlAppBuilder.Create($"test-{Guid.NewGuid():N}", []);
        var cmd = new RegisteredCommand("test.cmd", "Test", null, null, null, _ => default, null);
        builder.Commands.Register(cmd);

        var host = builder.Build();
        var registry = host.Services.GetRequiredService<CommandRegistry>();

        Assert.Equal(cmd, registry.Find("test.cmd"));
    }

    [Fact]
    public void CreateThrowsOnNullAppName()
    {
        Assert.Throws<ArgumentNullException>(() => PhoenixmlAppBuilder.Create(null!, []));
    }

    [Fact]
    public void CreateThrowsOnNullArgs()
    {
        Assert.Throws<ArgumentNullException>(() => PhoenixmlAppBuilder.Create("test", null!));
    }
}
