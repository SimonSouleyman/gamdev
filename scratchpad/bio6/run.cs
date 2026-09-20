// Runs BiomeTests methods by reflection (run_tests wedges the Editor).
var sb = new System.Text.StringBuilder();
System.Type t = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { t = a.GetType("Drift.Tests.BiomeTests"); if (t != null) break; }
if (t == null) return "no BiomeTests type";

string filter = "@@FILTER@@";
var setUp = t.GetMethod("SetUp");
var tearDown = t.GetMethod("TearDown");
int pass = 0, fail = 0;
foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
{
    if (System.Attribute.GetCustomAttribute(m, typeof(NUnit.Framework.TestAttribute)) == null) continue;
    if (filter.Length > 0 && m.Name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
    var inst = System.Activator.CreateInstance(t);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        setUp.Invoke(inst, null);
        m.Invoke(inst, null);
        pass++;
        sb.Append("PASS ").Append(m.Name).Append(" ").Append(sw.ElapsedMilliseconds).Append("ms\n");
    }
    catch (System.Exception e)
    {
        fail++;
        var inner = e is System.Reflection.TargetInvocationException ? e.InnerException : e;
        sb.Append("FAIL ").Append(m.Name).Append(": ").Append(inner.Message.Replace("\n", " | ")).Append("\n");
    }
    finally
    {
        try { tearDown.Invoke(inst, null); } catch { }
    }
}
return "pass=" + pass + " fail=" + fail + "\n" + sb;
