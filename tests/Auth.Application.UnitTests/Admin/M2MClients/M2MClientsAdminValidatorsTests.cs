using Auth.Application.Admin.M2MClients.Create;
using Shouldly;

namespace Auth.Application.UnitTests.Admin.M2MClients;

public class M2MClientsAdminValidatorsTests
{
    [Fact]
    public void Create_Should_RejectEmptyClientId()
    {
        new CreateM2MClientCommandValidator()
            .Validate(new CreateM2MClientCommand("", "Display", [])).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Create_Should_AcceptValid()
    {
        new CreateM2MClientCommandValidator()
            .Validate(new CreateM2MClientCommand("svc-a", "Display", ["api:auth"])).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Create_Should_RejectEmptyScope()
    {
        new CreateM2MClientCommandValidator()
            .Validate(new CreateM2MClientCommand("svc-a", "Display", [""])).IsValid.ShouldBeFalse();
    }
}
