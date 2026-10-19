using Quantos.Core;
using Quantos.Core.Security;
using Quantos.Core.Tasks;
using TaskStatus = Quantos.Core.Tasks.TaskStatus;

namespace Quantos.Tests;

public class SubtaskTests
{
    [Fact]
    public void Completing_a_task_completes_its_open_subtasks_all_the_way_down()
    {
        using var v = new TestVault();
        var t = v.Session.Tasks;
        var parent = t.Create(new CreateTaskRequest { Title = "Launch" }, Actor.User);
        var child = t.Create(new CreateTaskRequest { Title = "Write copy", Parent = parent.Id }, Actor.User);
        var grandchild = t.Create(new CreateTaskRequest { Title = "Draft headline", Parent = child.Id }, Actor.User);
        var cancelled = t.Create(new CreateTaskRequest { Title = "Old idea", Parent = parent.Id }, Actor.User);
        t.SetStatus(cancelled.Id, TaskStatus.Cancelled, Actor.User);

        t.SetStatus(parent.Id, TaskStatus.Done, Actor.User);
        Assert.Equal(TaskStatus.Done, t.Get(child.Id)!.Status);
        Assert.Equal(TaskStatus.Done, t.Get(grandchild.Id)!.Status);
        Assert.Equal(TaskStatus.Cancelled, t.Get(cancelled.Id)!.Status);

        t.SetStatus(parent.Id, TaskStatus.Todo, Actor.User);
        Assert.Equal(TaskStatus.Done, t.Get(child.Id)!.Status);
    }
}
