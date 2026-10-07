using LedgerCore.Application.Commands;
using LedgerCore.Application.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Application.Tests.Validation;

public class ValidationBehaviorTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_valid_command_reaches_its_handler()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();

        var result = await Dispatcher(scope).DispatchAsync(new Rename("ledger", 3), Token);

        Assert.Equal("ledger#3", result);
    }

    [Fact]
    public async Task An_invalid_command_never_reaches_its_handler()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();

        await Assert.ThrowsAsync<CommandValidationException>(
            () => Dispatcher(scope).DispatchAsync(new Rename("", 3), Token));
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<HandlerCalls>().Count);
    }

    [Fact]
    public async Task Every_error_from_every_validator_is_reported_together()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();

        var error = await Assert.ThrowsAsync<CommandValidationException>(
            () => Dispatcher(scope).DispatchAsync(new Rename("", 0), Token));

        Assert.Equal(nameof(Rename), error.Command);
        Assert.Equal(
            [new ValidationError("name", "required", "Give it a name."), new ValidationError("number", "out_of_range", "Must be 1 or more.")],
            error.Errors);
    }

    [Fact]
    public async Task A_command_without_validators_just_runs()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();

        Assert.Equal("ok", await Dispatcher(scope).DispatchAsync(new Ping(), Token));
    }

    private static ServiceProvider Provider() =>
        new ServiceCollection()
            .AddScoped<HandlerCalls>()
            .AddCommands(typeof(ValidationBehaviorTests).Assembly)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    private static ICommandDispatcher Dispatcher(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

    private sealed class HandlerCalls
    {
        public int Count { get; set; }
    }

    private sealed record Rename(string Name, int Number) : ICommand<string>;

    private sealed record Ping : ICommand<string>;

    private sealed class RenameHandler(HandlerCalls calls) : ICommandHandler<Rename, string>
    {
        public Task<string> HandleAsync(Rename command, CancellationToken cancellationToken = default)
        {
            calls.Count++;
            return Task.FromResult($"{command.Name}#{command.Number}");
        }
    }

    private sealed class PingHandler : ICommandHandler<Ping, string>
    {
        public Task<string> HandleAsync(Ping command, CancellationToken cancellationToken = default) =>
            Task.FromResult("ok");
    }

    private sealed class NameValidator : ICommandValidator<Rename>
    {
        public IEnumerable<ValidationError> Validate(Rename command)
        {
            if (string.IsNullOrEmpty(command.Name))
            {
                yield return new ValidationError("name", "required", "Give it a name.");
            }
        }
    }

    private sealed class NumberValidator : ICommandValidator<Rename>
    {
        public IEnumerable<ValidationError> Validate(Rename command)
        {
            if (command.Number < 1)
            {
                yield return new ValidationError("number", "out_of_range", "Must be 1 or more.");
            }
        }
    }
}
