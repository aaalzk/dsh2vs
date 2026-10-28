using System.Runtime.InteropServices;
using System.Text;

namespace Dsh2Vs.Debug;

public sealed record ModalWindow(string Title, string ClassName, IReadOnlyList<string> Texts);

/// <summary>
/// Inspects the target process's top-level windows. A debuggee stuck in a modal dialog cannot be
/// diagnosed from its stack alone: the stack only says "MessageBoxW", while the *title and the
/// static texts* are what tell you it is a CRT assertion rather than a deadlock.
/// </summary>
public static class WindowProbe
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>Visible top-level windows owned by the process, with their static text children.</summary>
    public static IReadOnlyList<ModalWindow> Inspect(int processId)
    {
        var windows = new List<ModalWindow>();

        try
        {
            EnumWindows((handle, _) =>
            {
                GetWindowThreadProcessId(handle, out var owner);
                if (owner != (uint)processId || !IsWindowVisible(handle))
                {
                    return true;
                }

                var title = ReadText(handle);
                var className = ReadClassName(handle);
                var texts = new List<string>();

                EnumChildWindows(handle, (child, _) =>
                {
                    var text = ReadText(child).Trim();
                    if (text.Length > 0 && !texts.Contains(text))
                    {
                        texts.Add(text);
                    }

                    return true;
                }, IntPtr.Zero);

                if (title.Length > 0 || className.Equals("#32770", StringComparison.Ordinal))
                {
                    windows.Add(new ModalWindow(title, className, texts));
                }

                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // Window enumeration is best effort; never break a debugging call for it.
        }

        return windows;
    }

    private static string ReadText(IntPtr handle)
    {
        var buffer = new StringBuilder(1024);
        var length = GetWindowTextW(handle, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : string.Empty;
    }

    private static string ReadClassName(IntPtr handle)
    {
        var buffer = new StringBuilder(256);
        var length = GetClassNameW(handle, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : string.Empty;
    }
}

public sealed record ModalFinding(string Kind, string? WindowTitle, string? AssertionText, string Summary);

/// <summary>
/// Turns "the target has a dialog window" into a conclusion the caller can act on, instead of
/// handing back raw stacks and leaving the pattern matching to whoever reads it.
/// </summary>
public static class ModalDiagnosis
{
    public static ModalFinding? From(IReadOnlyList<ModalWindow> windows)
    {
        if (windows.Count == 0)
        {
            return null;
        }

        // Prefer a real dialog (#32770) over incidental windows such as a console host.
        var dialog = windows.FirstOrDefault(w => w.ClassName == "#32770")
                     ?? windows.FirstOrDefault(w => w.Texts.Count > 0)
                     ?? windows[0];

        var joined = string.Join(" ", dialog.Texts);

        if (LooksLikeCrtAssert(joined) || LooksLikeCrtAssert(dialog.Title))
        {
            return new ModalFinding(
                "crt-assertion-dialog",
                dialog.Title,
                ExtractAssertion(joined),
                $"target is blocked in a modal dialog (title '{dialog.Title}') = CRT assertion/abort, "
                + "NOT a deadlock. Confirm with the assertion text below, then fix the bug or debug_stop.");
        }

        if (joined.Contains("assert", StringComparison.OrdinalIgnoreCase))
        {
            return new ModalFinding(
                "assertion-dialog",
                dialog.Title,
                ExtractAssertion(joined),
                $"target is blocked in a modal assertion dialog (title '{dialog.Title}') = a failed assertion, "
                + "NOT a deadlock.");
        }

        return new ModalFinding(
            "modal-dialog",
            dialog.Title,
            joined.Length > 0 ? Truncate(joined, 400) : null,
            $"target is blocked in a modal dialog (title '{dialog.Title}', class '{dialog.ClassName}'). "
            + "The thread stack shows MessageBoxW/MessageBoxA waiting for input; nobody can answer it while "
            + "running unattended.");
    }

    private static bool LooksLikeCrtAssert(string text) =>
        text.Contains("Microsoft Visual C++ Runtime Library", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Debug Assertion Failed", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Expression:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Pulls "Expression: vector subscript out of range" and the file:line that goes with it.</summary>
    internal static string? ExtractAssertion(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var line in text.Split('\n', '\r'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Expression:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("File:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Line:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Module:", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(trimmed);
            }
        }

        return parts.Count > 0 ? string.Join(" | ", parts) : Truncate(text, 300);
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "...";
}

