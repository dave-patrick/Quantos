using System.IO.Compression;
using Quantos.Client;

namespace Quantos.Client.Tests;

public class PackedFoldersTests
{
    [Fact]
    public void Unpacks_subfolders_once_and_again_when_the_zip_changes_but_never_outside_them()
    {
        var app = Path.Combine(Path.GetTempPath(), "quantos-packed", Guid.NewGuid().ToString("N"), "application");
        Directory.CreateDirectory(app);
        try
        {
            File.WriteAllText(Path.Combine(app, "Quantos.exe"), "program");
            void Pack(string moduleText)
            {
                var zip = Path.Combine(app, PackedFolders.ZipName);
                if (File.Exists(zip)) File.Delete(zip);
                using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
                using (var w = new StreamWriter(archive.CreateEntry("runtimes/win/lib/net10.0/Modules/Mod/Mod.psd1").Open())) w.Write(moduleText);
                using (var w = new StreamWriter(archive.CreateEntry("../outside.txt").Open())) w.Write("escape");
                using (var w = new StreamWriter(archive.CreateEntry("Quantos.exe").Open())) w.Write("replaced");
            }
            var module = Path.Combine(app, "runtimes", "win", "lib", "net10.0", "Modules", "Mod", "Mod.psd1");

            Pack("v1");
            Assert.True(PackedFolders.EnsureUnpacked(app));
            Assert.Equal("v1", File.ReadAllText(module));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(app)!, "outside.txt")));
            Assert.Equal("program", File.ReadAllText(Path.Combine(app, "Quantos.exe")));

            File.WriteAllText(module, "edited");
            Assert.True(PackedFolders.EnsureUnpacked(app)); // same zip: left alone
            Assert.Equal("edited", File.ReadAllText(module));

            Pack("v2");
            Assert.True(PackedFolders.EnsureUnpacked(app)); // an update brought a new zip
            Assert.Equal("v2", File.ReadAllText(module));
        }
        finally { Directory.Delete(Path.GetDirectoryName(app)!, true); }
    }
}
