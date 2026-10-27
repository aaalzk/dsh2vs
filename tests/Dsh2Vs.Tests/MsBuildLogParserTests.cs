using Dsh2Vs.Build;
using Dsh2Vs.Core;

namespace Dsh2Vs.Tests;

public class MsBuildLogParserTests
{
    [Fact]
    public void Parses_error_with_location_code_and_project()
    {
        const string line =
            @"D:\src\main.cpp(9,34): error C2039: ""does_not_exist"": is not a member of 'mathutil' [D:\build\hello.vcxproj]";

        var diagnostics = MsBuildLogParser.Parse([line]);

        var d = Assert.Single(diagnostics);
        Assert.Equal(Severity.Error, d.Severity);
        Assert.Equal("C2039", d.Code);
        Assert.Equal(@"D:\src\main.cpp", d.File);
        Assert.Equal(9, d.Line);
        Assert.Equal(34, d.Column);
        Assert.Equal(@"D:\build\hello.vcxproj", d.Project);
    }

    [Fact]
    public void Strips_parallel_build_node_prefix()
    {
        const string line = @"6>D:\src\main.cpp(9,34): error C3861: 'does_not_exist': identifier not found";

        var d = Assert.Single(MsBuildLogParser.Parse([line]));
        Assert.Equal(@"D:\src\main.cpp", d.File);
        Assert.Equal("C3861", d.Code);
    }

    [Fact]
    public void Parses_warning()
    {
        const string line = @"D:\src\math.cpp(3,5): warning C4100: unreferenced formal parameter [D:\build\math.vcxproj]";

        var d = Assert.Single(MsBuildLogParser.Parse([line]));
        Assert.Equal(Severity.Warning, d.Severity);
        Assert.Equal("C4100", d.Code);
    }

    [Fact]
    public void Deduplicates_identical_diagnostics()
    {
        const string line = @"D:\src\main.cpp(1,1): error C1083: cannot open source file [D:\build\x.vcxproj]";

        var diagnostics = MsBuildLogParser.Parse([line, line]);
        Assert.Single(diagnostics);
    }

    [Fact]
    public void Parses_msbuild_level_error_without_location()
    {
        const string line = @"MSBUILD : error MSB1009: Project file does not exist. Switch: nope.sln";

        var d = Assert.Single(MsBuildLogParser.Parse([line]));
        Assert.Equal("MSB1009", d.Code);
        Assert.Equal(0, d.Line);
    }

    [Fact]
    public void Ignores_non_diagnostic_noise()
    {
        string[] lines =
        [
            "Build started 2026-10-27 18:00:00.",
            "  hello.vcxproj -> D:\\build\\Debug\\hello.exe",
            "Time Elapsed 00:00:01.23",
        ];

        Assert.Empty(MsBuildLogParser.Parse(lines));
    }
}
