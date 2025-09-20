namespace PriceListProcessor.Tests;

/// <summary>
/// Test categories for organizing and filtering tests
/// </summary>
public static class TestCategories
{
    /// <summary>
    /// Unit tests that don't require external dependencies
    /// </summary>
    public const string Unit = "Unit";

    /// <summary>
    /// Integration tests that require external services (Redis, MinIO, etc.)
    /// </summary>
    public const string Integration = "Integration";

    /// <summary>
    /// End-to-end tests that test the complete workflow
    /// </summary>
    public const string EndToEnd = "EndToEnd";

    /// <summary>
    /// Tests that require Docker services to be running
    /// </summary>
    public const string Docker = "Docker";

    /// <summary>
    /// Tests that are slow and should be run separately
    /// </summary>
    public const string Slow = "Slow";
}
