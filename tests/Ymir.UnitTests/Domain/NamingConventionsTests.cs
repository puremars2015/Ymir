using Ymir.Platform.Infrastructure.Persistence;
using Ymir.VibeMaker.Domain;

namespace Ymir.UnitTests.Domain;

public class NamingConventionsTests
{
    [Theory]
    [InlineData("UserId", "user_id")]
    [InlineData("ClientRequestId", "client_request_id")]
    [InlineData("Id", "id")]
    [InlineData("PK_Users", "pk_users")]
    [InlineData("IX_agent_executions_status", "ix_agent_executions_status")]
    public void ToSnakeCase(string input, string expected) => Assert.Equal(expected, NamingConventions.ToSnakeCase(input));

    [Theory]
    [InlineData(RuntimeStatus.NotCreated, "NOT_CREATED")]
    [InlineData(RuntimeStatus.Busy, "BUSY")]
    public void EnumConverter_RoundTripsSaValues(RuntimeStatus status, string stored)
    {
        var converter = new UpperSnakeCaseEnumConverter<RuntimeStatus>();
        Assert.Equal(stored, converter.ConvertToProvider(status));
        Assert.Equal(status, converter.ConvertFromProvider(stored));
    }
}
