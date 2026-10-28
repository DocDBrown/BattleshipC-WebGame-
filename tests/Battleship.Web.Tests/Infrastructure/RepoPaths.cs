namespace Battleship.Web.Tests.Infrastructure;

internal static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string Web(params string[] parts) =>
        Path.Combine(new[] { Root, "src", "Battleship.Web" }.Concat(parts).ToArray());

    public static string AppJs => Web("wwwroot", "js", "app.js");

    public static string AppSettings => Web("wwwroot", "appsettings.json");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Battleship.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
