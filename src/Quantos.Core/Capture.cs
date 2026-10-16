using Quantos.Core.Security;

namespace Quantos.Core;

public enum CaptureTarget { Daily, Inbox, Task }

/// <summary>
/// Quick capture (BUILD-PLAN 8E.1-2): routes text to a workspace's daily note, a new note, or a new
/// task. Appends merge on the server, so a daily note open with unsaved edits isn't overwritten.
/// </summary>
public sealed class CaptureService(WriteService writes, Tasks.TaskService tasks)
{
    public const string Heading = "## Captured";

    public Node Capture(string text, string workspace, CaptureTarget target, Actor actor)
    {
        text = text.Trim();
        if (text.Length == 0) throw new QuantosException("Nothing to capture.");
        var now = writes.Clock.Now;
        switch (target)
        {
            case CaptureTarget.Inbox:
            {
                var firstLine = text.Split('\n')[0].Trim().TrimStart('#', ' ', '-', '*');
                var title = firstLine.Length > 70 ? firstLine[..70].TrimEnd() + "…" : firstLine.Length == 0 ? $"Capture {now:yyyy-MM-dd HH:mm}" : firstLine;
                return writes.Create(new CreateNodeRequest
                {
                    Title = title, Workspace = workspace, Body = text + "\n",
                    Properties = new Dictionary<string, object?> { ["captured"] = writes.Clock.Stamp() }
                }, actor);
            }
            case CaptureTarget.Task:
                return tasks.Create(new Tasks.CreateTaskRequest { Title = text.Split('\n')[0].Trim(), Workspace = workspace, Body = text.Contains('\n') ? text[(text.IndexOf('\n') + 1)..] : "" }, actor);
            default:
            {
                var daily = writes.GetOrCreateDaily(workspace, writes.Clock.Today, actor);
                var lines = text.Split('\n');
                var entry = $"- {now:HH:mm} {lines[0].TrimEnd()}" + string.Concat(lines.Skip(1).Select(l => "\n  " + l.TrimEnd()));
                return writes.AppendToBody(daily.Id, entry, actor, Heading);
            }
        }
    }
}
