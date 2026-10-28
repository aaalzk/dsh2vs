using Dsh2Vs.Debug;

namespace Dsh2Vs.Tests;

public class ThreadStackParserTests
{
    private const string Dump = """
           0  Id: 4968.17f0 Suspend: 1 Teb: 00000006`c92e7000 Unfrozen
        Child-SP          RetAddr               Call Site
        00000006`c94f7e98 00007ffd`e7ba13ba     win32u!NtUserWaitMessage+0x14
        00000006`c94f7ea0 00007ffd`e7bb0ec7     USER32!DialogBox2+0x246
        00000006`c94f7f50 00007ff6`a7316f8     USER32!MessageBoxA+0x45
        00000006`c94f7fa0 00007ff6`a7312119     loopy!main+0x1b8
           1  Id: 4968.2eb4 Suspend: 1 Teb: 00000006`c92e9000 Unfrozen
        00000006`c92f7e98 00007ffd`e98d1234     ntdll!NtWaitForWorkViaWorkerFactory+0x14
        00000006`c92f7ea0 00007ffd`e98d5678     ntdll!TppWorkerThread+0x37e
           4  Id: 4968.3b48 Suspend: 1 Teb: 00000006`c92ef000 Unfrozen
        00000006`c92ffd48 00007ffd`e999648e     ntdll!DbgBreakPoint
        00000006`c92ffd50 00007ffd`e917dda7     ntdll!DbgUiRemoteBreakin+0x4e
        """;

    [Fact]
    public void Parses_every_thread_and_its_frames()
    {
        var threads = ThreadStackParser.Parse(Dump);

        Assert.Equal(3, threads.Count);
        Assert.Equal(0, threads[0].Index);
        Assert.Equal("4968.17f0", threads[0].SystemId);
        Assert.Contains("USER32!MessageBoxA+0x45", threads[0].Symbols);
        Assert.Contains("ntdll!DbgBreakPoint", threads[2].Symbols);
    }

    [Fact]
    public void Picks_the_thread_blocked_in_the_dialog()
    {
        var threads = ThreadStackParser.Parse(Dump);

        Assert.Equal(0, ThreadStackParser.PickInteresting(threads));
    }

    [Fact]
    public void Never_picks_the_injected_break_thread_when_another_exists()
    {
        var threads = ThreadStackParser.Parse(Dump);

        var picked = ThreadStackParser.PickInteresting(threads);

        Assert.NotEqual(4, picked);
    }

    [Fact]
    public void Falls_back_to_the_current_thread_when_the_dump_is_empty()
    {
        Assert.Equal(2, ThreadStackParser.PickInteresting([], 2));
        Assert.Null(ThreadStackParser.PickInteresting([]));
    }

    [Fact]
    public void Prefers_threads_with_real_user_code_when_no_dialog_is_present()
    {
        const string dump = """
               0  Id: 1.1 Unfrozen
            0000 0000     ntdll!DbgBreakPoint
            0000 0000     ntdll!DbgUiRemoteBreakin+0x4e
               1  Id: 1.2 Unfrozen
            0000 0000     ntdll!NtWaitForSingleObject+0x14
            0000 0000     loopy!waitForWork+0x33
            0000 0000     loopy!main+0x90
            """;

        var threads = ThreadStackParser.Parse(dump);

        Assert.Equal(1, ThreadStackParser.PickInteresting(threads));
    }
}
