using System.Collections.Generic;
using Drift.Islands;
using Drift.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The adventure flotsam stream (Encounters.LayTrack): a group every few units along the ring, laid out so that
    // collecting it and going round the islands is one line - never on land, never outside the band, and rarer as
    // the difficulty (the spacing) rises.
    public class AdventureFlotsamTests
    {
        // Far from the scene's own islands, and across the band so nothing else can reach into it.
        static readonly RingGeometry Ring = new RingGeometry(5000f, 28f, 640f);

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

        (Encounters enc, ShipSystem ships) MakeStream()
        {
            var go = New("AdvFlotsamShips");
            var ships = go.AddComponent<ShipSystem>();
            var enc = go.AddComponent<Encounters>();
            enc.ships = ships;
            enc.seed = 11;
            // Left inactive on purpose: the stream is driven by hand, nothing of the scene's sea is touched.
            return (enc, ships);
        }

        Island MakeIsland(Vector2 pos, float radius, int seed)
        {
            var go = New("AdvFlotsamIsle_" + seed);
            go.transform.position = new Vector3(pos.x, 0f, pos.y);
            var isl = go.AddComponent<Island>();
            isl.useKeyboardInput = false;
            isl.sinkEnabled = false;
            isl.landRadius = radius;
            isl.cellSize = IslandArchetypes.CellSize(radius);
            isl.shapeSeed = seed;
            go.SetActive(true);
            return isl;
        }

        static List<Vector2> Pieces(ShipSystem ships)
        {
            var list = new List<Vector2>();
            for (int i = 0; i < ships.FlotsamSlots; i++)
                if (ships.FlotsamActive(i)) list.Add(ships.FlotsamPosition(i));
            return list;
        }

        [Test]
        public void LaysAStreamAheadOfThePlayerInsideTheBand()
        {
            var (enc, ships) = MakeStream();
            var start = new Vector2(Ring.centerX, 1000f);
            int groups = enc.LayTrack(Ring, start, 1f, 20f, 60f, 3f);
            Assert.GreaterOrEqual(groups, 2, "a steady stream, not one lucky drop");
            var pieces = Pieces(ships);
            Assert.GreaterOrEqual(pieces.Count, groups, "every group leaves at least one piece");
            foreach (var p in pieces)
            {
                Assert.IsTrue(Ring.Inside(p, 0f), "inside the band: " + p);
                float dz = Ring.AlongDelta(p.y, start.y);
                Assert.Greater(dz, 0f, "ahead of the player");
                Assert.Less(dz, 90f, "and not beyond the horizon");
            }
        }

        [Test]
        public void HarderMeansRarer()
        {
            var (easy, easyShips) = MakeStream();
            easy.LayTrack(Ring, new Vector2(Ring.centerX, 2000f), 1f, 18f, 60f, 3f);
            int many = Pieces(easyShips).Count;

            var (hard, hardShips) = MakeStream();
            hard.LayTrack(Ring, new Vector2(Ring.centerX, 3000f), 1f, 60f, 60f, 3f);
            int few = Pieces(hardShips).Count;
            Assert.Greater(many, few, "flotsam gets rarer with the difficulty (" + many + " vs " + few + ")");
        }

        [Test]
        public void NeverOnAnIsland_ButHappyToUseTheGapBesideOne()
        {
            var (enc, ships) = MakeStream();
            var start = new Vector2(Ring.centerX, 4000f);
            // A big island in the middle of the band: the free water is the two gaps left and right of it.
            var rock = MakeIsland(new Vector2(Ring.centerX, start.y + 40f), 8f, 4711);
            enc.trackGapLines = 1f;
            enc.LayTrack(Ring, start, 1f, 14f, 70f, 3f);
            var pieces = Pieces(ships);
            Assert.Greater(pieces.Count, 0);
            int beside = 0;
            foreach (var p in pieces)
            {
                Vector2 d = new Vector2(p.x - rock.PlanarPosition.x, Ring.AlongDelta(p.y, rock.PlanarPosition.y));
                Assert.Greater(d.magnitude, rock.BoundingRadius, "never on the island: " + p);
                if (Mathf.Abs(d.y) < rock.BoundingRadius + 8f) beside++;
            }
            Assert.Greater(beside, 0, "the lines lead through the gaps beside the islands");
        }
    }
}
