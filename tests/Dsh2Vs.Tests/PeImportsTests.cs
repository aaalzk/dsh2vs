using Dsh2Vs.Core;

namespace Dsh2Vs.Tests;

public class PeImportsTests
{
    private static string SystemExe(string name) => Path.Combine(Environment.SystemDirectory, name);

    [Fact]
    public void Reads_import_table_of_a_system_dll()
    {
        var imports = PeImports.ReadImportedDlls(SystemExe("notepad.exe"));

        Assert.NotEmpty(imports);
        Assert.All(imports, name => Assert.EndsWith(".dll", name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Healthy_image_resolves_all_imports()
    {
        var (resolved, missing) = DependencyResolver.Check(SystemExe("notepad.exe"));

        Assert.NotEmpty(resolved);
        Assert.Empty(missing);
    }

    [Fact]
    public void Non_pe_input_returns_no_imports_instead_of_throwing()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"not-a-pe-{Guid.NewGuid():N}.dll");
        File.WriteAllText(temp, "this is not a PE image");
        try
        {
            Assert.Empty(PeImports.ReadImportedDlls(temp));
        }
        finally
        {
            File.Delete(temp);
        }
    }
}

