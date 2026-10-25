using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Sync;

namespace Quantos.Mobile;

internal static class AndroidPdf
{
    private static PdfRenderer Open(VaultHost host, string path)
    {
        if (!path.StartsWith("attachments/", StringComparison.Ordinal) || !path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new QuantosException("Choose a PDF attachment.");
        var full = SyncFiles.Validate(host.Require().Vault, path);
        if (!File.Exists(full)) throw new QuantosException("The PDF was not found.", 404);
        return new PdfRenderer(ParcelFileDescriptor.Open(new Java.IO.File(full), ParcelFileMode.ReadOnly)!);
    }
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/android/pdf-info", (VaultHost host, string path) =>
        {
            using var pdf = Open(host, path);
            return new { pages = pdf.PageCount };
        });
        app.MapGet("/api/android/pdf-page", (VaultHost host, string path, int page) =>
        {
            using var pdf = Open(host, path);
            if (page < 1 || page > pdf.PageCount) throw new QuantosException("This PDF has no such page.");
            using var sheet = pdf.OpenPage(page - 1)!;
            var scale = Math.Min(1200f / Math.Max(1, sheet.Width), 1800f / Math.Max(1, sheet.Height));
            using var bitmap = Bitmap.CreateBitmap(Math.Max(1, (int)(sheet.Width * scale)), Math.Max(1, (int)(sheet.Height * scale)), Bitmap.Config.Argb8888!)!;
            using (var canvas = new Canvas(bitmap)) canvas.DrawColor(Color.White);
            sheet.Render(bitmap, null, null, PdfRenderMode.ForDisplay);
            using var output = new MemoryStream();
            bitmap.Compress(Bitmap.CompressFormat.Png!, 100, output);
            return Results.Bytes(output.ToArray(), "image/png");
        });
    }
}
