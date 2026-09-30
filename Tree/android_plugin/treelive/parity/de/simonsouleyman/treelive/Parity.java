package de.simonsouleyman.treelive;
/** Prints LiveScene's results for the inputs of res://tools/live_parity.gd (see there). Not built into the AAR. */
public class Parity {
  public static void main(String[] a) {
    StringBuilder sb = new StringBuilder();
    int[] days = {1, 80, 172, 273, 355};
    double[] hours = {0.5, 5.2, 7.4, 9.0, 12.5, 17.8, 19.1, 20.3, 23.9};
    for (int d : days) for (double h : hours) {
      LiveScene.Moment m = LiveScene.moment(d, h, d > 88 && d < 300 ? 2 : 1, 1790000000L + d * 86400L);
      sb.append(String.format(java.util.Locale.ROOT, "%d %.1f %.6f %.6f %.6f %.6f %.6f %.6f %.6f %.6f%n", d, h, m.mix[0], m.mix[1], m.mix[2], m.mix[3], m.sky[0][0], m.sky[1][1], m.moon, m.stars));
    }
    float[] o = new float[2];
    for (int i = 0; i < 20; i++) { double u = i / 19.0, v = (i * 7 % 20) / 19.0; LiveScene.windOffset(u, v, i * 1.37, 0.9, 0.3, o); sb.append(String.format(java.util.Locale.ROOT, "w %.6f %.6f%n", o[0], o[1])); }
    for (int i = 0; i < 5; i++) { double[] s = LiveScene.star(i); sb.append(String.format(java.util.Locale.ROOT, "s %.6f %.6f %.6f%n", s[0], s[1], s[2])); }
    float[] r = LiveScene.layout(1080, 2400, 540, 960, 0.1, 0.9); sb.append(String.format(java.util.Locale.ROOT, "l %.3f %.3f %.3f %.3f%n", r[0], r[1], r[2], r[3]));
    System.out.print(sb);
  }
}
