package de.simonsouleyman.treelive;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileInputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;

/**
 * The picture of the tree: the four layers (dawn, day, dusk, night) and their meta, as the game
 * writes them (res://tree/live_export.gd) into user://live_picture, which is the app's files dir.
 * Read-only. When there is no picture yet, or it cannot be read, the young sapling bundled in the
 * library's assets (live_fallback) is shown; a newer unreadable picture never replaces a good one.
 */
final class LiveData {
    static final String TAG = "TreeLive";
    static final String DIR = "live_picture";
    static final String META = "live.json";
    static final String FALLBACK = "live_fallback";

    final Bitmap[] layers = new Bitmap[LiveScene.LAYERS.length];
    /** The soft cloud images the game wrote (none: a clear sky). */
    Bitmap[] clouds = new Bitmap[0];
    int width;
    int height;
    double groundY;
    double crownTop;
    double horizonY;
    int groundColor;
    /** When the meta file on disk was written (0: the bundled sapling). */
    long stamp;

    private LiveData() {
    }

    static File metaFile(Context ctx) {
        return new File(new File(ctx.getFilesDir(), DIR), META);
    }

    /** The game's picture, or null if there is none or it cannot be used. */
    static LiveData fromFiles(Context ctx) {
        File meta = metaFile(ctx);
        if (!meta.isFile()) {
            return null;
        }
        try (InputStream in = new FileInputStream(meta)) {
            LiveData d = parse(readAll(in));
            if (d == null) {
                return null;
            }
            File dir = meta.getParentFile();
            for (int i = 0; i < LiveScene.LAYERS.length; i++) {
                d.layers[i] = BitmapFactory.decodeFile(new File(dir, d.names[i]).getPath());
                if (d.layers[i] == null) {
                    d.recycle();
                    return null;
                }
            }
            d.clouds = new Bitmap[d.cloudNames.length];
            for (int i = 0; i < d.cloudNames.length; i++) {
                d.clouds[i] = BitmapFactory.decodeFile(new File(dir, d.cloudNames[i]).getPath());
            }
            d.stamp = meta.lastModified();
            return d;
        } catch (Exception e) {
            Log.w(TAG, "cannot read the live picture", e);
            return null;
        }
    }

    /** The young sapling bundled with the library. */
    static LiveData fromAssets(Context ctx) {
        try (InputStream in = ctx.getAssets().open(FALLBACK + "/" + META)) {
            LiveData d = parse(readAll(in));
            if (d == null) {
                return null;
            }
            for (int i = 0; i < LiveScene.LAYERS.length; i++) {
                try (InputStream img = ctx.getAssets().open(FALLBACK + "/" + d.names[i])) {
                    d.layers[i] = BitmapFactory.decodeStream(img);
                }
                if (d.layers[i] == null) {
                    d.recycle();
                    return null;
                }
            }
            d.clouds = new Bitmap[d.cloudNames.length];
            for (int i = 0; i < d.cloudNames.length; i++) {
                try (InputStream img = ctx.getAssets().open(FALLBACK + "/" + d.cloudNames[i])) {
                    d.clouds[i] = BitmapFactory.decodeStream(img);
                } catch (Exception e) {
                    d.clouds[i] = null;
                }
            }
            d.stamp = 0;
            return d;
        } catch (Exception e) {
            Log.w(TAG, "cannot read the bundled picture", e);
            return null;
        }
    }

    private final String[] names = new String[LiveScene.LAYERS.length];
    private String[] cloudNames = new String[0];

    /** LivePicture.parse_meta: null when the meta cannot be used. */
    static LiveData parse(String json) {
        try {
            JSONObject m = new JSONObject(json);
            int version = m.optInt("version", 0);
            if (version < 1 || version > LiveScene.VERSION) {
                return null;
            }
            JSONObject layers = m.getJSONObject("layers");
            LiveData d = new LiveData();
            for (int i = 0; i < LiveScene.LAYERS.length; i++) {
                String f = layers.optString(LiveScene.LAYERS[i], "");
                if (f.isEmpty() || f.contains("/") || f.contains("\\") || f.contains("..")) {
                    return null;
                }
                d.names[i] = f;
            }
            JSONArray size = m.getJSONArray("size");
            d.width = size.getInt(0);
            d.height = size.getInt(1);
            d.groundY = m.optDouble("ground_y", -1.0);
            d.crownTop = m.optDouble("crown_top", -1.0);
            d.horizonY = m.optDouble("horizon_y", -1.0);
            if (d.width < 16 || d.height < 16 || d.groundY <= 0.0 || d.groundY > 1.0 || d.crownTop < 0.0
                    || d.crownTop >= d.groundY || d.horizonY < 0.0 || d.horizonY > 1.0) {
                return null;
            }
            String c = m.optString("ground_color", "");
            d.groundColor = Color.parseColor(c.startsWith("#") ? c : "#" + c);
            JSONArray clouds = m.optJSONArray("clouds");
            java.util.ArrayList<String> cn = new java.util.ArrayList<>();
            for (int i = 0; clouds != null && i < clouds.length() && i < 3; i++) {
                String f = clouds.optString(i, "");
                if (!f.isEmpty() && !f.contains("/") && !f.contains("\\") && !f.contains("..")) {
                    cn.add(f);
                }
            }
            d.cloudNames = cn.toArray(new String[0]);
            return d;
        } catch (Exception e) {
            return null;
        }
    }

    void recycle() {
        for (int i = 0; i < layers.length; i++) {
            if (layers[i] != null) {
                layers[i].recycle();
                layers[i] = null;
            }
        }
        for (int i = 0; i < clouds.length; i++) {
            if (clouds[i] != null) {
                clouds[i].recycle();
                clouds[i] = null;
            }
        }
    }

    private static String readAll(InputStream in) throws java.io.IOException {
        java.io.ByteArrayOutputStream out = new java.io.ByteArrayOutputStream();
        byte[] buf = new byte[4096];
        int n;
        while ((n = in.read(buf)) > 0) {
            out.write(buf, 0, n);
        }
        return new String(out.toByteArray(), StandardCharsets.UTF_8);
    }
}
