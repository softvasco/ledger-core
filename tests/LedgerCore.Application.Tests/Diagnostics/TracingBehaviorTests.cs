using System.Collections.Concurrent;
using System.Diagnostics;
using LedgerCore.Application.Commands;
using LedgerCore.Application.Diagnostics;
using LedgerCore.Application.Validation;
using LedgerCore.Domain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Application.Tests.Diagnostics;

public sealed class TracingBehaviorTests : IDisposable
{
    private readonly ConcurrentQueue<Activity> _stopped = new();
    private readonly ActivityListener _listener;
    private readonly Activity _test = new("test");

    public TracingBehaviorTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == LedgerTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
        _test.Start();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_command_runs_inside_a_span_named_after_it()
    {
        await Dispatch(new Charge(10));

        var span = Assert.Single(Spans());
        Assert.Equal(nameof(Charge), span.DisplayName);
        Assert.Equal(nameof(Charge), span.GetTagItem(CommandTags.Command));
        Assert.Equal(CommandOutcome.Succeeded, span.GetTagItem(CommandTags.Outcome));
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }

    [Fact]
    public async Task A_rejected_command_is_tagged_with_the_error_code_but_is_not_an_error()
    {
        await Dispatch(new Charge(500));

        var span = Assert.Single(Spans());
        Assert.Equal(CommandOutcome.Rejected, span.GetTagItem(CommandTags.Outcome));
        Assert.Equal("card.limit", span.GetTagItem(CommandTags.ErrorCode));
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }

    [Fact]
    public async Task An_invalid_command_is_tagged_invalid_and_is_not_an_error()
    {
        await Assert.ThrowsAsync<CommandValidationException>(() => Dispatch(new Charge(0)));

        var span = Assert.Single(Spans());
        Assert.Equal(CommandOutcome.Invalid, span.GetTagItem(CommandTags.Outcome));
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }

    [Fact]
    public async Task A_command_that_throws_marks_the_span_as_an_error_with_the_exception()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => Dispatch(new Charge(-1)));

        var span = Assert.Single(Spans());
        Assert.Equal(CommandOutcome.Failed, span.GetTagItem(CommandTags.Outcome));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Contains(span.Events, e => e.Name == "exception");
    }

    public void Dispose()
    {
        _test.Dispose();
        _listener.Dispose();
    }

    // tests run in parallel, so only look at spans from this test's trace
    private Activity[] Spans() => [.. _stopped.Where(a => a.TraceId == _test.TraceId)];

    private static async Task<Result<int>> Dispatch(Charge command)
    {
        await using var provider = new ServiceCollection()
            .AddCommands(typeof(TracingBehaviorTests).Assembly)
            .BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().DispatchAsync(command, Token);
    }

    private sealed record Charge(int Amount) : ICommand<Result<int>>;

    private sealed class ChargeHandler : ICommandHandler<Charge, Result<int>>
    {
        public Task<Result<int>> HandleAsync(Charge command, CancellationToken cancellationToken = default) =>
            command.Amount switch
            {
                < 0 => throw new TimeoutException("The card network did not answer."),
                > 100 => Task.FromResult(Result.Failure<int>(new DomainError("card.limit", "Over the limit."))),
                _ => Task.FromResult(Result.Success(command.Amount)),
            };
    }

    private sealed class ChargeValidator : ICommandValidator<Charge>
    {
        public IEnumerable<ValidationError> Validate(Charge command)
        {
            if (command.Amount == 0)
            {
                yield return new ValidationError("amount", "required", "Charge something.");
            }
        }
    }
}
