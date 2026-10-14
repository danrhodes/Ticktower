using System.Text.Json.Nodes;
using Ticktower.Core.Models;

namespace Ticktower.Core.Storage;

/// <summary>
/// v1 to v2: adds the optional per-timer <c>alertOverride</c> and the <c>Pending</c> state for queued chain timers.
/// Both are additive and absent values mean "none", so no data needs rewriting; the bump records the schema change.
/// </summary>
public sealed class StateV1ToV2 : IMigration
{
    public int FromVersion => 1;

    public void Apply(JsonObject root) { }
}

public static class StateMigrations
{
    public static Migrator Create() => new(StateFile.CurrentVersion, new IMigration[] { new StateV1ToV2() });
}

/// <summary>settings.json v1 to v2: adds remembered window placements and the board always-on-top choice (both optional).</summary>
public sealed class SettingsV1ToV2 : IMigration
{
    public int FromVersion => 1;

    public void Apply(JsonObject root) { }
}

public static class SettingsMigrations
{
    public static Migrator Create() => new(SettingsFile.CurrentVersion, new IMigration[] { new SettingsV1ToV2() });
}
