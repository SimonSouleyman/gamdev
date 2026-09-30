package de.simonsouleyman.treelive;

/**
 * The maths of the live picture, a line-by-line copy of the game's res://shared/live_picture.gd
 * (LivePicture) and the moon of res://shared/almanac.gd, which the game's tests pin. Change one,
 * change the other (docs/notes/live-icon-0.8.md lists the pairs). No Android classes here.
 */
final class LiveScene {
    private LiveScene() {
    }

    static final int VERSION = 1;
    static final String[] LAYERS = {"dawn", "day", "dusk", "night"};
    static final int FPS = 12;
    static final double KEEP_CLEAR = 0.28;
    static final double WIND = 0.012;
    static final double LATITUDE = 51.0;
    static final double LONGITUDE = 10.0;
    static final double SYNODIC_MONTH = 29.530588853;
    static final long KNOWN_NEW_MOON = 947182440L;

    static double smoothstep(double e0, double e1, double x) {
        double t = Math.max(0.0, Math.min(1.0, (x - e0) / (e1 - e0)));
        return t * t * (3.0 - 2.0 * t);
    }

    static double fposmod(double x, double y) {
        double r = x % y;
        return r < 0 ? r + y : r;
    }

    static double lerp(double a, double b, double t) {
        return a + (b - a) * t;
    }

    /** {rise, set, noon} as local clock hours (LivePicture.sun_times). */
    static double[] sunTimes(int dayOfYear, double utcOffsetHours) {
        double b = 2.0 * Math.PI * (dayOfYear - 81) / 365.0;
        double decl = Math.toRadians(23.44) * Math.sin(b);
        double lat = Math.toRadians(LATITUDE);
        double cosH = (Math.sin(Math.toRadians(-0.833)) - Math.sin(lat) * Math.sin(decl)) / (Math.cos(lat) * Math.cos(decl));
        double half = Math.toDegrees(Math.acos(Math.max(-1.0, Math.min(1.0, cosH)))) / 15.0;
        double eot = 9.87 * Math.sin(2.0 * b) - 7.53 * Math.cos(b) - 1.5 * Math.sin(b);
        double noon = 12.0 + utcOffsetHours - LONGITUDE / 15.0 - eot / 60.0;
        return new double[]{noon - half, noon + half, noon};
    }

    /** LivePicture.sun_height. */
    static double sunHeight(double hour, double[] times) {
        double rise = times[0];
        double set = times[1];
        if (hour >= rise && hour <= set) {
            return Math.sin(Math.PI * (hour - rise) / Math.max(set - rise, 0.1));
        }
        double sinceSet = fposmod(hour - set, 24.0);
        double untilRise = fposmod(rise - hour, 24.0);
        return -Math.min(sinceSet, untilRise);
    }

    /** LivePicture.light_mix: weights of dawn, day, dusk, night (the order of LAYERS). */
    static double[] lightMix(double h, boolean morning) {
        double night = smoothstep(0.0, 1.0, -h);
        double golden = h >= 0.0 ? 1.0 - smoothstep(0.0, 0.45, h) : 1.0;
        double gold = (1.0 - night) * golden;
        return new double[]{morning ? gold : 0.0, (1.0 - night) * (1.0 - golden), morning ? 0.0 : gold, night};
    }

    /** LivePicture.golden_of. */
    static double goldenOf(double[] mix) {
        double lit = 1.0 - mix[3];
        return lit > 0.001 ? (mix[0] + mix[2]) / lit : 1.0;
    }

    /** LivePicture.sky_colors: {top, horizon} as {r, g, b} each. */
    static double[][] skyColors(double golden, double night) {
        double[] top = mix3(new double[]{0.3, 0.5, 0.8}, new double[]{0.34, 0.42, 0.66}, golden);
        double[] horizon = mix3(new double[]{0.7, 0.8, 0.9}, new double[]{0.98, 0.7, 0.45}, golden);
        top = mix3(top, new double[]{0.05, 0.08, 0.18}, night);
        horizon = mix3(horizon, new double[]{0.14, 0.18, 0.3}, night);
        return new double[][]{top, horizon};
    }

    /** LivePicture.cloud_color: {r, g, b, a}. */
    static double[] cloudColor(double golden, double night) {
        double[] c = mix4(new double[]{1.0, 1.0, 1.0, 0.55}, new double[]{1.0, 0.86, 0.74, 0.6}, golden);
        return mix4(c, new double[]{0.3, 0.34, 0.46, 0.25}, night);
    }

    static double[] mix3(double[] a, double[] b, double t) {
        return new double[]{lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)};
    }

    static double[] mix4(double[] a, double[] b, double t) {
        return new double[]{lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t), lerp(a[3], b[3], t)};
    }

    /** Almanac.moon_phase: 0 new, 0.5 full. */
    static double moonPhase(long unix) {
        double days = (unix - KNOWN_NEW_MOON) / 86400.0;
        return fposmod(days / SYNODIC_MONTH, 1.0);
    }

    /** Almanac.moon_lit_fraction. */
    static double moonLitFraction(double phase) {
        return 0.5 * (1.0 - Math.cos(2.0 * Math.PI * phase));
    }

    /** One moment: what LivePicture.moment returns, in fields. */
    static final class Moment {
        double[] mix;
        double golden;
        double night;
        double[][] sky;
        double moon;
        double stars;
    }

    static Moment moment(int dayOfYear, double hour, double utcOffsetHours, long unix) {
        double[] times = sunTimes(dayOfYear, utcOffsetHours);
        double h = sunHeight(hour, times);
        Moment m = new Moment();
        m.mix = lightMix(h, hour < times[2]);
        m.golden = goldenOf(m.mix);
        m.night = m.mix[3];
        m.sky = skyColors(m.golden, m.night);
        m.moon = moonPhase(unix);
        m.stars = smoothstep(0.4, 1.0, m.night);
        return m;
    }

    /** LivePicture.wind_offset: {dx, dy} in layer units. */
    static void windOffset(double u, double v, double t, double groundY, double crownTop, float[] out) {
        double tall = Math.max(groundY - crownTop, 0.02);
        double k = Math.max(0.0, Math.min(1.0, (groundY - v) / tall));
        double bend = Math.pow(k, 1.6);
        double gust = 0.6 * Math.sin(t * 0.9 + u * 1.3) + 0.4 * Math.sin(t * 0.37 + 1.7);
        double flutter = 0.25 * k * Math.sin(t * 2.3 + u * 7.0 + v * 5.0);
        double dx = WIND * tall * bend * (gust + flutter);
        double dy = WIND * tall * 0.2 * bend * Math.sin(t * 1.1 + u * 3.0);
        if (v > groundY - 0.01) {
            double g = 1.0 - smoothstep(0.0, 0.2, v - groundY);
            dx += 0.002 * g * Math.sin(t * 1.7 + u * 11.0 + v * 23.0);
        }
        dx *= Math.max(0.0, Math.min(1.0, Math.min(u, 1.0 - u) / 0.04));
        out[0] = (float) dx;
        out[1] = (float) dy;
    }

    static final double[] CLOUD_SPEED = {0.0008, 0.0006, 0.0005};
    static final double[] CLOUD_START = {0.2, 0.75, 1.3};
    static final double[] CLOUD_Y = {0.08, 0.17, 0.03};
    static final double[] CLOUD_WIDTHS = {1.0, 0.8, 0.62};
    static final double CLOUD_ASPECT = 0.25;

    /** LivePicture.cloud_position: the cloud's middle {x, y} as shares of the screen. */
    static double[] cloudPosition(int i, double t) {
        int k = i % 3;
        return new double[]{fposmod(CLOUD_START[k] + t * CLOUD_SPEED[k], 2.2) - 0.6, CLOUD_Y[k]};
    }

    static final int STARS = 46;

    private static double fract(double x) {
        return x - Math.floor(x);
    }

    /** LivePicture.star: {x, y, size}. */
    static double[] star(int i) {
        double a = fract(Math.sin(i * 12.9898 + 1.0) * 43758.5453);
        double b = fract(Math.sin(i * 78.233 + 2.0) * 24634.6345);
        double c = fract(Math.sin(i * 39.425 + 3.0) * 12345.6789);
        return new double[]{a, b * 0.55, 1.5 + c * 2.5};
    }

    static final double MOON_X = 0.78;
    static final double MOON_Y = 0.2;
    static final double MOON_RADIUS = 0.04;

    /** LivePicture.moon_outline: x0, y0, x1, y1, ... around a unit disc. */
    static float[] moonOutline(double phase, int steps) {
        float[] pts = new float[(steps + 1) * 4];
        double side = phase < 0.5 ? 1.0 : -1.0;
        double k = Math.cos(2.0 * Math.PI * phase);
        int n = 0;
        for (int i = 0; i <= steps; i++) {
            double a = -Math.PI * 0.5 + Math.PI * i / steps;
            pts[n++] = (float) (side * Math.cos(a));
            pts[n++] = (float) Math.sin(a);
        }
        for (int i = steps; i >= 0; i--) {
            double a = -Math.PI * 0.5 + Math.PI * i / steps;
            pts[n++] = (float) (side * k * Math.cos(a));
            pts[n++] = (float) Math.sin(a);
        }
        return pts;
    }

    /** LivePicture.layout: {x, y, w, h} in screen pixels. */
    static float[] layout(float sw, float sh, float lw, float lh, double crownTop, double groundY) {
        double s = sw / lw;
        double x = 0.0;
        double y = sh - lh * s;
        double w = sw;
        double h = lh * s;
        double top = y + crownTop * h;
        double limit = KEEP_CLEAR * sh;
        if (top < limit && groundY > crownTop) {
            double foot = y + groundY * h;
            double s2 = (foot - limit) / ((groundY - crownTop) * lh);
            w = lw * s2;
            h = lh * s2;
            x = sw * 0.5 - w * 0.5;
            y = foot - groundY * h;
        }
        return new float[]{(float) x, (float) y, (float) w, (float) h};
    }
}
