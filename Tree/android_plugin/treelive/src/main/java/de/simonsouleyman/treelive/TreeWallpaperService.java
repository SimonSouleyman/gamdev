package de.simonsouleyman.treelive;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.graphics.Canvas;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.PowerManager;
import android.service.wallpaper.WallpaperService;
import android.view.SurfaceHolder;

/**
 * The live wallpaper (home and lock screen): the tree from the game's last save, drawn by
 * LiveRenderer at LiveScene.FPS frames a second while it is seen. It stops drawing as soon as it
 * is hidden (screen off, an app in front), draws one still frame a minute in battery saver, does
 * not pan with the home screen's pages and ignores touches: it is a picture.
 */
public class TreeWallpaperService extends WallpaperService {
    @Override
    public Engine onCreateEngine() {
        return new TreeEngine();
    }

    final class TreeEngine extends Engine {
        private final Handler handler = new Handler(Looper.getMainLooper());
        private final Runnable frame = this::drawFrame;
        private LiveRenderer renderer;
        private boolean visible;
        private int width;
        private int height;
        private PowerManager power;
        private final BroadcastReceiver saverChanged = new BroadcastReceiver() {
            @Override
            public void onReceive(Context context, Intent intent) {
                drawFrame();
            }
        };

        @Override
        public void onCreate(SurfaceHolder holder) {
            super.onCreate(holder);
            setTouchEventsEnabled(false);
            // The view never pans with the home screen's pages (specs/0.8.md, broken list 14e).
            setOffsetNotificationsEnabled(false);
            renderer = new LiveRenderer(TreeWallpaperService.this);
            power = (PowerManager) getSystemService(Context.POWER_SERVICE);
            IntentFilter saver = new IntentFilter(PowerManager.ACTION_POWER_SAVE_MODE_CHANGED);
            if (Build.VERSION.SDK_INT >= 33) {
                registerReceiver(saverChanged, saver, Context.RECEIVER_NOT_EXPORTED);
            } else {
                registerReceiver(saverChanged, saver);
            }
        }

        @Override
        public void onDestroy() {
            handler.removeCallbacks(frame);
            try {
                unregisterReceiver(saverChanged);
            } catch (IllegalArgumentException ignored) {
                // not registered
            }
            renderer.release();
            super.onDestroy();
        }

        @Override
        public void onVisibilityChanged(boolean isVisible) {
            visible = isVisible;
            if (isVisible) {
                // Back in view: the game may have saved a new picture meanwhile.
                renderer.checkForPicture(true);
                drawFrame();
            } else {
                handler.removeCallbacks(frame);
            }
        }

        @Override
        public void onSurfaceChanged(SurfaceHolder holder, int format, int w, int h) {
            super.onSurfaceChanged(holder, format, w, h);
            width = w;
            height = h;
            drawFrame();
        }

        @Override
        public void onSurfaceDestroyed(SurfaceHolder holder) {
            visible = false;
            handler.removeCallbacks(frame);
            super.onSurfaceDestroyed(holder);
        }

        private void drawFrame() {
            handler.removeCallbacks(frame);
            if (!visible || width <= 0 || height <= 0) {
                return;
            }
            SurfaceHolder holder = getSurfaceHolder();
            Canvas c = null;
            try {
                c = Build.VERSION.SDK_INT >= 26 ? holder.lockHardwareCanvas() : holder.lockCanvas();
                if (c != null) {
                    renderer.draw(c, width, height, System.currentTimeMillis());
                }
            } catch (IllegalStateException | IllegalArgumentException e) {
                // The surface went away between the check and the lock: the next frame retries.
            } finally {
                if (c != null) {
                    try {
                        holder.unlockCanvasAndPost(c);
                    } catch (IllegalStateException | IllegalArgumentException ignored) {
                        // surface gone
                    }
                }
            }
            boolean saver = power != null && power.isPowerSaveMode();
            // Battery saver: a still frame, redrawn once a minute so the light still follows the day.
            handler.postDelayed(frame, saver ? 60000L : 1000L / LiveScene.FPS);
        }
    }
}
