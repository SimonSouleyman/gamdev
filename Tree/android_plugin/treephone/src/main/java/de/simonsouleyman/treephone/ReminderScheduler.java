package de.simonsouleyman.treephone;

import android.Manifest;
import android.app.AlarmManager;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.os.Build;
import android.util.Log;

import java.util.Calendar;

/** Stores the daily reminder, arms the alarm for its next occurrence and posts it. */
final class ReminderScheduler {
    static final String CHANNEL_ID = "tree_daily";
    private static final String PREFS = "tree_phone_reminder";
    private static final int NOTIFICATION_ID = 7302;
    private static final int ALARM_REQUEST = 7303;
    private static final int TAP_REQUEST = 7304;

    private ReminderScheduler() {
    }

    static void save(Context ctx, int hour, int minute, String title, String text) {
        prefs(ctx).edit()
                .putBoolean("enabled", true)
                .putInt("hour", Math.max(0, Math.min(23, hour)))
                .putInt("minute", Math.max(0, Math.min(59, minute)))
                .putString("title", title)
                .putString("text", text)
                .apply();
    }

    static void cancel(Context ctx) {
        prefs(ctx).edit().putBoolean("enabled", false).apply();
        AlarmManager am = (AlarmManager) ctx.getSystemService(Context.ALARM_SERVICE);
        if (am != null) {
            am.cancel(alarmIntent(ctx));
        }
    }

    /** Arms a one-shot alarm for the next hour:minute, if a reminder is enabled. */
    static void arm(Context ctx) {
        SharedPreferences p = prefs(ctx);
        if (!p.getBoolean("enabled", false)) {
            return;
        }
        AlarmManager am = (AlarmManager) ctx.getSystemService(Context.ALARM_SERVICE);
        if (am == null) {
            return;
        }
        long at = nextTrigger(p.getInt("hour", 9), p.getInt("minute", 0));
        PendingIntent pi = alarmIntent(ctx);
        if (Build.VERSION.SDK_INT >= 31 && am.canScheduleExactAlarms()) {
            am.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pi);
        } else {
            // Inexact: the system may deliver it somewhat late to save battery.
            am.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pi);
        }
        Log.i(TreePhonePlugin.TAG, "daily reminder armed for " + at);
    }

    static long nextTrigger(int hour, int minute) {
        Calendar c = Calendar.getInstance();
        c.set(Calendar.HOUR_OF_DAY, hour);
        c.set(Calendar.MINUTE, minute);
        c.set(Calendar.SECOND, 0);
        c.set(Calendar.MILLISECOND, 0);
        if (c.getTimeInMillis() <= System.currentTimeMillis() + 1000L) {
            c.add(Calendar.DAY_OF_YEAR, 1);
        }
        return c.getTimeInMillis();
    }

    static boolean canNotify(Context ctx) {
        if (Build.VERSION.SDK_INT >= 33
                && ctx.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            return false;
        }
        NotificationManager nm = (NotificationManager) ctx.getSystemService(Context.NOTIFICATION_SERVICE);
        return nm != null && nm.areNotificationsEnabled();
    }

    /** Posts the stored reminder; tapping it opens the game. */
    static void post(Context ctx) {
        SharedPreferences p = prefs(ctx);
        if (!p.getBoolean("enabled", false) || !canNotify(ctx)) {
            return;
        }
        NotificationManager nm = (NotificationManager) ctx.getSystemService(Context.NOTIFICATION_SERVICE);
        Notification.Builder b;
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationChannel channel = new NotificationChannel(
                    CHANNEL_ID, "Daily reminder", NotificationManager.IMPORTANCE_DEFAULT);
            channel.setDescription("A daily nudge to look after your tree");
            nm.createNotificationChannel(channel);
            b = new Notification.Builder(ctx, CHANNEL_ID);
        } else {
            b = new Notification.Builder(ctx);
        }
        b.setSmallIcon(R.drawable.tree_phone_notification)
                .setContentTitle(p.getString("title", ""))
                .setContentText(p.getString("text", ""))
                .setAutoCancel(true);
        Intent launch = ctx.getPackageManager().getLaunchIntentForPackage(ctx.getPackageName());
        if (launch != null) {
            launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_RESET_TASK_IF_NEEDED);
            b.setContentIntent(PendingIntent.getActivity(ctx, TAP_REQUEST, launch,
                    PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE));
        }
        nm.notify(NOTIFICATION_ID, b.build());
    }

    private static PendingIntent alarmIntent(Context ctx) {
        Intent i = new Intent(ctx, ReminderReceiver.class);
        return PendingIntent.getBroadcast(ctx, ALARM_REQUEST, i,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
    }

    private static SharedPreferences prefs(Context ctx) {
        return ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }
}
