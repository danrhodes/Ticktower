using System.Text.Json.Nodes;
using Ticktower.Core.Storage;

namespace Ticktower.Tests;

public class MigratorTests
{
    private sealed class Step(int from, string marker) : IMigration
    {
        public int FromVersion => from;
        public void Apply(JsonObject root) =>
            root["trail"] = (root["trail"]?.GetValue<string>() ?? "") + marker;
    }

    private static JsonObject Doc(int version) => new() { ["version"] = version };

    [Fact]
    public void Current_version_document_is_untouched()
    {
        var m = new Migrator(3, new IMigration[] { new Step(1, "a"), new Step(2, "b") });
        var doc = Doc(3);
        Assert.False(m.Migrate(doc));
        Assert.Null(doc["trail"]);
    }

    [Fact]
    public void Old_document_runs_each_step_in_order_and_version_is_bumped()
    {
        var m = new Migrator(4, new IMigration[] { new Step(3, "c"), new Step(1, "a"), new Step(2, "b") });
        var doc = Doc(1);
        Assert.True(m.Migrate(doc));
        Assert.Equal("abc", doc["trail"]!.GetValue<string>());
        Assert.Equal(4, doc["version"]!.GetValue<int>());
    }

    [Fact]
    public void Starting_midway_only_runs_remaining_steps()
    {
        var m = new Migrator(3, new IMigration[] { new Step(1, "a"), new Step(2, "b") });
        var doc = Doc(2);
        m.Migrate(doc);
        Assert.Equal("b", doc["trail"]!.GetValue<string>());
    }

    [Fact]
    public void Newer_version_throws()
    {
        var m = new Migrator(1, Array.Empty<IMigration>());
        Assert.Throws<InvalidDataException>(() => m.Migrate(Doc(2)));
    }

    [Fact]
    public void Missing_or_invalid_version_throws()
    {
        var m = new Migrator(1, Array.Empty<IMigration>());
        Assert.Throws<InvalidDataException>(() => m.Migrate(new JsonObject()));
        Assert.Throws<InvalidDataException>(() => m.Migrate(new JsonObject { ["version"] = "one" }));
        Assert.Throws<InvalidDataException>(() => m.Migrate(Doc(0)));
    }

    [Fact]
    public void Gap_in_migration_chain_is_rejected_at_construction()
    {
        Assert.Throws<ArgumentException>(() => new Migrator(3, new IMigration[] { new Step(1, "a") }));
    }

    [Fact]
    public void Duplicate_or_surplus_migrations_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new Migrator(2, new IMigration[] { new Step(1, "a"), new Step(1, "b") }));
        Assert.Throws<ArgumentException>(() => new Migrator(2, new IMigration[] { new Step(1, "a"), new Step(2, "b") }));
    }
}
