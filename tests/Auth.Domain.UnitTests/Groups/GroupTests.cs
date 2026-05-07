using Auth.Domain.Groups;
using Shouldly;

namespace Auth.Domain.UnitTests.Groups;

public sealed class GroupTests
{
    [Fact]
    public void LinkEntraGroup_StoresEntraGroupId()
    {
        Group group = Group.Create(Guid.NewGuid(), "Engineers", "Engineering team");
        Guid entraGroupId = Guid.NewGuid();

        group.LinkEntraGroup(entraGroupId);

        group.EntraGroupId.ShouldBe(entraGroupId);
    }
}
