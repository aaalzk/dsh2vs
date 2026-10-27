using Dsh2Vs.Debug;

namespace Dsh2Vs.Tests;

public class CdbParsersTests
{
    [Fact]
    public void Parses_compact_stack_shape()
    {
        const string text = """
            RetAddr               : Args to Child                                                           : Call Site
            00007ff6`54061f89     : 00001161`00000001 0000018f`55667150 00000000`00000000 : loopy!main+0x5
            00007ff6`54061e32     : 00007ff6`54069000 00007ff6`54069220 00000000`00000000 : loopy!invoke_main+0x39
            """;

        var frames = CdbParsers.ParseStack(text);

        Assert.Equal(2, frames.Count);
        Assert.Contains("loopy!main+0x5", frames[0]!.ToString());
        Assert.Contains("loopy!invoke_main+0x39", frames[1]!.ToString());
    }

    [Fact]
    public void Parses_full_stack_shape_with_source()
    {
        const string text = @"00 000000d1`2b9ff838 00007ff6`54061077     : 00000000`00000000 00000000`00000000 : loopy!accumulate+0x17 [D:\src\loopy.cpp @ 15]";

        var frames = CdbParsers.ParseStack(text);
        var frame = Assert.Single(frames);

        var json = System.Text.Json.JsonSerializer.Serialize(frame);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("loopy!accumulate+0x17", root.GetProperty("symbol").GetString());
        Assert.Equal(@"D:\src\loopy.cpp", root.GetProperty("file").GetString());
        Assert.Equal(15, root.GetProperty("line").GetInt32());
    }

    [Fact]
    public void Ignores_header_and_noise()
    {
        Assert.Empty(CdbParsers.ParseStack("RetAddr : Args to Child : Call Site"));
        Assert.Empty(CdbParsers.ParseStack("Couldn't resolve error at 'loopy!nope'"));
    }

    [Fact]
    public void Parses_modules()
    {
        const string text = """
            start             end                 module name
            00007ff6`54060000 00007ff6`54072000   loopy    C (private pdb symbols)
            00007ffd`e9860000 00007ffd`e9ac8000   ntdll    (pdb symbols)
            """;

        var modules = CdbParsers.ParseModules(text);

        Assert.Equal(2, modules.Count);
        var json = System.Text.Json.JsonSerializer.Serialize(modules[0]);
        Assert.Contains("\"name\":\"loopy\"", json);
    }

    [Fact]
    public void Parses_evaluate_expression_format()
    {
        const string text = "Evaluate expression: 42 = 00000000`0000002a";
        Assert.Equal("00000000`0000002a", CdbParsers.ParseEvaluation(text));
    }

    [Fact]
    public void Parses_cpp_evaluator_format()
    {
        const string text = "int 0n1\n0:000>";
        Assert.Equal("int 0n1", CdbParsers.ParseEvaluation(text));
    }

    [Fact]
    public void Returns_null_for_empty_evaluation()
    {
        Assert.Null(CdbParsers.ParseEvaluation("0:000>"));
    }
}
