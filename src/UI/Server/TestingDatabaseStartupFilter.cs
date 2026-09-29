using ClearMeasure.Bootcamp.DataAccess.Mappings;

namespace ClearMeasure.Bootcamp.UI.Server;

/// <summary>
/// Ensures SQLite schema exists when hosting UI.Server in the <c>Testing</c> environment (integration tests).
/// </summary>
internal sealed class TestingDatabaseStartupFilter : IStartupFilter
{
    /// <summary>
    /// Serializes schema creation: parallel test fixtures share one named in-memory SQLite database, and
    /// concurrent <c>EnsureCreated</c> calls race ("table already exists").
    /// </summary>
    private static readonly Lock SchemaLock = new();

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            if (string.Equals(app.ApplicationServices.GetRequiredService<IHostEnvironment>().EnvironmentName, "Testing",
                    StringComparison.OrdinalIgnoreCase))
            {
                using var scope = app.ApplicationServices.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
                {
                    lock (SchemaLock)
                    {
                        db.Database.EnsureCreated();
                    }
                }
            }

            next(app);
        };
    }
}
