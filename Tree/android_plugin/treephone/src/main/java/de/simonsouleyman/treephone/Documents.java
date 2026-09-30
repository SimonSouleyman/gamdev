package de.simonsouleyman.treephone;

import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.ContentResolver;
import android.content.Intent;
import android.net.Uri;
import android.provider.DocumentsContract;
import android.util.Log;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;

/**
 * The save backup (0.8): Android's own "save as" and "open" pickers (Storage Access Framework,
 * ACTION_CREATE_DOCUMENT / ACTION_OPEN_DOCUMENT). No storage permission, no Google services.
 * One request at a time; the answer arrives as a signal with a result word:
 * "ok", "cancelled", "no_room", "too_big" or "failed".
 */
final class Documents {
    static final int REQUEST_CREATE = 7302;
    static final int REQUEST_OPEN = 7303;
    static final String SIGNAL_SAVED = "document_saved";
    static final String SIGNAL_OPENED = "document_opened";
    /** A copy bigger than this is not a Tree copy (the game reads it into memory). */
    private static final long MAX_BYTES = 512L * 1024 * 1024;

    private final TreePhonePlugin plugin;
    private String pendingSource;
    private String pendingDest;

    Documents(TreePhonePlugin plugin) {
        this.plugin = plugin;
    }

    /** Opens "save as" for the file at srcPath, suggesting the name. False if busy or no activity. */
    synchronized boolean create(Activity activity, String srcPath, String name, String mime) {
        if (activity == null || srcPath == null || !new File(srcPath).isFile() || busy()) {
            return false;
        }
        pendingSource = srcPath;
        Intent intent = new Intent(Intent.ACTION_CREATE_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType(mime == null || mime.isEmpty() ? "application/zip" : mime);
        intent.putExtra(Intent.EXTRA_TITLE, name);
        start(activity, intent, REQUEST_CREATE, SIGNAL_SAVED);
        return true;
    }

    /** Opens the file picker; the chosen file is copied to destPath. False if busy or no activity. */
    synchronized boolean open(Activity activity, String destPath) {
        if (activity == null || destPath == null || busy()) {
            return false;
        }
        pendingDest = destPath;
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        // File managers name a zip in different ways; the game checks the contents itself.
        intent.setType("*/*");
        intent.putExtra(Intent.EXTRA_MIME_TYPES, new String[] {
                "application/zip", "application/x-zip-compressed", "application/octet-stream" });
        start(activity, intent, REQUEST_OPEN, SIGNAL_OPENED);
        return true;
    }

    private boolean busy() {
        return pendingSource != null || pendingDest != null;
    }

    private void start(Activity activity, Intent intent, int request, String signal) {
        activity.runOnUiThread(() -> {
            try {
                activity.startActivityForResult(intent, request);
            } catch (ActivityNotFoundException e) {
                Log.w(TreePhonePlugin.TAG, "no document picker on this phone", e);
                finish(signal, "failed");
            }
        });
    }

    /** Called from the plugin's onMainActivityResult; true when the request was ours. */
    boolean onResult(Activity activity, int requestCode, int resultCode, Intent data) {
        if (requestCode != REQUEST_CREATE && requestCode != REQUEST_OPEN) {
            return false;
        }
        String signal = requestCode == REQUEST_CREATE ? SIGNAL_SAVED : SIGNAL_OPENED;
        Uri uri = data == null ? null : data.getData();
        if (resultCode != Activity.RESULT_OK || uri == null || activity == null) {
            finish(signal, "cancelled");
            return true;
        }
        ContentResolver cr = activity.getApplicationContext().getContentResolver();
        Thread worker = new Thread(() -> {
            String result = requestCode == REQUEST_CREATE ? writeTo(cr, uri) : readFrom(cr, uri);
            finish(signal, result);
        }, "TreePhone-document");
        worker.start();
        return true;
    }

    private String writeTo(ContentResolver cr, Uri uri) {
        String src;
        synchronized (this) {
            src = pendingSource;
        }
        if (src == null) {
            return "failed";
        }
        try (InputStream in = new FileInputStream(src); OutputStream os = cr.openOutputStream(uri, "wt")) {
            if (os == null) {
                throw new IOException("cannot open " + uri);
            }
            copy(in, os, Long.MAX_VALUE);
            return "ok";
        } catch (IOException | RuntimeException e) {
            Log.w(TreePhonePlugin.TAG, "saving the copy failed", e);
            // A half-written copy is worse than none.
            try {
                DocumentsContract.deleteDocument(cr, uri);
            } catch (Exception ignored) {
                // the provider may not allow it
            }
            return noRoom(e) ? "no_room" : "failed";
        }
    }

    private String readFrom(ContentResolver cr, Uri uri) {
        String dest;
        synchronized (this) {
            dest = pendingDest;
        }
        if (dest == null) {
            return "failed";
        }
        File out = new File(dest);
        File parent = out.getParentFile();
        if (parent != null && !parent.isDirectory() && !parent.mkdirs()) {
            return "failed";
        }
        try (InputStream in = cr.openInputStream(uri); OutputStream os = new FileOutputStream(out)) {
            if (in == null) {
                throw new IOException("cannot open " + uri);
            }
            copy(in, os, MAX_BYTES);
            return "ok";
        } catch (TooBig e) {
            //noinspection ResultOfMethodCallIgnored
            out.delete();
            return "too_big";
        } catch (IOException | RuntimeException e) {
            Log.w(TreePhonePlugin.TAG, "reading the copy failed", e);
            //noinspection ResultOfMethodCallIgnored
            out.delete();
            return noRoom(e) ? "no_room" : "failed";
        }
    }

    private void finish(String signal, String result) {
        synchronized (this) {
            pendingSource = null;
            pendingDest = null;
        }
        Log.i(TreePhonePlugin.TAG, signal + " " + result);
        plugin.emitLater(signal, result);
    }

    private static boolean noRoom(Throwable e) {
        for (Throwable t = e; t != null; t = t.getCause()) {
            String m = t.getMessage();
            if (m != null && (m.contains("ENOSPC") || m.toLowerCase().contains("no space"))) {
                return true;
            }
        }
        return false;
    }

    private static final class TooBig extends IOException {
        TooBig() {
            super("file too big");
        }
    }

    private static void copy(InputStream in, OutputStream os, long limit) throws IOException {
        byte[] buf = new byte[64 * 1024];
        long total = 0;
        int n;
        while ((n = in.read(buf)) > 0) {
            total += n;
            if (total > limit) {
                throw new TooBig();
            }
            os.write(buf, 0, n);
        }
        os.flush();
    }
}
