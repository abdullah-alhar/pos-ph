using VetPos.Core.Infrastructure;

namespace VetPos.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void All_files_live_under_the_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "vetpos-paths-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);

        Assert.Equal(Path.Combine(root, "vetpos.db"), paths.DatabaseFile);
        Assert.StartsWith(root, paths.BackupsDir, StringComparison.Ordinal);
        Assert.StartsWith(root, paths.LogsDir, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureCreated_makes_every_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "vetpos-paths-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCreated();
            Assert.True(Directory.Exists(paths.BackupsDir));
            Assert.True(Directory.Exists(paths.LogsDir));
            Assert.True(Directory.Exists(paths.AssetsDir));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Empty_root_is_refused() => Assert.Throws<ArgumentException>(() => new AppPaths(" "));
}
