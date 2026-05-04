using Phoenixml.Platform.Hosting;
using Xunit;

namespace Phoenixml.Platform.Tests.Hosting;

public class CommandRegistryTests
{
    private static RegisteredCommand MakeCommand(string id) =>
        new(id, $"Command {id}", null, null, null, _ => default, null);

    [Fact]
    public void RegisteredCommandIsFoundById()
    {
        var registry = new CommandRegistry();
        var cmd = MakeCommand("file.save");
        registry.Register(cmd);

        var found = registry.Find("file.save");

        Assert.Equal(cmd, found);
    }

    [Fact]
    public void FindReturnsNullForUnknownId()
    {
        var registry = new CommandRegistry();
        Assert.Null(registry.Find("does.not.exist"));
    }

    [Fact]
    public void DuplicateIdReplacesExistingCommand()
    {
        var registry = new CommandRegistry();
        var first = MakeCommand("edit.copy");
        var second = new RegisteredCommand("edit.copy", "Copy v2", "Ctrl+C", "Edit/Copy", null, _ => default, null);
        registry.Register(first);
        registry.Register(second);

        var found = registry.Find("edit.copy");

        Assert.Equal(second, found);
        Assert.Equal("Copy v2", found!.DisplayName);
    }

    [Fact]
    public void AllContainsAllRegisteredCommands()
    {
        var registry = new CommandRegistry();
        registry.Register(MakeCommand("a"));
        registry.Register(MakeCommand("b"));
        registry.Register(MakeCommand("c"));

        Assert.Equal(3, registry.All.Count);
        Assert.True(registry.All.ContainsKey("a"));
        Assert.True(registry.All.ContainsKey("b"));
        Assert.True(registry.All.ContainsKey("c"));
    }

    [Fact]
    public void RegisterThrowsOnNullCommand()
    {
        var registry = new CommandRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void FindThrowsOnNullId()
    {
        var registry = new CommandRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Find(null!));
    }
}
