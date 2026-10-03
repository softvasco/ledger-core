using System.Runtime.InteropServices;
using LedgerCore.Domain.Accounts;

namespace LedgerCore.Domain.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Domain_references_only_BCL_assemblies()
    {
        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        var trustedPlatformAssemblies = Assert.IsType<string>(
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"));
        var bclAssemblyNames = trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Where(path => string.Equals(
                Path.GetDirectoryName(path),
                runtimeDirectory.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nonBclReferences = typeof(Account).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is null || !bclAssemblyNames.Contains(name))
            .ToArray();

        Assert.Empty(nonBclReferences);
    }
}
