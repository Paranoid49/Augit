using Augit.Infrastructure.Terminal;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class TerminalSessionRegistryTests
{
    [TestMethod]
    public void 会话锁跨实例阻止并在释放后清理()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        string locks = temporary.GetPath("locks");
        Directory.CreateDirectory(workspace);
        TerminalSessionRegistry ownerRegistry = new(locks);
        TerminalSessionRegistry observerRegistry = new(locks);

        using TerminalSessionLease? owner = ownerRegistry.TryAcquire(workspace);
        Assert.IsNotNull(owner);
        Assert.IsTrue(observerRegistry.IsActive(workspace));
        owner.Dispose();

        Assert.IsFalse(observerRegistry.IsActive(workspace));
        Assert.IsFalse(File.Exists(observerRegistry.GetLockPath(workspace)));
    }
}
