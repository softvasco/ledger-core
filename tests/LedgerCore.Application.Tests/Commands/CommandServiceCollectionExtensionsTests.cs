using LedgerCore.Application.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Application.Tests.Commands;

public class CommandServiceCollectionExtensionsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handlers_in_the_assembly_are_found_and_dispatched_to()
    {
        await using var provider = new ServiceCollection()
            .AddCommands(typeof(CommandServiceCollectionExtensionsTests).Assembly)
            .BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        Assert.Equal("tcejorp", await dispatcher.DispatchAsync(new Reverse("project"), Token));
    }

    [Fact]
    public void Abstract_and_open_generic_handlers_are_skipped()
    {
        var services = new ServiceCollection().AddCommands(typeof(CommandServiceCollectionExtensionsTests).Assembly);

        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(ReverseHandlerBase));
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(EchoHandler<>));
    }

    [Fact]
    public void Handlers_and_the_dispatcher_are_scoped()
    {
        var services = new ServiceCollection().AddCommands(typeof(CommandServiceCollectionExtensionsTests).Assembly);

        Assert.All(
            services.Where(d => d.ServiceType == typeof(ICommandDispatcher) || d.ImplementationType == typeof(ReverseHandler)),
            d => Assert.Equal(ServiceLifetime.Scoped, d.Lifetime));
    }

    private sealed record Reverse(string Text) : ICommand<string>;

    private abstract class ReverseHandlerBase : ICommandHandler<Reverse, string>
    {
        public abstract Task<string> HandleAsync(Reverse command, CancellationToken cancellationToken = default);
    }

    private sealed class ReverseHandler : ReverseHandlerBase
    {
        public override Task<string> HandleAsync(Reverse command, CancellationToken cancellationToken = default) =>
            Task.FromResult(new string([.. command.Text.Reverse()]));
    }

    private sealed class EchoHandler<T> : ICommandHandler<Echo<T>, T>
    {
        public Task<T> HandleAsync(Echo<T> command, CancellationToken cancellationToken = default) =>
            Task.FromResult(command.Value);
    }

    private sealed record Echo<T>(T Value) : ICommand<T>;
}
