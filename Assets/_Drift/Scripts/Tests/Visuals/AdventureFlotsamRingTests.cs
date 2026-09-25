using System.Collections.Generic;
using System.Reflection;
using Drift.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The flotsam gets the outline ring of the cozy watch marker (owner, 2026-09-25: "Das Treibgut ist tagsüber schlecht
    // sichtbar. Mache hier einen Ring herum wie wenn man im Gemütlich-Modus die Tiere zum Beobachten markiert").
    public class AdventureFlotsamRingTests
    {
        readonly List<GameObject> _objects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        GameObject New(string name)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            _objects.Add(go);
            return go;
        }

        [Test]
        public void FlotsamRing_OnEveryPiece_GoneOnceCollected()
        {
            Assert.AreEqual(1f, ShipSystem.MarkerRingStrength(1f, -1f));
            Assert.AreEqual(0.5f, ShipSystem.MarkerRingStrength(0.5f, -1f), 1e-6f, "fades in with the piece");
            Assert.AreEqual(0f, ShipSystem.MarkerRingStrength(1f, 0f), "gone the moment it is collected");
            Assert.AreEqual(0f, ShipSystem.MarkerRingStrength(1f, 0.5f));

            var go = New("IntroShips");
            var ships = go.AddComponent<ShipSystem>();
            ships.flotsamRingInCozy = true;
            int a = ships.SpawnRouteFlotsam(ShipSystem.FlotsamKind.Crate, new Vector2(5000f, 1000f));
            int b = ships.SpawnRouteFlotsam(ShipSystem.FlotsamKind.Barrel, new Vector2(5003f, 1010f));
            Assert.GreaterOrEqual(a, 0);
            Assert.GreaterOrEqual(b, 0);
            SetPiece(ships, a, "fade", 1f);
            SetPiece(ships, b, "fade", 1f);
            Draw(ships);
            Assert.AreEqual(2, ships.FlotsamRingsShown);
            for (int i = 0; i < 2; i++)
            {
                Assert.AreEqual(1f, ships.FlotsamRingData(i).y, 1e-5f);
                Assert.AreEqual(ships.flotsamRingRadius, ships.FlotsamRingData(i).x, 1e-5f);
            }

            SetPiece(ships, a, "collect", 0f);
            Draw(ships);
            Assert.AreEqual(1, ships.FlotsamRingsShown, "the collected piece loses its ring at once");

            ships.flotsamRingStrength = 0f;
            Draw(ships);
            Assert.AreEqual(0, ships.FlotsamRingsShown, "0 = off");
        }

        static void SetPiece(ShipSystem ships, int slot, string field, float value)
        {
            var f = typeof(ShipSystem).GetField("_flotsam", BindingFlags.NonPublic | BindingFlags.Instance);
            var arr = (System.Array)f.GetValue(ships);
            object el = arr.GetValue(slot);
            el.GetType().GetField(field).SetValue(el, value);
            arr.SetValue(el, slot);
        }

        static void Draw(ShipSystem ships) =>
            typeof(ShipSystem).GetMethod("DrawBeacons", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ships, null);
    }
}
