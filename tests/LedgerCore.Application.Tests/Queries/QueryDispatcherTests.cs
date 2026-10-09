using LedgerCore.Application.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Application.Tests.Queries;

public class QueryDispatcherTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_query_goes_to_the_handler_found_in_the_assembly()
    {
        await using var provider = Provider(services => services.AddQueries(typeof(QueryDispatcherTests).Assembly));
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>().DispatchAsync(new Length("ledger"), Token);

        Assert.Equal(6, result);
    }

    [Fact]
    public async Task A_query_without_a_handler_is_a_bug()
    {
        await using var provider = Provider(services => services.AddScoped<IQueryDispatcher, QueryDispatcher>());
        await using var scope = provider.CreateAsyncScope();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IQueryDispatcher>().DispatchAsync(new Length("x"), Token));

        Assert.Contains(nameof(Length), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_handlers_for_one_query_is_a_bug()
    {
        await using var provider = Provider(services => services
            .AddQueries(typeof(QueryDispatcherTests).Assembly)
            .AddScoped<IQueryHandler<Length, int>, LengthHandler>());
        await using var scope = provider.CreateAsyncScope();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IQueryDispatcher>().DispatchAsync(new Length("x"), Token));

        Assert.Contains("2 handlers", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Handlers_and_the_dispatcher_are_scoped()
    {
        var services = new ServiceCollection().AddQueries(typeof(QueryDispatcherTests).Assembly);

        Assert.All(
            services.Where(d => d.ServiceType == typeof(IQueryDispatcher) || d.ImplementationType == typeof(LengthHandler)),
            d => Assert.Equal(ServiceLifetime.Scoped, d.Lifetime));
    }

    private static ServiceProvider Provider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed record Length(string Text) : IQuery<int>;

    private sealed class LengthHandler : IQueryHandler<Length, int>
    {
        public Task<int> HandleAsync(Length query, CancellationToken cancellationToken = default) =>
            Task.FromResult(query.Text.Length);
    }
}
