using LedgerCore.Application.Commands;
using LedgerCore.Application.Validation;
using LedgerCore.Domain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace LedgerCore.Application.Tests.Diagnostics;

public class LoggingBehaviorTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly FakeLogCollector _logs = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_command_that_succeeds_is_logged_with_its_duration()
    {
        await Dispatch(new Transfer(40));

        var line = Assert.Single(_logs.GetSnapshot());
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Equal("Transfer succeeded in 40 ms", line.Message);
    }

    [Fact]
    public async Task A_rejected_command_is_logged_with_the_error_code()
    {
        await Dispatch(new Transfer(500));

        var line = Assert.Single(_logs.GetSnapshot());
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Equal("Transfer was rejected with transfer.limit in 500 ms", line.Message);
    }

    [Fact]
    public async Task An_invalid_command_is_logged_with_the_number_of_errors()
    {
        await Assert.ThrowsAsync<CommandValidationException>(() => Dispatch(new Transfer(0)));

        var line = Assert.Single(_logs.GetSnapshot());
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Equal("Transfer was invalid with 1 errors", line.Message);
    }

    [Fact]
    public async Task A_command_that_throws_is_logged_as_an_error_with_the_exception()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => Dispatch(new Transfer(-1)));

        var line = Assert.Single(_logs.GetSnapshot());
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.IsType<TimeoutException>(line.Exception);
    }

    private async Task Dispatch(Transfer command)
    {
        await using var provider = new ServiceCollection()
            .AddSingleton<TimeProvider>(_clock)
            .AddLogging(logging => logging.AddProvider(new FakeLoggerProvider(_logs)))
            .AddCommands(typeof(LoggingBehaviorTests).Assembly)
            .BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().DispatchAsync(command, Token);
    }

    private sealed record Transfer(int Amount) : ICommand<Result<int>>;

    // the handler moves the fake clock by the amount, so the logged duration is known
    private sealed class TransferHandler(TimeProvider clock) : ICommandHandler<Transfer, Result<int>>
    {
        public Task<Result<int>> HandleAsync(Transfer command, CancellationToken cancellationToken = default)
        {
            if (command.Amount < 0)
            {
                throw new TimeoutException("The other bank did not answer.");
            }

            ((FakeTimeProvider)clock).Advance(TimeSpan.FromMilliseconds(command.Amount));
            return Task.FromResult(command.Amount > 100
                ? Result.Failure<int>(new DomainError("transfer.limit", "Over the limit."))
                : Result.Success(command.Amount));
        }
    }

    private sealed class TransferValidator : ICommandValidator<Transfer>
    {
        public IEnumerable<ValidationError> Validate(Transfer command)
        {
            if (command.Amount == 0)
            {
                yield return new ValidationError("amount", "required", "Transfer something.");
            }
        }
    }
}
