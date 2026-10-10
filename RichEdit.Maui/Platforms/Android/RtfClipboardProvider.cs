#if ANDROID
using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using System.ComponentModel;
using System.Text;
using Uri = Android.Net.Uri;

namespace RichEdit.Maui.Platforms.Android;

/// <summary>Provides read-only, permission-granted RTF clipboard streams to Android applications.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[ContentProvider(["${applicationId}.richedit.rtf"], Name = "com.richedit.maui.RtfClipboardProvider", Exported = false, GrantUriPermissions = true)]
public sealed class RtfClipboardProvider : ContentProvider
{
    internal const string MimeType = "application/rtf";
    internal const int MaximumBytes = 64 * 1024 * 1024;
    private static string DirectoryPath => Path.Combine(global::Android.App.Application.Context.CacheDir!.AbsolutePath, "richedit-rtf-clipboard");

    internal static async Task<Uri> StoreAsync(string rtf)
    {
        if (Encoding.Latin1.GetByteCount(rtf) > MaximumBytes) throw new InvalidOperationException("The RTF clipboard payload exceeds 64 MiB.");
        Directory.CreateDirectory(DirectoryPath);
        var token = Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(Path.Combine(DirectoryPath, token + ".rtf"), rtf, Encoding.Latin1);
        // Clipboard URI grants are ephemeral. Keep recent files for readers that
        // are already transferring data; prune only this provider's own cache.
        foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.rtf"))
            if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-1))
                try { File.Delete(file); } catch (IOException) { }
        var package = global::Android.App.Application.Context.PackageName;
        return Uri.Parse($"content://{package}.richedit.rtf/{token}")!;
    }

    internal static async Task<string?> ReadAsync(ContentResolver resolver, Uri uri, CancellationToken cancellationToken = default)
    {
        if (uri.Scheme != "content") return null;
        var mime = resolver.GetType(uri);
        if (!string.Equals(mime, MimeType, StringComparison.OrdinalIgnoreCase) && !string.Equals(mime, "text/rtf", StringComparison.OrdinalIgnoreCase)) return null;
        using var stream = resolver.OpenInputStream(uri);
        if (stream is null) return null;
        using var bytes = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (await stream.ReadAsync(buffer, cancellationToken) is var count && count != 0)
        {
            if (bytes.Length + count > MaximumBytes) throw new InvalidDataException("The RTF clipboard payload exceeds 64 MiB.");
            await bytes.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        return Encoding.Latin1.GetString(bytes.GetBuffer(), 0, checked((int)bytes.Length));
    }

    private static string Resolve(Uri uri)
    {
        var context = global::Android.App.Application.Context;
        if (uri.Scheme != "content" || uri.Authority != context.PackageName + ".richedit.rtf" || uri.PathSegments is not { Count: 1 } segments ||
            !Guid.TryParseExact(segments[0], "N", out var token)) throw new Java.IO.FileNotFoundException("Unknown RTF clipboard item.");
        return Path.Combine(DirectoryPath, token.ToString("N") + ".rtf");
    }

    /// <inheritdoc />
    public override bool OnCreate() => true;
    /// <inheritdoc />
    public override string? GetType(Uri uri) { _ = Resolve(uri); return MimeType; }
    /// <inheritdoc />
    public override ParcelFileDescriptor? OpenFile(Uri uri, string mode)
    {
        if (mode != "r") throw new Java.Lang.SecurityException("RTF clipboard items are read-only.");
        return ParcelFileDescriptor.Open(new Java.IO.File(Resolve(uri)), ParcelFileMode.ReadOnly);
    }
    /// <inheritdoc />
    public override ICursor? Query(Uri uri, string[]? projection, string? selection, string[]? selectionArgs, string? sortOrder)
    {
        var file = new FileInfo(Resolve(uri));
        var columns = projection ?? [IOpenableColumns.DisplayName!, IOpenableColumns.Size!];
        var cursor = new MatrixCursor(columns);
        var row = cursor.NewRow()!;
        foreach (var column in columns)
            row.Add(column == IOpenableColumns.DisplayName ? new Java.Lang.String("Document.rtf") :
                column == IOpenableColumns.Size ? Java.Lang.Long.ValueOf(file.Length) : null);
        return cursor;
    }
    /// <inheritdoc />
    public override Uri? Insert(Uri uri, ContentValues? values) => throw new Java.Lang.SecurityException("RTF clipboard items are read-only.");
    /// <inheritdoc />
    public override int Delete(Uri uri, string? selection, string[]? selectionArgs) => throw new Java.Lang.SecurityException("RTF clipboard items are read-only.");
    /// <inheritdoc />
    public override int Update(Uri uri, ContentValues? values, string? selection, string[]? selectionArgs) => throw new Java.Lang.SecurityException("RTF clipboard items are read-only.");
}
#endif
