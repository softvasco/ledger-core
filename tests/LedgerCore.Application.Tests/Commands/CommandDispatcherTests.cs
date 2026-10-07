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

    [Fact]
    public async Task Behaviors_run_around_the_handler_in_registration_order()
    {
        var calls = new List<string>();
        var dispatcher = Dispatcher(services => services
            .AddSingleton(calls)
            .AddScoped<ICommandHandler<Add, int>, AddHandler>()
            .AddScoped<ICommandBehavior<Add, int>>(_ => new Recording("outer", calls))
            .AddScoped<ICommandBehavior<Add, int>>(_ => new Recording("inner", calls)));

        Assert.Equal(3, await dispatcher.DispatchAsync(new Add(1, 2), Token));
        Assert.Equal(["outer before", "inner before", "inner after", "outer after"], calls);
    }

    [Fact]
    public async Task A_behavior_can_answer_without_calling_the_handler()
    {
        var dispatcher = Dispatcher(services => services
            .AddScoped<ICommandHandler<Add, int>, AddHandler>()
            .AddScoped<ICommandBehavior<Add, int>, ShortCircuit>());

        Assert.Equal(-1, await dispatcher.DispatchAsync(new Add(1, 2), Token));
    }

    [Fact]
    public async Task An_open_generic_behavior_applies_to_every_command()
    {
        var calls = new List<string>();
        var dispatcher = Dispatcher(services => services
            .AddSingleton(calls)
            .AddScoped<ICommandHandler<Add, int>, AddHandler>()
            .AddScoped<ICommandHandler<Shout, string>, ShoutHandler>()
            .AddScoped(typeof(ICommandBehavior<,>), typeof(NameRecorder<,>)));

        await dispatcher.DispatchAsync(new Add(1, 2), Token);
        await dispatcher.DispatchAsync(new Shout("hi"), Token);

        Assert.Equal([nameof(Add), nameof(Shout)], calls);
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

    private sealed class Recording(string name, List<string> calls) : ICommandBehavior<Add, int>
    {
        public async Task<int> HandleAsync(Add command, Func<Task<int>> continuation, CancellationToken cancellationToken)
        {
            calls.Add($"{name} before");
            var result = await continuation();
            calls.Add($"{name} after");
            return result;
        }
    }

    private sealed class ShortCircuit : ICommandBehavior<Add, int>
    {
        public Task<int> HandleAsync(Add command, Func<Task<int>> continuation, CancellationToken cancellationToken) =>
            Task.FromResult(-1);
    }

    private sealed class NameRecorder<TCommand, TResult>(List<string> calls) : ICommandBehavior<TCommand, TResult>
        where TCommand : ICommand<TResult>
    {
        public Task<TResult> HandleAsync(TCommand command, Func<Task<TResult>> continuation, CancellationToken cancellationToken)
        {
            calls.Add(typeof(TCommand).Name);
            return continuation();
        }
    }
}
