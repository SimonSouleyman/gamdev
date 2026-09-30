package de.simonsouleyman.treephone;

import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Context;
import android.content.pm.PackageManager;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Rect;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaCodecList;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.os.SystemClock;
import android.provider.MediaStore;
import android.util.Log;
import android.view.Surface;

import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

/**
 * Turns the game's Motion-JPEG AVI (res://ui/mjpeg_avi.gd) into an H.264 MP4 and puts it into
 * the gallery under Movies/&lt;album&gt;. Plain Android APIs (MediaCodec, MediaMuxer, MediaStore).
 * Blocking: call it off the UI thread.
 */
final class VideoSaver {
    private static final String TAG = TreePhonePlugin.TAG;
    private static final String MIME = MediaFormat.MIMETYPE_VIDEO_AVC;
    private static final long TIMEOUT_US = 10_000;
    /**
     * With canvas input the encoder stamps each frame with the time it was posted, and its rate
     * control budgets bits by those stamps. Frames are posted this far apart (a steady 25 fps)
     * and the bitrate is scaled to match, so quality does not depend on how fast the phone draws.
     */
    private static final long POST_INTERVAL_MS = 40;
    /** Bits per pixel per frame for the finished video (stills with little motion). */
    private static final float BITS_PER_PIXEL = 0.5f;

    private VideoSaver() {
    }

    /** A parsed AVI: the frames' byte ranges in the file and the frame rate. */
    static final class Avi {
        final byte[] data;
        final List<int[]> frames = new ArrayList<>(); // {offset, length}
        double fps = 6.0;
        int width;
        int height;

        Avi(byte[] data) {
            this.data = data;
        }
    }

    /** Converts and inserts; true when the MP4 is in the gallery. */
    static boolean save(Context ctx, String aviPath, String album) {
        File avi = new File(aviPath);
        String base = avi.getName();
        int dot = base.lastIndexOf('.');
        String name = (dot > 0 ? base.substring(0, dot) : base) + ".mp4";
        File mp4 = new File(ctx.getCacheDir(), name);
        try {
            Avi parsed = parse(readAll(avi));
            if (parsed.frames.isEmpty()) {
                Log.w(TAG, "saveVideoToGallery: no frames in " + aviPath);
                return false;
            }
            encode(parsed, mp4);
            return insert(ctx, mp4, name, album);
        } catch (Exception e) {
            Log.w(TAG, "saveVideoToGallery failed for " + aviPath, e);
            return false;
        } finally {
            //noinspection ResultOfMethodCallIgnored
            mp4.delete();
        }
    }

    /** Converts the AVI into an MP4 at mp4 without touching the gallery (sharing, 0.8). */
    static boolean toMp4(String aviPath, File mp4) {
        try {
            Avi parsed = parse(readAll(new File(aviPath)));
            if (parsed.frames.isEmpty()) {
                return false;
            }
            encode(parsed, mp4);
            return mp4.isFile();
        } catch (Exception e) {
            Log.w(TAG, "toMp4 failed for " + aviPath, e);
            //noinspection ResultOfMethodCallIgnored
            mp4.delete();
            return false;
        }
    }

    // --- reading the AVI -------------------------------------------------------------------

    static byte[] readAll(File f) throws IOException {
        long len = f.length();
        if (len <= 0 || len > Integer.MAX_VALUE) {
            throw new IOException("bad file size " + len + " for " + f);
        }
        byte[] out = new byte[(int) len];
        try (InputStream in = new FileInputStream(f)) {
            int pos = 0;
            while (pos < out.length) {
                int n = in.read(out, pos, out.length - pos);
                if (n < 0) {
                    throw new IOException("short read " + f);
                }
                pos += n;
            }
        }
        return out;
    }

    static Avi parse(byte[] d) throws IOException {
        if (d.length < 12 || !fourcc(d, 0).equals("RIFF") || !fourcc(d, 8).equals("AVI ")) {
            throw new IOException("not an AVI");
        }
        Avi avi = new Avi(d);
        walk(avi, 12, (int) Math.min((long) d.length, 8L + u32(d, 4)), false);
        return avi;
    }

    /** Walks the chunks in [start, end); frame chunks count only inside the movi list. */
    private static void walk(Avi avi, int start, int end, boolean inMovi) {
        byte[] d = avi.data;
        int pos = start;
        while (pos + 8 <= end) {
            String id = fourcc(d, pos);
            long size = u32(d, pos + 4);
            int body = pos + 8;
            int bodyEnd = (int) Math.min((long) end, body + size);
            if (id.equals("LIST") && body + 4 <= bodyEnd) {
                String kind = fourcc(d, body);
                // hdrl, strl, movi and rec lists hold what we need; skip INFO and others.
                if (kind.equals("hdrl") || kind.equals("strl") || kind.equals("movi") || kind.equals("rec ")) {
                    walk(avi, body + 4, bodyEnd, inMovi || kind.equals("movi"));
                }
            } else if (id.equals("avih") && size >= 40) {
                avi.width = (int) u32(d, body + 32);
                avi.height = (int) u32(d, body + 36);
            } else if (id.equals("strh") && size >= 28 && fourcc(d, body).equals("vids")) {
                long scale = u32(d, body + 20);
                long rate = u32(d, body + 24);
                if (scale > 0 && rate > 0) {
                    avi.fps = (double) rate / scale;
                }
            } else if (inMovi && size > 0 && (id.endsWith("dc") || id.endsWith("db")) && body + size <= end) {
                avi.frames.add(new int[] { body, (int) size });
            }
            pos = body + (int) ((size + 1) & ~1L); // chunks are padded to an even size
            if (pos <= start) {
                break;
            }
        }
    }

    private static String fourcc(byte[] d, int at) {
        return new String(d, at, 4, StandardCharsets.US_ASCII);
    }

    private static long u32(byte[] d, int at) {
        return (d[at] & 0xFFL) | (d[at + 1] & 0xFFL) << 8 | (d[at + 2] & 0xFFL) << 16 | (d[at + 3] & 0xFFL) << 24;
    }

    // --- encoding ----------------------------------------------------------------------------

    private static void encode(Avi avi, File out) throws IOException {
        Bitmap first = decode(avi, 0);
        if (first == null) {
            throw new IOException("first frame is not a JPEG");
        }
        int w = first.getWidth() & ~1;
        int h = first.getHeight() & ~1;
        first.recycle();
        double fps = avi.fps > 0 ? avi.fps : 6.0;

        MediaCodec codec = null;
        Surface surface = null;
        MediaMuxer muxer = null;
        boolean muxerStarted = false;
        try {
            // Try the exact (even) size first; some older encoders want multiples of 16.
            int[][] sizes = { { w, h }, { (w + 15) & ~15, (h + 15) & ~15 } };
            IOException lastError = null;
            for (int[] s : sizes) {
                try {
                    codec = MediaCodec.createEncoderByType(MIME);
                    codec.configure(format(s[0], s[1], fps), null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
                    w = s[0];
                    h = s[1];
                    lastError = null;
                    break;
                } catch (Exception e) {
                    lastError = new IOException("encoder rejects " + s[0] + "x" + s[1], e);
                    if (codec != null) {
                        codec.release();
                        codec = null;
                    }
                }
            }
            if (codec == null) {
                throw lastError != null ? lastError : new IOException("no H.264 encoder");
            }
            surface = codec.createInputSurface();
            codec.start();
            muxer = new MediaMuxer(out.getAbsolutePath(), MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);

            int n = avi.frames.size();
            long[] postedFrom = new long[n]; // the canvas stamps the frame between these (monotonic us)
            long[] postedTo = new long[n];
            long frameUs = Math.round(1_000_000.0 / fps);
            Paint paint = new Paint(Paint.FILTER_BITMAP_FLAG);
            Rect dst = new Rect(0, 0, w, h);
            MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
            int[] track = { -1 };
            boolean[] started = { false };
            long nextPost = SystemClock.uptimeMillis();
            Bitmap last = null;

            for (int i = 0; i < n; i++) {
                Bitmap bmp = decode(avi, i);
                if (bmp == null) {
                    Log.w(TAG, "saveVideoToGallery: frame " + i + " is not a JPEG, repeating the one before");
                } else {
                    if (last != null) {
                        last.recycle();
                    }
                    last = bmp;
                }
                long wait = nextPost - SystemClock.uptimeMillis();
                if (wait > 0) {
                    SystemClock.sleep(wait);
                }
                nextPost = SystemClock.uptimeMillis() + POST_INTERVAL_MS;
                Canvas canvas = surface.lockHardwareCanvas();
                try {
                    canvas.drawColor(Color.BLACK);
                    if (last != null) {
                        canvas.drawBitmap(last, null, dst, paint);
                    }
                } finally {
                    postedFrom[i] = System.nanoTime() / 1000;
                    surface.unlockCanvasAndPost(canvas);
                    postedTo[i] = System.nanoTime() / 1000;
                }
                drain(codec, muxer, info, track, started, postedFrom, postedTo, frameUs, false);
            }
            if (last != null) {
                last.recycle();
            }
            codec.signalEndOfInputStream();
            drain(codec, muxer, info, track, started, postedFrom, postedTo, frameUs, true);
            muxerStarted = started[0];
            if (!muxerStarted) {
                throw new IOException("encoder produced no output");
            }
        } finally {
            if (codec != null) {
                try {
                    codec.stop();
                } catch (Exception ignored) {
                    // not started
                }
                codec.release();
            }
            if (surface != null) {
                surface.release();
            }
            if (muxer != null) {
                try {
                    if (muxerStarted) {
                        muxer.stop();
                    }
                } finally {
                    muxer.release();
                }
            }
        }
    }

    private static MediaFormat format(int w, int h, double fps) {
        MediaFormat f = MediaFormat.createVideoFormat(MIME, w, h);
        f.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface);
        // The finished video's bitrate, scaled up by how much faster than fps the frames are posted.
        double postFps = 1000.0 / POST_INTERVAL_MS;
        int target = (int) (w * h * fps * BITS_PER_PIXEL);
        int bitrate = (int) Math.max(1_000_000, Math.min(20_000_000, target * Math.max(1.0, postFps / fps)));
        f.setInteger(MediaFormat.KEY_BIT_RATE, clampBitrate(bitrate));
        f.setInteger(MediaFormat.KEY_FRAME_RATE, (int) Math.round(postFps));
        f.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1);
        return f;
    }

    /** Keeps the bitrate inside what the default H.264 encoder says it takes. */
    private static int clampBitrate(int bitrate) {
        try {
            for (MediaCodecInfo ci : new MediaCodecList(MediaCodecList.REGULAR_CODECS).getCodecInfos()) {
                if (!ci.isEncoder()) {
                    continue;
                }
                for (String t : ci.getSupportedTypes()) {
                    if (t.equalsIgnoreCase(MIME)) {
                        MediaCodecInfo.VideoCapabilities vc = ci.getCapabilitiesForType(MIME).getVideoCapabilities();
                        return vc == null ? bitrate : vc.getBitrateRange().clamp(bitrate);
                    }
                }
            }
        } catch (Exception e) {
            Log.w(TAG, "saveVideoToGallery: cannot read encoder limits", e);
        }
        return bitrate;
    }

    private static Bitmap decode(Avi avi, int i) {
        int[] fr = avi.frames.get(i);
        return BitmapFactory.decodeByteArray(avi.data, fr[0], fr[1]);
    }

    /**
     * Moves the encoder's output into the muxer. Canvas input stamps frames with the time they
     * were posted; each output is matched to the frame posted at that time, and gets that frame's
     * place in the film (frame / fps) as its presentation time.
     */
    private static void drain(MediaCodec codec, MediaMuxer muxer, MediaCodec.BufferInfo info, int[] track,
            boolean[] started, long[] postedFrom, long[] postedTo, long frameUs, boolean toEnd) throws IOException {
        long deadline = SystemClock.uptimeMillis() + 10_000;
        while (true) {
            int idx = codec.dequeueOutputBuffer(info, TIMEOUT_US);
            if (idx == MediaCodec.INFO_TRY_AGAIN_LATER) {
                if (!toEnd) {
                    return;
                }
                if (SystemClock.uptimeMillis() > deadline) {
                    throw new IOException("encoder did not finish");
                }
            } else if (idx == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                if (started[0]) {
                    throw new IOException("encoder format changed twice");
                }
                track[0] = muxer.addTrack(codec.getOutputFormat());
                muxer.start();
                started[0] = true;
            } else if (idx >= 0) {
                ByteBuffer buf = codec.getOutputBuffer(idx);
                boolean config = (info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0;
                if (buf != null && info.size > 0 && !config) {
                    if (!started[0]) {
                        throw new IOException("encoder output before its format");
                    }
                    info.presentationTimeUs = frameOf(info.presentationTimeUs, postedFrom, postedTo) * frameUs;
                    buf.position(info.offset);
                    buf.limit(info.offset + info.size);
                    muxer.writeSampleData(track[0], buf, info);
                }
                codec.releaseOutputBuffer(idx, false);
                if ((info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) {
                    return;
                }
                deadline = SystemClock.uptimeMillis() + 10_000;
            }
        }
    }

    /** The frame whose posting time is nearest to the encoder's timestamp. */
    private static long frameOf(long ptsUs, long[] from, long[] to) {
        int best = 0;
        long bestDist = Long.MAX_VALUE;
        for (int i = 0; i < from.length; i++) {
            if (from[i] == 0 && to[i] == 0) {
                break; // not posted yet
            }
            long dist = ptsUs < from[i] ? from[i] - ptsUs : (ptsUs > to[i] ? ptsUs - to[i] : 0);
            if (dist < bestDist) {
                bestDist = dist;
                best = i;
            }
        }
        return best;
    }

    // --- the gallery ---------------------------------------------------------------------------

    private static boolean insert(Context ctx, File mp4, String name, String album) throws IOException {
        ContentResolver cr = ctx.getContentResolver();
        long now = System.currentTimeMillis();
        ContentValues v = new ContentValues();
        v.put(MediaStore.Video.Media.DISPLAY_NAME, name);
        v.put(MediaStore.Video.Media.MIME_TYPE, "video/mp4");
        v.put(MediaStore.Video.Media.DATE_ADDED, now / 1000);
        v.put(MediaStore.Video.Media.DATE_TAKEN, now);

        if (Build.VERSION.SDK_INT >= 29) {
            v.put(MediaStore.Video.Media.RELATIVE_PATH, Environment.DIRECTORY_MOVIES + "/" + album);
            v.put(MediaStore.Video.Media.IS_PENDING, 1);
            Uri uri = cr.insert(MediaStore.Video.Media.EXTERNAL_CONTENT_URI, v);
            if (uri == null) {
                throw new IOException("MediaStore refused the video");
            }
            try {
                try (OutputStream os = cr.openOutputStream(uri, "w"); InputStream in = new FileInputStream(mp4)) {
                    if (os == null) {
                        throw new IOException("cannot open " + uri);
                    }
                    copy(in, os);
                }
                ContentValues done = new ContentValues();
                done.put(MediaStore.Video.Media.IS_PENDING, 0);
                cr.update(uri, done, null, null);
                return true;
            } catch (IOException | RuntimeException e) {
                cr.delete(uri, null, null);
                throw e;
            }
        }

        // Android 9 and older: write the file into Movies/<album> and register it (needs
        // WRITE_EXTERNAL_STORAGE, granted by the owner).
        if (ctx.checkSelfPermission(android.Manifest.permission.WRITE_EXTERNAL_STORAGE) != PackageManager.PERMISSION_GRANTED) {
            Log.w(TAG, "saveVideoToGallery: WRITE_EXTERNAL_STORAGE not granted");
            return false;
        }
        @SuppressWarnings("deprecation")
        File dir = new File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_MOVIES), album);
        if (!dir.isDirectory() && !dir.mkdirs()) {
            throw new IOException("cannot create " + dir);
        }
        File dest = new File(dir, name);
        try (InputStream in = new FileInputStream(mp4); OutputStream os = new java.io.FileOutputStream(dest)) {
            copy(in, os);
        }
        //noinspection deprecation
        v.put(MediaStore.Video.Media.DATA, dest.getAbsolutePath());
        v.put(MediaStore.Video.Media.SIZE, dest.length());
        return cr.insert(MediaStore.Video.Media.EXTERNAL_CONTENT_URI, v) != null;
    }

    private static void copy(InputStream in, OutputStream os) throws IOException {
        byte[] buf = new byte[64 * 1024];
        int n;
        while ((n = in.read(buf)) > 0) {
            os.write(buf, 0, n);
        }
    }
}
