namespace OneGateApp.Tests;

static class TestPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string projectPath = Path.Combine(directory.FullName, "OneGateApp", "OneGateApp.csproj");
            if (File.Exists(projectPath))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the OneGateApp repository root.");
    }
}
