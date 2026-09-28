using System.Collections.Concurrent;

namespace ClearMeasure.Bootcamp.AcceptanceTests;

/// <summary>
/// Holds one Chromium browser per NUnit worker. Browsers are launched lazily on first use,
/// relaunched if they disconnect, and closed once by <see cref="ServerFixture"/> at the end of the run.
/// Tests isolate state by creating a new <see cref="IBrowserContext"/> per test.
/// </summary>
public static class BrowserPool
{
    private static readonly ConcurrentDictionary<string, WorkerBrowser> Browsers = new();
    private static readonly Random RandomPosition = new();

    /// <summary>
    /// Returns the connected browser owned by the current NUnit worker, launching it if needed.
    /// </summary>
    public static async Task<IBrowser> GetForCurrentWorkerAsync(bool? headless, float? slowMo)
    {
        var workerId = TestContext.CurrentContext.WorkerId ?? "default";
        var slot = Browsers.GetOrAdd(workerId, _ => new WorkerBrowser());

        await slot.Lock.WaitAsync();
        try
        {
            if (slot.Browser is { IsConnected: true })
            {
                return slot.Browser;
            }

            if (slot.Browser != null)
            {
                await CloseQuietlyAsync(slot.Browser);
            }

            int x, y;
            lock (RandomPosition)
            {
                x = RandomPosition.Next(0, 1200);
                y = RandomPosition.Next(0, 700);
            }

            slot.Browser = await ServerFixture.Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = headless,
                SlowMo = slowMo,
                Args = [$"--window-position={x},{y}", "--window-size=800,600"]
            });
            return slot.Browser;
        }
        finally
        {
            slot.Lock.Release();
        }
    }

    /// <summary>
    /// Closes every pooled browser. Called from the assembly-level OneTimeTearDown.
    /// </summary>
    public static async Task CloseAllAsync()
    {
        foreach (var slot in Browsers.Values)
        {
            if (slot.Browser != null)
            {
                await CloseQuietlyAsync(slot.Browser);
                slot.Browser = null;
            }
        }

        Browsers.Clear();
    }

    private static async Task CloseQuietlyAsync(IBrowser browser)
    {
        try
        {
            await browser.CloseAsync();
        }
        catch (Exception ex)
        {
            await TestContext.Out.WriteLineAsync($"BrowserPool: ignoring browser close failure: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed class WorkerBrowser
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public IBrowser? Browser { get; set; }
    }
}
