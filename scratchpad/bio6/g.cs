// Runs whole test classes by reflection (run_tests wedges the Editor).
var sb = new System.Text.StringBuilder();
string[] names = "BiomeTests,IslandLifeSystemTests,LifePhase1Tests,LifePhase2Tests,LifePhase3Tests,LifePhase4Tests,LifePhase5Tests,FireflyTests,CollectionTests,SettlementTests,WatchToolsTests,AnimalDetailTests,TutorialTests".Split(',');
int pass = 0, fail = 0;
foreach (var name in names)
{
    System.Type t = null;
    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { t = a.GetType("Drift.Tests." + name.Trim()); if (t != null) break; }
    if (t == null) { sb.Append("MISSING ").Append(name).Append('\n'); continue; }
    var setUp = t.GetMethod("SetUp");
    var tearDown = t.GetMethod("TearDown");
    foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
    {
        if (System.Attribute.GetCustomAttribute(m, typeof(NUnit.Framework.TestAttribute)) == null) continue;
        var inst = System.Activator.CreateInstance(t);
        try
        {
            if (setUp != null) setUp.Invoke(inst, null);
            m.Invoke(inst, null);
            pass++;
        }
        catch (System.Exception e)
        {
            fail++;
            var inner = e is System.Reflection.TargetInvocationException ? e.InnerException : e;
            sb.Append("FAIL ").Append(t.Name).Append('.').Append(m.Name).Append(": ").Append(inner.Message.Replace("\n", " | ")).Append('\n');
        }
        finally
        {
            try { if (tearDown != null) tearDown.Invoke(inst, null); } catch { }
        }
    }
}
return "pass=" + pass + " fail=" + fail + "\n" + sb;
