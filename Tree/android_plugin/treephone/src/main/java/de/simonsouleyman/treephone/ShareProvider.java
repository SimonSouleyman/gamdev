package de.simonsouleyman.treephone;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;

import java.io.File;
import java.io.FileNotFoundException;
import java.io.IOException;

/**
 * Hands one shared file (a Polaroid or the month's video) to the app the player picked in the
 * share sheet, read-only. A tiny replacement for androidx FileProvider (plain Android, no extra
 * libraries): it serves only files directly inside the cache folder "share", by name.
 * Authority: the app's package name + ".treeshare".
 */
public class ShareProvider extends ContentProvider {
    static final String DIR = "share";

    static String authority(Context ctx) {
        return ctx.getPackageName() + ".treeshare";
    }

    static File folder(Context ctx) {
        return new File(ctx.getCacheDir(), DIR);
    }

    static Uri uriFor(Context ctx, File file) {
        return new Uri.Builder().scheme("content").authority(authority(ctx)).appendPath(file.getName()).build();
    }

    @Override
    public boolean onCreate() {
        return true;
    }

    /** The file for a uri, or null when it names anything outside the share folder. */
    private File fileFor(Uri uri) {
        Context ctx = getContext();
        if (ctx == null || uri.getPathSegments().size() != 1) {
            return null;
        }
        try {
            File dir = folder(ctx).getCanonicalFile();
            File f = new File(dir, uri.getLastPathSegment()).getCanonicalFile();
            return dir.equals(f.getParentFile()) && f.isFile() ? f : null;
        } catch (IOException e) {
            return null;
        }
    }

    @Override
    public String getType(Uri uri) {
        String name = uri.getLastPathSegment() == null ? "" : uri.getLastPathSegment().toLowerCase();
        if (name.endsWith(".png")) {
            return "image/png";
        }
        if (name.endsWith(".jpg") || name.endsWith(".jpeg")) {
            return "image/jpeg";
        }
        if (name.endsWith(".mp4")) {
            return "video/mp4";
        }
        return "application/octet-stream";
    }

    @Override
    public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        if (mode != null && !mode.equals("r")) {
            throw new FileNotFoundException("read-only");
        }
        File f = fileFor(uri);
        if (f == null) {
            throw new FileNotFoundException(String.valueOf(uri));
        }
        return ParcelFileDescriptor.open(f, ParcelFileDescriptor.MODE_READ_ONLY);
    }

    @Override
    public Cursor query(Uri uri, String[] projection, String selection, String[] selectionArgs, String sortOrder) {
        File f = fileFor(uri);
        String[] cols = projection == null ? new String[] { OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE } : projection;
        MatrixCursor c = new MatrixCursor(cols, 1);
        if (f == null) {
            return c;
        }
        Object[] row = new Object[cols.length];
        for (int i = 0; i < cols.length; i++) {
            if (OpenableColumns.DISPLAY_NAME.equals(cols[i])) {
                row[i] = f.getName();
            } else if (OpenableColumns.SIZE.equals(cols[i])) {
                row[i] = f.length();
            }
        }
        c.addRow(row);
        return c;
    }

    @Override
    public Uri insert(Uri uri, ContentValues values) {
        return null;
    }

    @Override
    public int delete(Uri uri, String selection, String[] selectionArgs) {
        return 0;
    }

    @Override
    public int update(Uri uri, ContentValues values, String selection, String[] selectionArgs) {
        return 0;
    }
}
