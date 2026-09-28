package de.simonsouleyman.treephone;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/** The daily alarm: post the reminder, then arm tomorrow's. */
public class ReminderReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context context, Intent intent) {
        ReminderScheduler.post(context);
        ReminderScheduler.arm(context);
    }
}
