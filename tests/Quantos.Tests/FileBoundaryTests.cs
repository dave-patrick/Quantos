using System.IO.Compression;
using Quantos.Core;
using Quantos.Core.Export;
using Quantos.Core.Markdown;
using Quantos.Core.Security;
using Quantos.Core.Storage;

namespace Quantos.Tests;

public sealed class FileBoundaryTests
{
    [Fact]
    public void Attachment_access_rejects_junctions_into_outside_folders()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows junction behavior; other platforms still test lexical containment.
        using var v = new TestVault();
        var outside = Path.Combine(v.Root, "outside-marker");
        Directory.CreateDirectory(outside);
        var junction = v.Abs("attachments/jump");
        File.WriteAllText(Path.Combine(outside, "marker.txt"), "outside marker");
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            ArgumentList = { "-NoProfile", "-Command", "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path " + Quote(junction) + " -Target " + Quote(outside) + " | Out-Null" }
        }))
        {
            Assert.NotNull(process);
            Assert.True(process.WaitForExit(15_000));
            Assert.Equal(0, process.ExitCode);
        }
        try
        {
            Assert.False(FileBoundary.ContainsFile(v.Session.Vault.Attachments, Path.Combine(junction, "marker.txt")));
            var n = v.Writes.Create(new CreateNodeRequest { Title = "Linked asset", Body = "[marker](attachments/jump/marker.txt)" }, Actor.User);
            var exported = v.Session.Exports.Publish("default", Actor.User);
            Assert.False(File.Exists(Path.Combine(v.Abs(exported.Path), "assets", "marker.txt")));
            Assert.True(File.Exists(Path.Combine(outside, "marker.txt")));
        }
        finally { Directory.Delete(junction); } // Remove only the junction, never traverse its target.
    }

    [Theory]
    [InlineData("attachments/../.state/marker.txt")]
    [InlineData("attachments/%2e%2e/.state/marker.txt")]
    [InlineData("attachments/a/../../.state/marker.txt")]
    [InlineData("attachments/a\\..\\..\\.state/marker.txt")]
    [InlineData("attachments/a.txt:stream")]
    public void Attachment_references_cannot_escape_or_address_streams(string target)
    {
        Assert.Empty(Attachments.Find($"[marker]({target})"));
    }

    [Theory]
    [InlineData(ExportFormat.Site)]
    [InlineData(ExportFormat.Markdown)]
    [InlineData(ExportFormat.Html)]
    public void Folder_and_site_exports_never_copy_internal_files_as_attachments(ExportFormat format)
    {
        using var v = new TestVault();
        const string marker = "PRIVATE-INTERNAL-MARKER";
        File.WriteAllText(v.Abs(".state/marker.txt"), marker);
        v.Writes.Create(new CreateNodeRequest { Title = "Safe note", Body = "[hidden](attachments/../.state/marker.txt)" }, Actor.User);
        var result = format == ExportFormat.Site
            ? v.Session.Exports.Publish("default", Actor.User)
            : v.Session.Exports.ExportFolder("workspaces/default", format, Actor.User);
        if (format == ExportFormat.Site)
            Assert.False(File.Exists(Path.Combine(v.Abs(result.Path), "assets", "marker.txt")));
        else
        {
            using var archive = ZipFile.OpenRead(v.Abs(result.Path));
            Assert.DoesNotContain(archive.Entries, e => e.FullName.Contains("marker.txt"));
        }
        Assert.Equal(marker, File.ReadAllText(v.Abs(".state/marker.txt")));
    }

    [Fact]
    public void Attachment_trash_rejects_a_sibling_folder_without_moving_its_file()
    {
        using var v = new TestVault();
        const string relative = "attachments-elsewhere/012345678901234567890123456789.txt";
        Directory.CreateDirectory(Path.GetDirectoryName(v.Abs(relative))!);
        File.WriteAllText(v.Abs(relative), "private marker");
        Assert.Throws<QuantosException>(() => v.Session.Attachments.TrashOrphan(relative, Actor.User));
        Assert.True(File.Exists(v.Abs(relative)));
        Assert.Empty(v.Writes.TrashBin.List());
    }
}
