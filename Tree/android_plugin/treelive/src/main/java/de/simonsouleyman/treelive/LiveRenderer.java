package de.simonsouleyman.treelive;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.PorterDuff;
import android.graphics.PorterDuffColorFilter;
import android.graphics.RectF;
import android.graphics.Shader;

import java.util.Calendar;
import java.util.TimeZone;

/**
 * Draws one frame of the live picture onto a Canvas: the sky in the real time's light (gradient,
 * stars, the moon in its real phase, three soft clouds the game drew, drifting), then the tree's layers mixed by that
 * light and bent by the wind (a Canvas bitmap mesh). Shared by the wallpaper and the screen saver.
 * The desktop copy is res://tools/live_preview.gd; the maths is LiveScene (= LivePicture).
 *
 * Cheap on purpose: no 3D, no shaders, a few hundred mesh vertices a frame; the light is worked
 * out once every few seconds, the picture's files are looked at once a minute (a file date).
 */
final class LiveRenderer {
    static final int MESH_W = 24;
    static final int MESH_H = 48;
    /** How often the light is worked out again (it changes over minutes). */
    static final long MOMENT_MS = 5000;
    /** How often the game's picture is looked for (a new one after the game saved). */
    static final long CHECK_MS = 60000;

    private final Context ctx;
    private LiveData data;
    private long lastCheck = -CHECK_MS;
    /** The file date of the game's picture last found unusable (0: none). */
    private long rejected;
    private long lastMoment = -MOMENT_MS;
    private LiveScene.Moment moment;
    private final long startMs = System.currentTimeMillis();

    private final Paint paint = new Paint(Paint.FILTER_BITMAP_FLAG | Paint.ANTI_ALIAS_FLAG);
    private final Paint sky = new Paint();
    private final Paint fill = new Paint();
    private final Paint dots = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final float[] verts = new float[(MESH_W + 1) * (MESH_H + 1) * 2];
    private final float[] off = new float[2];
    private final Path moonPath = new Path();
    private final RectF dst = new RectF();
    private PorterDuffColorFilter cloudFilter;
    private int cloudFilterColor;
    private int skyW = -1;
    private int skyH = -1;
    private float skyHorizon = -1;
    private LiveScene.Moment skyMoment;

    LiveRenderer(Context ctx) {
        this.ctx = ctx.getApplicationContext();
    }

    /** Looks for a new picture from the game (at most once a minute unless forced). */
    void checkForPicture(boolean force) {
        long now = System.currentTimeMillis();
        if (!force && now - lastCheck < CHECK_MS && data != null) {
            return;
        }
        lastCheck = now;
        long onDisk = LiveData.metaFile(ctx).lastModified();
        if (data != null && data.stamp != 0 && onDisk == data.stamp) {
            return;
        }
        if (data != null && onDisk == rejected) {
            // The same unusable picture as last time: not decoded again each minute.
            return;
        }
        LiveData fresh = onDisk > 0 ? LiveData.fromFiles(ctx) : null;
        rejected = fresh == null ? onDisk : 0;
        if (fresh == null && data == null) {
            // No picture from the game yet (or it cannot be read): the young sapling.
            fresh = LiveData.fromAssets(ctx);
        }
        if (fresh != null) {
            if (data != null) {
                data.recycle();
            }
            data = fresh;
        }
    }

    void release() {
        if (data != null) {
            data.recycle();
            data = null;
        }
    }

    /** Draws the frame for the wall clock time `nowMs` onto a w x h canvas. */
    void draw(Canvas c, int w, int h, long nowMs) {
        checkForPicture(false);
        double t = (nowMs - startMs) / 1000.0;
        if (moment == null || nowMs - lastMoment >= MOMENT_MS) {
            lastMoment = nowMs;
            moment = momentAt(nowMs);
        }
        LiveScene.Moment m = moment;
        if (data == null) {
            // Nothing readable at all (should not happen: the sapling is bundled): the sky alone.
            c.drawColor(rgb(m.sky[1], 1.0));
            return;
        }
        float[] r = LiveScene.layout(w, h, data.width, data.height, data.crownTop, data.groundY);
        float horizon = r[1] + (float) data.horizonY * r[3];
        drawSky(c, w, h, horizon, m);
        drawStarsAndMoon(c, w, h, m, t);
        drawClouds(c, w, h, m, t);
        if (r[0] > 0.5f) {
            // The ground beside a narrowed picture.
            fill.setColor(data.groundColor);
            c.drawRect(0, horizon, w, h, fill);
        }
        drawTree(c, r, m, t);
    }

    static LiveScene.Moment momentAt(long nowMs) {
        Calendar cal = Calendar.getInstance();
        cal.setTimeInMillis(nowMs);
        double hour = cal.get(Calendar.HOUR_OF_DAY) + cal.get(Calendar.MINUTE) / 60.0 + cal.get(Calendar.SECOND) / 3600.0;
        double offset = TimeZone.getDefault().getOffset(nowMs) / 3600000.0;
        return LiveScene.moment(cal.get(Calendar.DAY_OF_YEAR), hour, offset, nowMs / 1000L);
    }

    private void drawSky(Canvas c, int w, int h, float horizon, LiveScene.Moment m) {
        if (w != skyW || h != skyH || horizon != skyHorizon || m != skyMoment) {
            skyW = w;
            skyH = h;
            skyHorizon = horizon;
            skyMoment = m;
            sky.setShader(new LinearGradient(0, 0, 0, Math.max(horizon, 1f), rgb(m.sky[0], 1.0), rgb(m.sky[1], 1.0), Shader.TileMode.CLAMP));
        }
        c.drawRect(0, 0, w, h, sky);
    }

    private void drawStarsAndMoon(Canvas c, int w, int h, LiveScene.Moment m, double t) {
        if (m.stars > 0.0) {
            for (int i = 0; i < LiveScene.STARS; i++) {
                double[] s = LiveScene.star(i);
                double a = m.stars * (0.65 + 0.35 * Math.sin(t * 0.8 + i * 1.7));
                dots.setColor(Color.argb((int) (255 * a), 255, 255, 242));
                c.drawCircle((float) (s[0] * w), (float) (s[1] * h), (float) (s[2] * w / 1080.0), dots);
            }
        }
        double lit = LiveScene.moonLitFraction(m.moon);
        if (m.night > 0.3 && lit > 0.03) {
            double alpha = LiveScene.smoothstep(0.3, 0.8, m.night);
            float cx = (float) (LiveScene.MOON_X * w);
            float cy = (float) (LiveScene.MOON_Y * h);
            float rad = (float) (LiveScene.MOON_RADIUS * w);
            dots.setColor(Color.argb((int) (255 * 0.25 * alpha), 89, 97, 128));
            c.drawCircle(cx, cy, rad, dots);
            float[] pts = LiveScene.moonOutline(m.moon, 24);
            moonPath.rewind();
            moonPath.moveTo(cx + pts[0] * rad, cy + pts[1] * rad);
            for (int i = 2; i < pts.length; i += 2) {
                moonPath.lineTo(cx + pts[i] * rad, cy + pts[i + 1] * rad);
            }
            moonPath.close();
            dots.setColor(Color.argb((int) (255 * alpha), 242, 240, 219));
            c.drawPath(moonPath, dots);
        }
    }

    private void drawClouds(Canvas c, int w, int h, LiveScene.Moment m, double t) {
        double[] cc = LiveScene.cloudColor(m.golden, m.night);
        int col = rgb(new double[]{cc[0], cc[1], cc[2]}, cc[3]);
        if (cloudFilter == null || col != cloudFilterColor) {
            cloudFilter = new PorterDuffColorFilter(col, PorterDuff.Mode.SRC_IN);
            cloudFilterColor = col;
        }
        paint.setColorFilter(cloudFilter);
        paint.setAlpha(255);
        for (int i = 0; i < data.clouds.length && i < 3; i++) {
            if (data.clouds[i] == null) {
                continue;
            }
            double[] p = LiveScene.cloudPosition(i, t);
            float cw = (float) (LiveScene.CLOUD_WIDTHS[i] * w);
            float ch = (float) (cw * LiveScene.CLOUD_ASPECT);
            float x = (float) (p[0] * w - cw * 0.5);
            float y = (float) (p[1] * h - ch * 0.5);
            dst.set(x, y, x + cw, y + ch);
            c.drawBitmap(data.clouds[i], null, dst, paint);
        }
        paint.setColorFilter(null);
    }

    private void drawTree(Canvas c, float[] r, LiveScene.Moment m, double t) {
        // The strongest light's layer opaque, the next blended over it by its share (at most two
        // are lit at once: golden and day, or golden and night).
        int first = 0;
        for (int i = 1; i < 4; i++) {
            if (m.mix[i] > m.mix[first]) {
                first = i;
            }
        }
        int second = -1;
        for (int i = 0; i < 4; i++) {
            if (i != first && m.mix[i] >= 0.01 && (second < 0 || m.mix[i] > m.mix[second])) {
                second = i;
            }
        }
        buildMesh(r, t);
        drawLayer(c, data.layers[first], 1.0);
        if (second >= 0) {
            drawLayer(c, data.layers[second], m.mix[second] / (m.mix[second] + m.mix[first]));
        }
    }

    private void buildMesh(float[] r, double t) {
        int n = 0;
        for (int j = 0; j <= MESH_H; j++) {
            double v = (double) j / MESH_H;
            for (int i = 0; i <= MESH_W; i++) {
                double u = (double) i / MESH_W;
                LiveScene.windOffset(u, v, t, data.groundY, data.crownTop, off);
                verts[n++] = r[0] + (float) (u + off[0]) * r[2];
                verts[n++] = r[1] + (float) (v + off[1]) * r[3];
            }
        }
    }

    private void drawLayer(Canvas c, Bitmap b, double alpha) {
        paint.setAlpha((int) Math.round(255 * Math.max(0.0, Math.min(1.0, alpha))));
        c.drawBitmapMesh(b, MESH_W, MESH_H, verts, 0, null, 0, paint);
        paint.setAlpha(255);
    }

    static int rgb(double[] c, double a) {
        return Color.argb(clamp255(a), clamp255(c[0]), clamp255(c[1]), clamp255(c[2]));
    }

    private static int clamp255(double x) {
        return (int) Math.round(255 * Math.max(0.0, Math.min(1.0, x)));
    }
}
