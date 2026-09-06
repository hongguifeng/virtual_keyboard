using VirtualKeyboard.Windows;
using Xunit;

namespace VirtualKeyboard.Windows.Tests;

public sealed class AutoStartManagerTests
{
    /// <summary>内存版 Run 键存储（替换真实注册表；写失败模拟策略禁用等异常）。</summary>
    private sealed class FakeRunKeyStore : AutoStartManager.IUserRunKeyStore
    {
        public bool StoreFails;
        public string? Command;
        public int SetCalls;
        public int DeleteCalls;

        public bool TryGetCommand(out string? command)
        {
            if (StoreFails) { command = null; return false; }
            command = Command;
            return true;
        }

        public bool TrySetCommand(string command)
        {
            if (StoreFails) return false;
            SetCalls++;
            Command = command;
            return true;
        }

        public bool TryDeleteCommand()
        {
            if (StoreFails) return false;
            DeleteCalls++;
            Command = null;
            return true;
        }
    }

    private static AutoStartManager CreateManager(FakeRunKeyStore store, string? processPath) =>
        new AutoStartManager(() => store, () => processPath);

    [Fact]
    public void EnableWritesQuotedProcessPathToRunValue()
    {
        var store = new FakeRunKeyStore();
        var manager = CreateManager(store, @"C:\Programs\kb\VirtualKeyboard.exe");

        Assert.True(manager.TrySetEnabled(true, out _));
        Assert.Equal(1, store.SetCalls);
        Assert.Equal(@"""C:\Programs\kb\VirtualKeyboard.exe""", store.Command);
    }

    [Fact]
    public void DisableDeletesRunValue()
    {
        var store = new FakeRunKeyStore { Command = @"""C:\old.exe""" };
        var manager = CreateManager(store, @"C:\kb.exe");

        Assert.True(manager.TrySetEnabled(false, out _));
        Assert.Equal(1, store.DeleteCalls);
        Assert.Null(store.Command);
    }

    [Fact]
    public void GetReportsCurrentPathValueAsEnabled()
    {
        var store = new FakeRunKeyStore { Command = @"""C:\kb.exe""" };
        var manager = CreateManager(store, @"C:\kb.exe");

        Assert.True(manager.TryGetEnabled(out bool enabled));
        Assert.True(enabled);
    }

    [Fact]
    public void GetReportsForeignPathValueAsDisabled()
    {
        // 值指向其他可执行文件：读取成功，但本应用自启项不存在（外部占用）
        var store = new FakeRunKeyStore { Command = @"""C:\some\other\app.exe""" };
        var manager = CreateManager(store, @"C:\kb.exe");

        Assert.True(manager.TryGetEnabled(out bool enabled));
        Assert.False(enabled);
    }

    [Fact]
    public void GetReportsAbsentValueAsDisabled()
    {
        var store = new FakeRunKeyStore();
        var manager = CreateManager(store, @"C:\kb.exe");

        Assert.True(manager.TryGetEnabled(out bool enabled));
        Assert.False(enabled);
    }

    [Fact]
    public void StoreReadFailurePropagatesWithoutGuessing()
    {
        var store = new FakeRunKeyStore { StoreFails = true };
        var manager = CreateManager(store, @"C:\kb.exe");

        Assert.False(manager.TryGetEnabled(out _));
    }

    [Fact]
    public void MissingProcessPathFailsEnableWithProcessPathErrorCodeWithoutWriting()
    {
        var store = new FakeRunKeyStore();
        var manager = CreateManager(store, null);

        Assert.False(manager.TrySetEnabled(true, out int errorCode));
        Assert.Equal(1, errorCode);
        Assert.Equal(0, store.SetCalls);
    }

    [Fact]
    public void RegistryWriteFailureReturnsRegistryErrorCode()
    {
        var store = new FakeRunKeyStore { StoreFails = true };
        var manager = CreateManager(store, @"C:\kb.exe");

        Assert.False(manager.TrySetEnabled(true, out int enableError));
        Assert.Equal(2, enableError);
        Assert.False(manager.TrySetEnabled(false, out int disableError));
        Assert.Equal(2, disableError);
    }
}
