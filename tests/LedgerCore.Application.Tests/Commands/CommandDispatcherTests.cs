using LedgerCore.Application.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Application.Tests.Commands;

public class CommandDispatcherTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_command_goes_to_the_handler_registered_for_its_type()
    {
        var dispatcher = Dispatcher(services => services.AddScoped<ICommandHandler<Add, int>, AddHandler>());

        Assert.Equal(5, await dispatcher.DispatchAsync(new Add(2, 3), Token));
    }

    [Fact]
    public async Task Each_command_type_gets_its_own_handler()
    {
        var dispatcher = Dispatcher(services => services
            .AddScoped<ICommandHandler<Add, int>, AddHandler>()
            .AddScoped<ICommandHandler<Shout, string>, ShoutHandler>());

        Assert.Equal(6, await dispatcher.DispatchAsync(new Add(4, 2), Token));
        Assert.Equal("HI", await dispatcher.DispatchAsync(new Shout("hi"), Token));
    }

    [Fact]
    public async Task A_command_without_a_handler_is_a_bug()
    {
        var dispatcher = Dispatcher(_ => { });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(new Add(1, 1), Token));

        Assert.Contains(nameof(Add), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_handlers_for_one_command_is_a_bug()
    {
        var dispatcher = Dispatcher(services => services
            .AddScoped<ICommandHandler<Add, int>, AddHandler>()
            .AddScoped<ICommandHandler<Add, int>, AddHandler>());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(new Add(1, 1), Token));

        Assert.Contains("2 handlers", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_cancellation_token_reaches_the_handler()
    {
        var dispatcher = Dispatcher(services => services.AddScoped<ICommandHandler<Shout, string>, ShoutHandler>());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => dispatcher.DispatchAsync(new Shout("hi"), cancelled.Token));
    }

    [Fact]
    public async Task A_null_command_is_refused()
    {
        var dispatcher = Dispatcher(_ => { });

        await Assert.ThrowsAsync<ArgumentNullException>(() => dispatcher.DispatchAsync<int>(null!, Token));
    }

    private static CommandDispatcher Dispatcher(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return new CommandDispatcher(services.BuildServiceProvider());
    }

    private sealed record Add(int Left, int Right) : ICommand<int>;

    private sealed record Shout(string Text) : ICommand<string>;

    private sealed class AddHandler : ICommandHandler<Add, int>
    {
        public Task<int> HandleAsync(Add command, CancellationToken cancellationToken = default) =>
            Task.FromResult(command.Left + command.Right);
    }

    private sealed class ShoutHandler : ICommandHandler<Shout, string>
    {
        public Task<string> HandleAsync(Shout command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(command.Text.ToUpperInvariant());
        }
    }
}
