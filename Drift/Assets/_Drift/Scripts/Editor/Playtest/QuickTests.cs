using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Drift.EditorTools.Playtest
{
    // Runs plain EditMode tests ([Test]/[TestCase], SetUp/TearDown) of the Drift.Tests* assemblies directly by
    // reflection, from eval: `unity command run_tests` hung the Editor's command pipeline twice (2026-09-22/23) and
    // only an Editor restart brought it back. [UnityTest] coroutines are not run here.
    public static class QuickTests
    {
        public static string Run(string classPattern, int maxFailures = 20)
        {
            var filter = new Regex(classPattern);
            int pass = 0, fail = 0, classes = 0;
            var sb = new StringBuilder();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.GetName().Name.StartsWith("Drift.Tests")) continue;
                foreach (var t in asm.GetTypes())
                {
                    if (t.IsAbstract || !filter.IsMatch(t.Name)) continue;
                    MethodInfo setup = null, teardown = null;
                    var tests = new List<(MethodInfo m, object[] args)>();
                    foreach (var m in t.GetMethods())
                        foreach (var a in m.GetCustomAttributes(true))
                        {
                            string n = a.GetType().Name;
                            if (n == "SetUpAttribute") setup = m;
                            else if (n == "TearDownAttribute") teardown = m;
                            else if (n == "TestAttribute") foreach (var args in ValueCombinations(m)) tests.Add((m, args));
                            else if (n == "TestCaseAttribute") tests.Add((m, (object[])a.GetType().GetProperty("Arguments").GetValue(a)));
                        }
                    if (tests.Count == 0) continue;
                    classes++;
                    foreach (var (m, args) in tests)
                    {
                        object inst = Activator.CreateInstance(t);
                        try
                        {
                            setup?.Invoke(inst, null);
                            m.Invoke(inst, args);
                            pass++;
                        }
                        catch (Exception e)
                        {
                            fail++;
                            var inner = e.InnerException ?? e;
                            if (fail <= maxFailures)
                                sb.Append("\nFAIL ").Append(t.Name).Append('.').Append(m.Name).Append(": ").Append(inner.Message.Split('\n')[0]);
                        }
                        finally
                        {
                            try { teardown?.Invoke(inst, null); } catch { }
                        }
                    }
                }
            }
            return $"{pass} passed, {fail} failed ({classes} classes)" + sb;
        }

        // [Values(...)] parameters: every combination (NUnit's default combinatorial strategy).
        static List<object[]> ValueCombinations(MethodInfo m)
        {
            var result = new List<object[]> { Array.Empty<object>() };
            foreach (var p in m.GetParameters())
            {
                object[] values = null;
                foreach (var a in p.GetCustomAttributes(true))
                    if (a.GetType().Name == "ValuesAttribute")
                        values = a.GetType().GetField("data", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(a) as object[];
                if (values == null || values.Length == 0) return new List<object[]>();
                var next = new List<object[]>();
                foreach (var prefix in result)
                    foreach (var v in values)
                    {
                        var args = new object[prefix.Length + 1];
                        prefix.CopyTo(args, 0);
                        args[prefix.Length] = p.ParameterType.IsEnum || v == null || p.ParameterType.IsInstanceOfType(v) ? v : Convert.ChangeType(v, p.ParameterType);
                        next.Add(args);
                    }
                result = next;
            }
            return result;
        }
    }
}
