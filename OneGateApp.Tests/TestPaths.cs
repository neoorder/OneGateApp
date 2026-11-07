namespace OneGateApp.Tests;

static class TestPaths
{
    static string AppProjectPath { get; } = GetAssemblyMetadata("OneGateAppProjectPath")
        ?? throw new InvalidOperationException("OneGateAppProjectPath metadata is missing.");
    public static string AppProjectDirectory { get; } = Path.GetDirectoryName(AppProjectPath)
        ?? throw new InvalidOperationException("Could not locate the OneGateApp project directory.");
    public static string RepositoryRoot { get; } = Directory.GetParent(AppProjectDirectory)?.FullName
        ?? throw new InvalidOperationException("Could not locate the OneGateApp repository root.");

    static string? GetAssemblyMetadata(string key)
    {
        return Attribute
            .GetCustomAttributes(typeof(TestPaths).Assembly, typeof(System.Reflection.AssemblyMetadataAttribute))
            .OfType<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(p => p.Key == key)
            ?.Value;
    }
}
