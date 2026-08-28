using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
public sealed class WindowPlacementTests
{
    [TestMethod]
    public async Task 从未显示的窗口关闭时不会写入非有限坐标()
    {
        using TemporaryDirectory temporary = new();
        string settingsPath = temporary.GetPath("settings.json");
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                using MainWindow window = new(new SettingsStore(settingsPath), new());
                window.Close();
                completion.TrySetResult(null);
            }
            catch (Exception exception)
            {
                completion.TrySetResult(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        bool stopped = thread.Join(TimeSpan.FromSeconds(1));

        Assert.IsNull(exception, exception?.ToString());
        Assert.IsTrue(stopped);
    }
}
