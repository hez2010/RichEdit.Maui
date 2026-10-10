package com.richedit.clipboardpeer;

import android.app.Activity;
import android.content.ClipData;
import android.content.ClipDescription;
import android.content.ClipboardManager;
import android.net.Uri;
import android.os.Bundle;
import android.widget.TextView;
import java.io.File;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

/** An independent process proving Android URI grants and the public RTF transport. */
public final class ClipboardPeer extends Activity {
    private boolean started;
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        TextView status = new TextView(this);
        status.setText("RTF clipboard interoperability fixture");
        setContentView(status);
    }
    @Override public void onWindowFocusChanged(boolean focused) {
        super.onWindowFocusChanged(focused);
        if (!focused || started) return;
        started = true;
        getWindow().getDecorView().postDelayed(this::run, 200);
    }
    private void run() {
        String result;
        try {
            ClipboardManager manager = (ClipboardManager)getSystemService(CLIPBOARD_SERVICE);
            if ("publish".equals(getIntent().getStringExtra("action"))) {
                ClipData.Item item = new ClipData.Item("External peer 日本", null, Uri.parse("content://com.richedit.clipboardpeer/rtf"));
                manager.setPrimaryClip(new ClipData(new ClipDescription("External RTF", new String[]{"application/rtf", "text/plain"}), item));
                result = "PASS published external RTF";
            } else {
                ClipData data = manager.getPrimaryClip();
                if (data == null || data.getItemCount() == 0) throw new Exception("No clipboard item");
                Uri uri = data.getItemAt(0).getUri();
                if (uri == null || !"application/rtf".equals(getContentResolver().getType(uri))) throw new Exception("Missing RTF URI");
                try (InputStream stream = getContentResolver().openInputStream(uri)) {
                    if (stream == null) throw new Exception("No RTF stream");
                    byte[] bytes = new byte[65536];
                    int count = stream.read(bytes);
                    String rtf = new String(bytes, 0, count, StandardCharsets.ISO_8859_1);
                    if (!rtf.startsWith("{\\rtf") || !rtf.contains("\\b") || !rtf.contains("library")) throw new Exception("Rich payload was lost");
                    Files.write(new File(getCacheDir(), "received.rtf").toPath(), rtf.getBytes(StandardCharsets.ISO_8859_1));
                    result = "PASS received permission-granted library RTF";
                }
            }
        } catch (Exception error) { result = "FAIL " + error.toString(); }
        try { Files.write(new File(getCacheDir(), "result.txt").toPath(), result.getBytes(StandardCharsets.UTF_8)); }
        catch (Exception error) { throw new RuntimeException(error); }
        android.util.Log.i("RtfClipboardPeer", result);
    }
}
