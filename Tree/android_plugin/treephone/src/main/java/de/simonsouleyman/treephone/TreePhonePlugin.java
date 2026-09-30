package de.simonsouleyman.treephone;

import android.Manifest;
import android.app.Activity;
import android.app.WallpaperManager;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.os.Build;
import android.util.Log;

import org.godotengine.godot.Godot;
import org.godotengine.godot.plugin.GodotPlugin;
import org.godotengine.godot.plugin.SignalInfo;
import org.godotengine.godot.plugin.UsedByGodot;

import java.io.File;
import java.util.HashSet;
import java.util.Set;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Godot singleton "TreePhone": home-screen wallpaper, a daily local reminder and the month
 * time-lapse saved to the gallery.
 * Plain Android APIs only (no Google Play services).
 */
public class TreePhonePlugin extends GodotPlugin {
    static final String TAG = "TreePhone";
    private static final int REQUEST_NOTIFICATIONS = 7301;
    private static final String SIGNAL_PERMISSION = "notification_permission_result";
    private static final String SIGNAL_VIDEO = "video_saved";

    private final AtomicBoolean savingVideo = new AtomicBoolean(false);
    private static final String SIGNAL_SHARED = "shared";
    private final Documents documents = new Documents(this);
    private final AtomicBoolean sharingVideo = new AtomicBoolean(false);

    public TreePhonePlugin(Godot godot) {
        super(godot);
    }

    @Override
    public String getPluginName() {
        return "TreePhone";
    }

    @Override
    public Set<SignalInfo> getPluginSignals() {
        Set<SignalInfo> signals = new HashSet<>();
        signals.add(new SignalInfo(SIGNAL_PERMISSION, Boolean.class));
        signals.add(new SignalInfo(SIGNAL_VIDEO, Boolean.class));
        // 0.8: save backup and sharing (Documents, Sharer).
        signals.add(new SignalInfo(Documents.SIGNAL_SAVED, String.class));
        signals.add(new SignalInfo(Documents.SIGNAL_OPENED, String.class));
        signals.add(new SignalInfo(SIGNAL_SHARED, String.class));
        return signals;
    }

    private Context context() {
        Activity activity = getActivity();
        return activity == null ? null : activity.getApplicationContext();
    }

    /**
     * Sets the PNG (or JPEG) at the absolute file path as the home-screen wallpaper,
     * and also as the lock-screen wallpaper when alsoLock is true.
     */
    @UsedByGodot
    public boolean setWallpaper(String pngPath, boolean alsoLock) {
        Context ctx = context();
        if (ctx == null) {
            return false;
        }
        Bitmap bitmap = BitmapFactory.decodeFile(pngPath);
        if (bitmap == null) {
            Log.w(TAG, "setWallpaper: cannot decode " + pngPath);
            return false;
        }
        try {
            WallpaperManager wm = WallpaperManager.getInstance(ctx);
            if (!wm.isWallpaperSupported() || (Build.VERSION.SDK_INT >= 24 && !wm.isSetWallpaperAllowed())) {
                Log.w(TAG, "setWallpaper: not allowed on this device");
                return false;
            }
            if (Build.VERSION.SDK_INT >= 24) {
                int which = WallpaperManager.FLAG_SYSTEM;
                if (alsoLock) {
                    which |= WallpaperManager.FLAG_LOCK;
                }
                return wm.setBitmap(bitmap, null, true, which) != 0;
            }
            wm.setBitmap(bitmap);
            return true;
        } catch (Exception e) {
            Log.w(TAG, "setWallpaper failed", e);
            return false;
        } finally {
            bitmap.recycle();
        }
    }

    /** True when the app may post notifications (runtime permission on Android 13+). */
    @UsedByGodot
    public boolean isNotificationPermissionGranted() {
        Context ctx = context();
        return ctx != null && ReminderScheduler.canNotify(ctx);
    }

    /**
     * Shows the system "allow notifications" prompt on Android 13+. The answer arrives as the
     * signal notification_permission_result(granted). Below Android 13 it answers at once.
     */
    @UsedByGodot
    public void requestNotificationPermission() {
        Activity activity = getActivity();
        if (activity == null) {
            return;
        }
        if (Build.VERSION.SDK_INT < 33
                || activity.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED) {
            emitSignal(SIGNAL_PERMISSION, isNotificationPermissionGranted());
            return;
        }
        activity.runOnUiThread(() -> activity.requestPermissions(
                new String[] { Manifest.permission.POST_NOTIFICATIONS }, REQUEST_NOTIFICATIONS));
    }

    @Override
    public void onMainRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onMainRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == REQUEST_NOTIFICATIONS) {
            boolean granted = grantResults.length > 0 && grantResults[0] == PackageManager.PERMISSION_GRANTED;
            emitSignal(SIGNAL_PERMISSION, granted);
        }
    }

    /** Posts a reminder every day at about hour:minute (local time) until cancelDaily(). */
    @UsedByGodot
    public void scheduleDaily(int hour, int minute, String title, String text) {
        Context ctx = context();
        if (ctx != null) {
            ReminderScheduler.save(ctx, hour, minute, title, text);
            ReminderScheduler.arm(ctx);
        }
    }

    @UsedByGodot
    public void cancelDaily() {
        Context ctx = context();
        if (ctx != null) {
            ReminderScheduler.cancel(ctx);
        }
    }

    /**
     * Converts the game's Motion-JPEG AVI at the absolute path into an H.264 MP4 and puts it into
     * the gallery under Movies/album. The work (a second or more) runs on its own thread: returns
     * true once it has started, and the result arrives as the signal video_saved(ok). Returns
     * false (and emits nothing) when it cannot start: no file, or a video is being saved already.
     */
    @UsedByGodot
    public boolean saveVideoToGallery(String aviPath, String album) {
        Context ctx = context();
        if (ctx == null || aviPath == null || !new File(aviPath).isFile()) {
            Log.w(TAG, "saveVideoToGallery: no file " + aviPath);
            return false;
        }
        if (!savingVideo.compareAndSet(false, true)) {
            Log.w(TAG, "saveVideoToGallery: already saving a video");
            return false;
        }
        String folder = album == null || album.trim().isEmpty() ? "Tree" : album.trim();
        Thread worker = new Thread(() -> {
            boolean ok = false;
            try {
                ok = VideoSaver.save(ctx, aviPath, folder);
            } catch (Throwable t) {
                Log.w(TAG, "saveVideoToGallery crashed", t);
            } finally {
                savingVideo.set(false);
                boolean result = ok;
                Log.i(TAG, "saveVideoToGallery " + aviPath + " -> " + result);
                runOnRenderThread(() -> emitSignal(SIGNAL_VIDEO, result));
            }
        }, "TreePhone-video");
        worker.start();
        return true;
    }

    // --- 0.8: save backup (Storage Access Framework) and sharing (share sheet) ----------------

    /** Emits a signal on Godot's render thread (for the helper classes). */
    void emitLater(String signal, Object... args) {
        runOnRenderThread(() -> emitSignal(signal, args));
    }

    /**
     * Android's own "save as" picker for the file at srcPath (the backup zip), suggesting name.
     * Returns true once the picker opens; the answer arrives as document_saved(result) with
     * "ok", "cancelled", "no_room" or "failed".
     */
    @UsedByGodot
    public boolean createDocument(String srcPath, String name, String mime) {
        return documents.create(getActivity(), srcPath, name, mime);
    }

    /**
     * Android's own "open" picker; the chosen file is copied to destPath (absolute). Returns true
     * once the picker opens; the answer arrives as document_opened(result) with "ok",
     * "cancelled", "too_big", "no_room" or "failed".
     */
    @UsedByGodot
    public boolean openDocument(String destPath) {
        return documents.open(getActivity(), destPath);
    }

    @Override
    public void onMainActivityResult(int requestCode, int resultCode, Intent data) {
        super.onMainActivityResult(requestCode, resultCode, data);
        documents.onResult(getActivity(), requestCode, resultCode, data);
    }

    /**
     * Opens the share sheet with the file (absolute path; a Polaroid PNG). Returns "ok" once the
     * sheet is up, "no_app" when no app takes the kind, "failed" otherwise.
     */
    @UsedByGodot
    public String shareFile(String path, String mime) {
        Activity activity = getActivity();
        if (activity == null || path == null || !new File(path).isFile()) {
            return "failed";
        }
        File staged = Sharer.stage(activity.getApplicationContext(), new File(path));
        return Sharer.open(activity, staged, mime == null || mime.isEmpty() ? "image/png" : mime);
    }

    /**
     * The month time-lapse (MJPEG AVI, absolute path) as an MP4 in the share sheet. The encoding
     * runs on its own thread: returns true once started, then emits shared(result) with "ok",
     * "no_app" or "failed".
     */
    @UsedByGodot
    public boolean shareVideo(String aviPath) {
        Activity activity = getActivity();
        if (activity == null || aviPath == null || !new File(aviPath).isFile() || !sharingVideo.compareAndSet(false, true)) {
            return false;
        }
        Context ctx = activity.getApplicationContext();
        Thread worker = new Thread(() -> {
            String result = "failed";
            try {
                File dir = ShareProvider.folder(ctx);
                if (dir.isDirectory() || dir.mkdirs()) {
                    String base = new File(aviPath).getName();
                    int dot = base.lastIndexOf('.');
                    File mp4 = new File(dir, (dot > 0 ? base.substring(0, dot) : base) + ".mp4");
                    if (VideoSaver.toMp4(aviPath, mp4)) {
                        result = Sharer.open(activity, mp4, "video/mp4");
                    }
                }
            } catch (Throwable t) {
                Log.w(TAG, "shareVideo crashed", t);
            } finally {
                sharingVideo.set(false);
                emitLater(SIGNAL_SHARED, result);
            }
        }, "TreePhone-share");
        worker.start();
        return true;
    }
}
