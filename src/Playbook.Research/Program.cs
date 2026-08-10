using Playbook.Infrastructure.Research;

namespace Playbook.Research;

/// <summary>
/// Offline research CLI. Does not host the Blazor app and does not change production defaults.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var request = ResearchCliParser.Parse(args);
            var outputRoot = request.OutputRoot;
            var workbench = new ResearchWorkbench(
                workingDirectory: Directory.GetCurrentDirectory(),
                outputRoot: outputRoot);
            return await workbench.ExecuteAsync(request).ConfigureAwait(false);
        }
        catch (ResearchCliUsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }
}
