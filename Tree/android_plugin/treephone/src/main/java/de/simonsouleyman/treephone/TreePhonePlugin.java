package de.simonsouleyman.treephone;

import android.Manifest;
import android.app.Activity;
import android.app.WallpaperManager;
import android.content.Context;
import android.content.pm.PackageManager;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.os.Build;
import android.util.Log;

import org.godotengine.godot.Godot;
import org.godotengine.godot.plugin.GodotPlugin;
import org.godotengine.godot.plugin.SignalInfo;
import org.godotengine.godot.plugin.UsedByGodot;

import java.util.Collections;
import java.util.Set;

/**
 * Godot singleton "TreePhone": home-screen wallpaper and a daily local reminder.
 * Plain Android APIs only (no Google Play services).
 */
public class TreePhonePlugin extends GodotPlugin {
    static final String TAG = "TreePhone";
    private static final int REQUEST_NOTIFICATIONS = 7301;
    private static final String SIGNAL_PERMISSION = "notification_permission_result";

    public TreePhonePlugin(Godot godot) {
        super(godot);
    }

    @Override
    public String getPluginName() {
        return "TreePhone";
    }

    @Override
    public Set<SignalInfo> getPluginSignals() {
        return Collections.singleton(new SignalInfo(SIGNAL_PERMISSION, Boolean.class));
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
}
