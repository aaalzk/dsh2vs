using Dsh2Vs.Build;

namespace Dsh2Vs.Tests;

public class SolutionParserTests
{
    private const string SampleSolution = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        # Visual Studio Version 17
        Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "CMakePredefinedTargets", "CMakePredefinedTargets", "{AAAA0000-0000-0000-0000-000000000001}"
        EndProject
        Project("{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}") = "hello", "hello.vcxproj", "{BBBB0000-0000-0000-0000-000000000001}"
        EndProject
        Project("{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}") = "math_util", "math_util.vcxproj", "{CCCC0000-0000-0000-0000-000000000001}"
        EndProject
        Global
        	GlobalSection(SolutionConfigurationPlatforms) = preSolution
        		Debug|x64 = Debug|x64
        		Release|x64 = Release|x64
        	EndGlobalSection
        EndGlobal
        """;

    [Fact]
    public void Skips_solution_folders_and_reads_configurations()
    {
        var dir = Directory.CreateTempSubdirectory("dsh2vs-tests-");
        try
        {
            var path = Path.Combine(dir.FullName, "sample.sln");
            File.WriteAllText(path, SampleSolution);

            var info = new SolutionParser().ParseSolution(path);

            Assert.Equal("sample", info.Name);
            Assert.Equal(2, info.Projects.Count);
            Assert.DoesNotContain(info.Projects, p => p.Name == "CMakePredefinedTargets");
            Assert.Equal(["Debug", "Release"], info.Configurations);
            Assert.Equal(["x64"], info.Platforms);
            Assert.Equal(Path.Combine(dir.FullName, "hello.vcxproj"), info.Projects[0].FullPath);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Reads_project_configurations_from_vcxproj()
    {
        const string vcxproj = """
            <Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup Label="ProjectConfigurations">
                <ProjectConfiguration Include="Debug|x64">
                  <Configuration>Debug</Configuration>
                  <Platform>x64</Platform>
                </ProjectConfiguration>
                <ProjectConfiguration Include="Release|Win32">
                  <Configuration>Release</Configuration>
                  <Platform>Win32</Platform>
                </ProjectConfiguration>
              </ItemGroup>
            </Project>
            """;

        var dir = Directory.CreateTempSubdirectory("dsh2vs-tests-");
        try
        {
            var path = Path.Combine(dir.FullName, "p.vcxproj");
            File.WriteAllText(path, vcxproj);

            var (configs, platforms) = new SolutionParser().ParseProjectConfigurations(path);

            Assert.Equal(["Debug", "Release"], configs);
            Assert.Equal(["x64", "Win32"], platforms);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
