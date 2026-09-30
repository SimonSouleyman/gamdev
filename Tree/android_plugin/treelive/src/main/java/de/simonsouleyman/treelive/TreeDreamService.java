package de.simonsouleyman.treelive;

import android.content.Context;
import android.graphics.Canvas;
import android.service.dreams.DreamService;
import android.view.View;

/**
 * The screen saver (Settings > Display > Screen saver, while charging or docked): the same
 * animation as the live wallpaper, full screen, not interactive (a touch ends it, as usual).
 */
public class TreeDreamService extends DreamService {
    private LiveView view;

    @Override
    public void onAttachedToWindow() {
        super.onAttachedToWindow();
        setInteractive(false);
        setFullscreen(true);
        view = new LiveView(this);
        setContentView(view);
    }

    @Override
    public void onDreamingStarted() {
        super.onDreamingStarted();
        if (view != null) {
            view.setRunning(true);
        }
    }

    @Override
    public void onDreamingStopped() {
        if (view != null) {
            view.setRunning(false);
        }
        super.onDreamingStopped();
    }

    @Override
    public void onDetachedFromWindow() {
        if (view != null) {
            view.setRunning(false);
            view.release();
        }
        super.onDetachedFromWindow();
    }

    /** A plain view that draws LiveRenderer's frame and asks for the next one at LiveScene.FPS. */
    static final class LiveView extends View {
        private final LiveRenderer renderer;
        private boolean running;

        LiveView(Context ctx) {
            super(ctx);
            renderer = new LiveRenderer(ctx);
        }

        void setRunning(boolean on) {
            running = on;
            if (on) {
                renderer.checkForPicture(true);
                invalidate();
            }
        }

        void release() {
            renderer.release();
        }

        @Override
        protected void onDraw(Canvas canvas) {
            renderer.draw(canvas, getWidth(), getHeight(), System.currentTimeMillis());
            if (running) {
                postInvalidateDelayed(1000L / LiveScene.FPS);
            }
        }
    }
}
