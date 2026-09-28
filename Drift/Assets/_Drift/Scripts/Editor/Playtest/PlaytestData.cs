using System;
using System.Collections.Generic;

namespace Drift.EditorTools.Playtest
{
    // Everything a scenario run writes to Playtests/<scenario>_<time>.json, so later versions can be compared.
    [Serializable]
    public class PlaytestResult
    {
        public string scenario;
        public string version;
        public string started;
        public int seed;
        public string mode;
        public string bot;
        public bool directSteering;
        public string pipeline;
        public int screenW, screenH;
        public float realSeconds;
        public List<PhaseResult> phases = new();
        public List<EventRec> events = new();
        public List<LevelResult> levels = new();
        public List<MemorySample> memory = new();
        public List<string> notes = new();
        public List<string> shots = new();
        public List<string> errors = new();
    }

    [Serializable]
    public class EventRec
    {
        public float t;
        public string phase;
        public string kind;
        public float x, y, z;
        public float dist;
        public bool onScreen;
        public float px;
        public bool noticed;
    }

    [Serializable]
    public class PhaseResult
    {
        public string name;
        public float t0, t1;
        public float zoom;
        public float pxPerUnitAtPlayer;
        public float animalPx;
        // Cozy: gaps between noticed events (phase start and end count as edges).
        public int noticed;
        public int allEvents;
        public float medianGap, maxGap;
        public float windowsCovered;
        public string noticedByKind;
        // Frames.
        public int frames;
        public float avgMs, p95Ms, maxMs;
        public float avgMainMs;
        public float gcKbPerFrame;
        public float batches, setPass, trisK;
        // Player / feel.
        public float avgSpeed, avgTrackSpeed;
        public float opticFlow;
        public float landArea0, landArea1;
        public int merges;
        public float cameraJitter;
    }

    [Serializable]
    public class LevelResult
    {
        public int level;
        public float t0, t1;
        public float avgTrackSpeed;
        public float opticFlow;
        public int threats, pickups, nearMisses, hits, dodges, flotsam;
        public float decisionsPer10s;
        public float boostShare, surfShare, staggerShare;
    }

    [Serializable]
    public class MemorySample
    {
        public float t;
        public float totalMb, monoMb, gfxMb;
        public int islands, gameObjects;
        public float saveKb;
    }
}
