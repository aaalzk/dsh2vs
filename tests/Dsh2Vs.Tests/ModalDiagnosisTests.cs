using Dsh2Vs.Debug;

namespace Dsh2Vs.Tests;

public class ModalDiagnosisTests
{
    [Fact]
    public void No_windows_means_no_diagnosis()
    {
        Assert.Null(ModalDiagnosis.From([]));
    }

    [Fact]
    public void Recognises_the_crt_assertion_dialog()
    {
        var windows = new[]
        {
            new ModalWindow(
                "Microsoft Visual C++ Runtime Library",
                "#32770",
                [
                    "Debug Assertion Failed!",
                    "Program: D:\\build\\bin\\Debug\\VerifyServer.exe",
                    "File: d:\\Program Files\\Microsoft Visual Studio\\2022\\Community\\VC\\Tools\\MSVC\\14.44.35207\\include\\vector",
                    "Line: 1931",
                    "Expression: vector subscript out of range",
                ]),
        };

        var diagnosis = ModalDiagnosis.From(windows);

        Assert.NotNull(diagnosis);
        Assert.Equal("crt-assertion-dialog", diagnosis!.Kind);
        Assert.Equal("Microsoft Visual C++ Runtime Library", diagnosis.WindowTitle);
        Assert.Contains("vector subscript out of range", diagnosis.AssertionText);
        Assert.Contains("NOT a deadlock", diagnosis.Summary);
    }

    [Fact]
    public void Recognises_a_generic_modal_dialog()
    {
        var windows = new[]
        {
            new ModalWindow("Confirm", "#32770", ["Continue anyway?"]),
        };

        var diagnosis = ModalDiagnosis.From(windows);

        Assert.NotNull(diagnosis);
        Assert.Equal("modal-dialog", diagnosis!.Kind);
        Assert.Contains("Confirm", diagnosis.Summary);
    }

    [Fact]
    public void Prefers_a_real_dialog_over_an_incidental_window()
    {
        var windows = new[]
        {
            new ModalWindow("Console", "ConsoleWindowClass", []),
            new ModalWindow("Assertion", "#32770", ["Debug Assertion Failed!"]),
        };

        var diagnosis = ModalDiagnosis.From(windows);

        Assert.NotNull(diagnosis);
        Assert.Equal("Assertion", diagnosis!.WindowTitle);
    }

    [Fact]
    public void Extracts_the_assertion_fields()
    {
        var text = "Debug Assertion Failed!\r\nExpression: vector subscript out of range\r\nLine: 1931";

        var assertion = ModalDiagnosis.ExtractAssertion(text);

        Assert.NotNull(assertion);
        Assert.Contains("Expression:", assertion);
        Assert.Contains("Line:", assertion);
    }
}
