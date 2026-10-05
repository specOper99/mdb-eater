using MdbConverter.Core.Models;
using MdbConverter.Core.Settings;

namespace MdbConverter.Core.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Remembers_conflict_mode_without_passwords()
    {
        var path = Path.Combine(Path.GetTempPath(), "mdb-converter-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new SettingsStore(path);
            store.Save(new AppSettings
            {
                ConflictMode = ConflictMode.Skip,
                LastOutputFolder = "/tmp/out",
                WriteJson = false,
                WritePostgresSql = true
            });

            var loaded = store.Load();
            Assert.Equal(ConflictMode.Skip, loaded.ConflictMode);
            Assert.Equal("/tmp/out", loaded.LastOutputFolder);
            Assert.False(loaded.WriteJson);
            Assert.True(loaded.WritePostgresSql);
            var json = File.ReadAllText(path);
            Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
