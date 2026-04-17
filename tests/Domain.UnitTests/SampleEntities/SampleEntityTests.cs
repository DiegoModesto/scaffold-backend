using Domain.SampleEntities;
using Shouldly;

namespace Domain.UnitTests.SampleEntities;

public class SampleEntityTests
{
    [Fact]
    public void SampleEntity_Should_BeCreated_WithDefaults()
    {
        var entity = new SampleEntity
        {
            Id = Guid.NewGuid(),
            Name = "foo"
        };

        entity.Name.ShouldBe("foo");
        entity.IsDeleted.ShouldBeFalse();
    }
}
