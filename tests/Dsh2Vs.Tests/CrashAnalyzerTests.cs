using Dsh2Vs.Debug;

namespace Dsh2Vs.Tests;

public class CrashAnalyzerTests
{
    [Fact]
    public void Parses_exception_record_from_a_dump()
    {
        const string text = """
            (462c.2564): Access violation - code c0000005 (first/second chance not available)
            ExceptionAddress: 00007ff6983316ad (loopy!main+0x000000000000016d)
               ExceptionCode: c0000005 (Access violation)
              ExceptionFlags: 00000000
            """;

        var code = CrashAnalyzer.ParseExceptionCode(text);

        Assert.Equal("0xc0000005", code);
        Assert.Equal("access violation", CrashAnalyzer.DescribeException(code));
    }

    [Fact]
    public void Parses_analyze_fields()
    {
        const string text = """
            EXCEPTION_CODE: (NTSTATUS) 0xc0000409 - The system detected an overrun of a stack-based buffer
            MODULE_NAME: ZoneServer
            IMAGE_NAME:  ZoneServer.exe
            PROCESS_NAME:  ZoneServer.exe
            FAILURE_BUCKET_ID:  STACK_BUFFER_OVERRUN_c0000409_ZoneServer.exe!HandlePacket
            FAULTING_SOURCE_FILE:  d:\projects\tianyuan4590\server\src\ZoneServer.cpp
            FAULTING_SOURCE_LINE:  1234
            """;

        var report = CrashAnalyzer.Parse(text);

        Assert.Equal("0xc0000409", report.ExceptionCode);
        Assert.Equal("ZoneServer", report.ModuleName);
        Assert.Equal("ZoneServer.exe", report.ImageName);
        Assert.Equal("ZoneServer.exe", report.ProcessName);
        Assert.Contains("STACK_BUFFER_OVERRUN", report.FailureBucket);
        Assert.Equal(@"d:\projects\tianyuan4590\server\src\ZoneServer.cpp:1234", report.FaultingSource);
    }

    [Fact]
    public void Drops_wrong_symbols_verdicts()
    {
        const string text = """
            MODULE_NAME: ntdll_wrong_symbols
            IMAGE_NAME:  ntdll.wrong.symbols.dll
            EXCEPTION_CODE: (NTSTATUS) 0x5fd39a05 - <Unable to get error code text>
            """;

        var report = CrashAnalyzer.Parse(text);

        Assert.Null(report.ModuleName);
        Assert.Null(report.ImageName);
    }

    [Fact]
    public void Picks_the_first_non_system_stack_frame()
    {
        const string stack = """
            Child-SP          RetAddr               Call Site
            0000005b`6359f588 00007ffd`e7ba13ba     win32u!NtUserWaitMessage+0x14
            0000005b`6359f590 00007ffd`e7bb0ec7     USER32!DialogBox2+0x246
            0000005b`6359fb40 00007ff6`98332119     loopy!main+0x1b8
            0000005b`6359fb90 00007ff6`98331fc2     loopy!invoke_main+0x39
            """;

        Assert.Equal("loopy!main+0x1b8", CrashAnalyzer.FirstUserFrame(stack));
    }

    [Fact]
    public void Extracts_symbol_from_exception_address_line()
    {
        const string stack =
            "ExceptionAddress: 00007ff6983316ad (loopy!main+0x000000000000016d)";

        Assert.Equal("loopy!main+0x000000000000016d", CrashAnalyzer.FirstUserFrame(stack));
    }

    [Theory]
    [InlineData("0xc0000005", "access violation")]
    [InlineData("0xc0000409", "stack buffer overrun")]
    [InlineData("0xc0000374", "heap corruption")]
    [InlineData("0xe0434352", ".NET")]
    [InlineData("0x12345678", null)]
    public void Describes_common_exception_codes(string code, string? expectedFragment)
    {
        var description = CrashAnalyzer.DescribeException(code);

        if (expectedFragment is null)
        {
            Assert.Null(description);
        }
        else
        {
            Assert.Contains(expectedFragment, description);
        }
    }
}
