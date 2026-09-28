using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Drift.Tests
{
    // Unity only builds shaders that a built material references. The first APK (2026-09-23) shipped without
    // Drift/VertexColor, Animal, Critter, Fish and RingRim because code creates their materials with Shader.Find:
    // Tilda, animals, birds and ships were invisible on the phone. Every shader code looks up by name needs a
    // reference material in Resources/ShaderRefs.
    public class ShaderBuildTests
    {
        [Test]
        public void EveryShaderFoundByNameHasAResourcesReference()
        {
            var names = new System.Collections.Generic.HashSet<string>();
            foreach (var file in Directory.GetFiles("Assets/_Drift/Scripts", "*.cs", SearchOption.AllDirectories))
            {
                string path = file.Replace(Path.DirectorySeparatorChar, '/');
                if (path.Contains("/Tests/") || path.Contains("/Editor/")) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(Drift/[A-Za-z]+)\"")) names.Add(m.Groups[1].Value);
            }
            Assert.IsNotEmpty(names);
            var referenced = new System.Collections.Generic.HashSet<string>();
            foreach (var mat in Resources.LoadAll<Material>("ShaderRefs"))
                if (mat.shader != null) referenced.Add(mat.shader.name);
            foreach (var n in names) Assert.IsTrue(referenced.Contains(n), $"{n} is looked up by name but has no material in Resources/ShaderRefs");
        }
    }
}
