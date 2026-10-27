using Quantos.Core;
using Quantos.Core.Security;
using Quantos.Core.Structure;
using Quantos.Core.Tasks;

namespace Quantos.Tests;

public class TaskSupertagTests
{
    [Fact]
    public void Default_task_properties_do_not_rekey_unrelated_supertags()
    {
        using var v = new TestVault();
        var existing = v.Session.Supertags.Create(new("reading", null, null, null, null,
            [new() { ["name"] = "Status", ["type"] = "text" }], null, null, null, null), Actor.User);
        Assert.Equal("status", Assert.Single(existing.Fields).Key);
        var tag = v.Session.Supertags.Get("task")!;
        var saved = v.Session.Supertags.Update(tag.Id, new(null, null, null, null, null,
            [new() { ["name"] = "Status", ["type"] = "status", ["options"] = Quantos.Core.Tasks.TaskStatus.All }], null, null, null, null), Actor.User);
        Assert.Equal("status", Assert.Single(saved.Fields).Key);
        Assert.Equal("status", Assert.Single(v.Session.Supertags.Get("reading")!.Fields).Key);
    }

    [Fact]
    public void Explicit_migration_includes_existing_tasks_without_an_explicit_task_tag()
    {
        using var v = new TestVault();
        var task = v.Session.Tasks.Create(new CreateTaskRequest { Title = "Call Sam" }, Actor.User);
        v.Writes.UpdateFields(task.Id, null, new Dictionary<string, object?> { ["customer_number"] = "42" }, null, Actor.User);
        var result = v.Session.Supertags.Migrate(SupertagService.TaskDefinitionId, "customer_number", "number", null, Actor.User);
        Assert.Equal(1, result.Changed);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void Popup_title_edits_update_the_source_task_line()
    {
        using var v = new TestVault();
        var note = v.Writes.Create(new CreateNodeRequest { Title = "Plan", Body = "Before\n\n- [ ] Call the bank\n\nAfter" }, Actor.User);
        var task = Assert.Single(v.Session.Tasks.List());
        v.Writes.Rename(task.Id, "Call Sam", Actor.User);
        var updated = v.Writes.Require(note.Id);
        Assert.Contains("- [ ] Call Sam <!--task:" + task.Id + "-->", updated.Body);
        Assert.Contains("Before", updated.Body);
        Assert.Contains("After", updated.Body);
    }

    [Fact]
    public void Task_fields_are_inherited_before_the_default_is_saved()
    {
        using var v = new TestVault();
        var tag = v.Session.Supertags.Create(new("followup", "task", null, null, null,
            [new() { ["name"] = "Customer", ["type"] = "text" }], null, null, null, null), Actor.User);
        Assert.Contains(tag.AllFields, f => f.Key == "due" && f.Inherited == "task");
    }

    [Fact]
    public void Existing_tasks_have_a_queryable_editable_definition_without_rewriting_files()
    {
        using var v = new TestVault();
        var task = v.Session.Tasks.Create(new CreateTaskRequest { Title = "Call the bank" }, Actor.User);
        var before = File.ReadAllText(v.Abs(task.Path));
        var tag = v.Session.Supertags.Get("task")!;
        Assert.Equal(SupertagService.TaskDefinitionId, tag.Id);
        Assert.Equal(1, tag.Count);
        Assert.Contains(tag.AllFields, f => f.Key == "due" && f.Type == "date");
        var result = v.Session.Queries.Run(new QuerySpec { Tags = ["task"] }, "default");
        Assert.Contains(result.Rows, row => row.Id == task.Id);
        Assert.Equal(before, File.ReadAllText(v.Abs(task.Path)));
        var fields = tag.Fields.Select(f => new Dictionary<string, object?> { ["name"] = f.Name, ["key"] = f.Key, ["type"] = f.Type, ["options"] = f.Options }).ToList();
        fields.Add(new() { ["name"] = "Customer", ["type"] = "text" });
        var saved = v.Session.Supertags.Update(tag.Id, new(null, null, null, null, null, fields, null, null, null, null), Actor.User);
        Assert.NotEqual(SupertagService.TaskDefinitionId, saved.Id);
        Assert.Contains(saved.AllFields, f => f.Key == "customer");
        Assert.Equal(before, File.ReadAllText(v.Abs(task.Path)));
        v.Restart();
        Assert.Contains(v.Session.Supertags.Get("task")!.AllFields, f => f.Key == "customer");
    }

    [Fact]
    public void Renaming_a_property_label_keeps_its_storage_key()
    {
        using var v = new TestVault();
        var tag = v.Session.Supertags.Get("task")!;
        var saved = v.Session.Supertags.Update(tag.Id, new(null, null, null, null, null,
            [new() { ["name"] = "Deadline", ["key"] = "due", ["type"] = "date" }], null, null, null, null), Actor.User);
        Assert.Equal("due", Assert.Single(saved.AllFields).Key);
        Assert.Throws<QuantosException>(() => v.Session.Supertags.Rename(saved.Id, "work", Actor.User));
    }
}
