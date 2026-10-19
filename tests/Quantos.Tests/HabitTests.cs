using Quantos.Core;
using Quantos.Core.Security;

namespace Quantos.Tests;

/// <summary>Release 1.1 habit and metric tracking on daily notes.</summary>
public class HabitTests
{
    [Fact]
    public void Values_are_properties_of_the_daily_note()
    {
        using var v = new TestVault();
        var today = v.Writes.Clock.Today;
        var habits = v.Session.Habits;
        habits.Set("default", today, "exercise", true, Actor.User);
        habits.Set("default", today, "sleep", 7.5, Actor.User);
        habits.Set("default", today.AddDays(-1), "sleep", 6L, Actor.User);

        var daily = v.Writes.GetOrCreateDaily("default", today, Actor.User);
        Assert.Contains("exercise: true", daily.Content);
        Assert.Contains("sleep: 7.5", daily.Content);

        var read = habits.Read("default", today.AddDays(-3), today, ["exercise", "sleep"], Actor.User);
        Assert.Equal(2, read.Days.Count);
        Assert.Equal(true, read.Days[today.ToString("yyyy-MM-dd")]["exercise"]);

        habits.Set("default", today, "exercise", false, Actor.User);
        Assert.DoesNotContain("exercise", v.Writes.GetOrCreateDaily("default", today, Actor.User).Content);
    }

    [Fact]
    public void Clearing_a_day_without_a_note_creates_nothing_and_bad_keys_are_refused()
    {
        using var v = new TestVault();
        var past = v.Writes.Clock.Today.AddDays(-20);
        Assert.Null(v.Session.Habits.Set("default", past, "exercise", null, Actor.User));
        Assert.False(File.Exists(v.Writes.Vault.DailyPath("default", past)));
        Assert.Throws<QuantosException>(() => v.Session.Habits.Set("default", past, "title", true, Actor.User));
        Assert.Throws<QuantosException>(() => v.Session.Habits.Set("default", past, "Has Space", true, Actor.User));
        Assert.Throws<QuantosException>(() => v.Session.Habits.Set("default", v.Writes.Clock.Today.AddDays(5), "exercise", true, Actor.User));
    }
}
