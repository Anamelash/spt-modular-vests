using ModularVests.Client.Bones;
using Xunit;

namespace ModularVests.Server.Tests;

public sealed class MountStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mv-mounts-" + Guid.NewGuid().ToString("N"));

    private string File_ => Path.Combine(_dir, "mounts.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_is_silent_and_every_pouch_mounts_by_default()
    {
        var warnings = new List<string>();
        var store = new MountStore(File_, warnings.Add);
        store.Load();
        Assert.Empty(warnings);
        Assert.False(store.TryGet("pouch", out _));
        var mount = store.Get("pouch");
        Assert.Equal([0f, 0f, 0f], mount.Position);
        Assert.Equal([90f, 0f, 0f], mount.Rotation);
    }

    [Fact]
    public void Roundtrip()
    {
        var store = new MountStore(File_);
        var mount = new MountPose { Position = [0.01f, 0f, -0.02f], Rotation = [90f, 180f, 0f] };
        store.Set("pouch", mount);
        Assert.True(store.Dirty);
        store.Save();
        Assert.False(store.Dirty);

        // the store keeps its own copy
        mount.Position[0] = 5f;

        var again = new MountStore(File_);
        again.Load();
        Assert.True(again.TryGet("pouch", out var read));
        Assert.Equal([0.01f, 0f, -0.02f], read.Position);
        Assert.Equal([90f, 180f, 0f], read.Rotation);
    }

    [Fact]
    public void Remove_returns_the_pouch_to_the_default_mount()
    {
        var store = new MountStore(File_);
        store.Set("pouch", new MountPose { Position = [1f, 2f, 3f], Rotation = [0f, 0f, 0f] });
        store.Save();
        store.Remove("pouch");
        Assert.True(store.Dirty);
        Assert.False(store.TryGet("pouch", out _));
    }

    [Fact]
    public void Malformed_entry_is_skipped_but_the_rest_loads()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, """{ "a": { "pos": [1, 2], "rot": [0, 0, 0] }, "b": { "pos": [1, 2, 3], "rot": [0, 0, 0] } }""");
        var warnings = new List<string>();
        var store = new MountStore(File_, warnings.Add);
        store.Load();
        Assert.Single(warnings);
        Assert.False(store.TryGet("a", out _));
        Assert.True(store.TryGet("b", out _));
    }

    [Fact]
    public void Shipped_mounts_load_cleanly()
    {
        var warnings = new List<string>();
        var store = new MountStore(Path.Combine(AppContext.BaseDirectory, "mounts.json"), warnings.Add);
        store.Load();
        Assert.Empty(warnings);
    }
}
