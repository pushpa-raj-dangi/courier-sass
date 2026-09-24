using Xunit;

namespace Courier.Api.Tests;

// Test classes in this collection run only after every parallel collection has finished (xUnit 2
// schedules DisableParallelization collections last, one at a time). Use it for tests that mutate
// process-wide shared state other tests depend on — e.g. PlatformSettingsJwtRotationTests rotates
// the real Jwt.SigningKey and broadcasts it over Redis, invalidating every concurrently running
// test's access tokens.
[CollectionDefinition(Name, DisableParallelization = true)]
public class NonParallelCollection
{
    public const string Name = "NonParallel";
}
