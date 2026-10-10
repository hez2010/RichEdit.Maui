package com.richedit.clipboardpeer;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import java.io.File;
import java.io.FileNotFoundException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

/** Test-only RTF producer with no dependency on the editor library. */
public final class PeerProvider extends ContentProvider {
    @Override public boolean onCreate() { return true; }
    @Override public String getType(Uri uri) { return "application/rtf"; }
    @Override public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        if (!"r".equals(mode) || !"/rtf".equals(uri.getPath())) throw new FileNotFoundException();
        File file = new File(getContext().getCacheDir(), "outgoing.rtf");
        try { Files.write(file.toPath(), "{\\rtf1\\ansi External {\\b peer} \\u26085?\\u26412?}".getBytes(StandardCharsets.US_ASCII)); }
        catch (Exception error) { throw new FileNotFoundException(error.toString()); }
        return ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY);
    }
    @Override public Cursor query(Uri uri, String[] projection, String selection, String[] args, String order) { return null; }
    @Override public Uri insert(Uri uri, ContentValues values) { throw new SecurityException(); }
    @Override public int update(Uri uri, ContentValues values, String selection, String[] args) { throw new SecurityException(); }
    @Override public int delete(Uri uri, String selection, String[] args) { throw new SecurityException(); }
}
