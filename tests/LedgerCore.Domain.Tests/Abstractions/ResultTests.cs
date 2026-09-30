using LedgerCore.Domain.Abstractions;

namespace LedgerCore.Domain.Tests.Abstractions;

public class ResultTests
{
    private static readonly DomainError Refused = new("test.refused", "Not today.");

    [Fact]
    public void A_success_has_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Fact]
    public void A_success_carries_its_value()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void A_failure_carries_its_error()
    {
        var result = Result.Failure<int>(Refused);

        Assert.False(result.IsSuccess);
        Assert.Equal(Refused, result.Error);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_throws_with_the_error_in_the_message()
    {
        var result = Result.Failure<int>(Refused);

        var error = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("test.refused", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_needs_an_error()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
    }
}
