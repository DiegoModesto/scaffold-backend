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

    [Fact]
    public void UpdateDetails_UpdatesAllFields()
    {
        Group group = Group.Create(Guid.NewGuid(), "Old", "Old desc");
        Guid newEntra = Guid.NewGuid();

        group.UpdateDetails("New", "New desc", newEntra);

        group.Name.ShouldBe("New");
        group.Description.ShouldBe("New desc");
        group.EntraGroupId.ShouldBe(newEntra);
    }

    [Fact]
    public void Delete_SetsIsDeletedAndDeletedAt()
    {
        Group group = Group.Create(Guid.NewGuid(), "Eng", "Eng team");

        group.Delete();

        group.IsDeleted.ShouldBeTrue();
        group.DeletedAt.ShouldNotBeNull();
    }
}
