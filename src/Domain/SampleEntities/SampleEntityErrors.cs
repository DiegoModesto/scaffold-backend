using SharedKernel;

namespace Domain.SampleEntities;

public static class SampleEntityErrors
{
    public static Error NotFound(Guid id) =>
        Error.NotFound("SampleEntity.NotFound", $"Sample entity with id '{id}' was not found.");

    public static readonly Error NameRequired =
        Error.Validation("SampleEntity.NameRequired", "Name is required.");
}
