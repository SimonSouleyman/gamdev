using System.Collections.Generic;
using Drift.Core;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class IcosphereGeneratorTests
    {
        [TestCase(0, 12, 20)]
        [TestCase(1, 42, 80)]
        [TestCase(2, 162, 320)]
        [TestCase(3, 642, 1280)]
        [TestCase(4, 2562, 5120)]
        public void Generate_HasExpectedCounts(int subdivisions, int vertices, int faces)
        {
            var d = IcosphereGenerator.Generate(subdivisions);
            Assert.AreEqual(vertices, d.Vertices.Length);
            Assert.AreEqual(faces * 3, d.Triangles.Length);
            Assert.AreEqual(faces, d.TriangleChunkId.Length);
        }

        [Test]
        public void Generate_VerticesAreOnUnitSphere()
        {
            foreach (var v in IcosphereGenerator.Generate(3).Vertices)
                Assert.AreEqual(1f, v.magnitude, 1e-4f);
        }

        [Test]
        public void Generate_TriangleIndicesAreValidAndNonDegenerate()
        {
            var d = IcosphereGenerator.Generate(2);
            for (int i = 0; i < d.Triangles.Length; i += 3)
            {
                int a = d.Triangles[i], b = d.Triangles[i + 1], c = d.Triangles[i + 2];
                Assert.That(a, Is.InRange(0, d.Vertices.Length - 1));
                Assert.That(b, Is.InRange(0, d.Vertices.Length - 1));
                Assert.That(c, Is.InRange(0, d.Vertices.Length - 1));
                Assert.That(a != b && b != c && a != c);
            }
        }

        [Test]
        public void Generate_VerticesAreShared()
        {
            var d = IcosphereGenerator.Generate(2);
            var unique = new HashSet<Vector3>(d.Vertices);
            Assert.AreEqual(d.Vertices.Length, unique.Count);
        }

        [TestCase(0)]
        [TestCase(2)]
        public void Generate_ChunkIdsSplitFacesEvenlyAcrossTwentyChunks(int subdivisions)
        {
            var d = IcosphereGenerator.Generate(subdivisions);
            int perChunk = d.TriangleChunkId.Length / 20;
            var counts = new int[20];
            foreach (int id in d.TriangleChunkId)
            {
                Assert.That(id, Is.InRange(0, 19));
                counts[id]++;
            }
            foreach (int c in counts) Assert.AreEqual(perChunk, c);
        }

        [Test]
        public void Generate_IsDeterministic()
        {
            var a = IcosphereGenerator.Generate(2);
            var b = IcosphereGenerator.Generate(2);
            Assert.AreEqual(a.Vertices, b.Vertices);
            Assert.AreEqual(a.Triangles, b.Triangles);
            Assert.AreEqual(a.TriangleChunkId, b.TriangleChunkId);
        }
    }
}
