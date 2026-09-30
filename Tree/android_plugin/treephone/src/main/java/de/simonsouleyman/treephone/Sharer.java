package de.simonsouleyman.treephone;

import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.ClipData;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.util.Log;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;

/**
 * Sharing a photo or the month's video (0.8): Android's own share sheet (ACTION_SEND with the
 * file through ShareProvider). The player picks the app; nothing is sent by the game, and no
 * text, link or name is added. Result words: "ok", "no_app", "failed".
 */
final class Sharer {
    /** Shared files older than this are cleared from the share folder. */
    private static final long KEEP_MS = 24L * 3600 * 1000;

    private Sharer() {
    }

    /** Copies the file into the share folder (unless it is there already). Null on failure. */
    static File stage(Context ctx, File src) {
        File dir = ShareProvider.folder(ctx);
        if (!dir.isDirectory() && !dir.mkdirs()) {
            return null;
        }
        clearOld(dir);
        File dest = new File(dir, src.getName());
        if (dest.getAbsolutePath().equals(src.getAbsolutePath())) {
            return dest;
        }
        try (InputStream in = new FileInputStream(src); OutputStream os = new FileOutputStream(dest)) {
            byte[] buf = new byte[64 * 1024];
            int n;
            while ((n = in.read(buf)) > 0) {
                os.write(buf, 0, n);
            }
            return dest;
        } catch (IOException e) {
            Log.w(TreePhonePlugin.TAG, "share: cannot copy " + src, e);
            //noinspection ResultOfMethodCallIgnored
            dest.delete();
            return null;
        }
    }

    private static void clearOld(File dir) {
        File[] files = dir.listFiles();
        if (files == null) {
            return;
        }
        long now = System.currentTimeMillis();
        for (File f : files) {
            if (now - f.lastModified() > KEEP_MS) {
                //noinspection ResultOfMethodCallIgnored
                f.delete();
            }
        }
    }

    /** Opens the share sheet for a file in the share folder. */
    static String open(Activity activity, File staged, String mime) {
        if (activity == null || staged == null || !staged.isFile()) {
            return "failed";
        }
        Context ctx = activity.getApplicationContext();
        Uri uri = ShareProvider.uriFor(ctx, staged);
        Intent send = new Intent(Intent.ACTION_SEND);
        send.setType(mime);
        send.putExtra(Intent.EXTRA_STREAM, uri);
        send.setClipData(ClipData.newRawUri("", uri));
        send.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        if (activity.getPackageManager().queryIntentActivities(send, 0).isEmpty()) {
            return "no_app";
        }
        // The system chooser, without a title: the picture speaks for itself.
        Intent chooser = Intent.createChooser(send, null);
        chooser.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        activity.runOnUiThread(() -> {
            try {
                activity.startActivity(chooser);
            } catch (ActivityNotFoundException e) {
                Log.w(TreePhonePlugin.TAG, "share: no chooser", e);
            }
        });
        return "ok";
    }
}
