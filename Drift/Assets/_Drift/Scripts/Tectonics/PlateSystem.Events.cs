using System;
using System.Collections.Generic;
using UnityEngine;

namespace Drift.Tectonics
{
    public enum PlateEventType { Uplift, Earthquake, Rift, Slip }

    [Serializable]
    public struct PlateEventData
    {
        public PlateEventType type;
        public BoundaryKind boundary;
        public Vector2 position;
        public float strength;
        public float duration;
        public float age;

        public float Progress => duration > 0f ? Mathf.Clamp01(age / duration) : 1f;
    }

    public partial class PlateSystem
    {
        const int MaxEventsPerBorderStep = 6;

        public bool enableEvents = true;
        public float eventRate = 1f;
        public float upliftRate = 0.03f;
        public float earthquakeRate = 0.015f;
        public float riftRate = 0.03f;
        public float slipRate = 0.02f;
        public float eventKick = 2f;
        public float eventInfluence = 16f;
        public int maxActiveEvents = 48;
        public bool showEventCues = true;

        public event Action<PlateEventData> EventStarted;
        public event Action<PlateEventData> EventEnded;

        readonly List<PlateEventData> _events = new();
        uint _eventRng;

        public IReadOnlyList<PlateEventData> ActiveEvents => _events;

        uint SeedEventRng() => (uint)Hash(seed, 7919, 104729) | 1u;

        float NextEventRandom()
        {
            if (_eventRng == 0u) _eventRng = SeedEventRng();
            uint x = _eventRng;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _eventRng = x;
            return (x & 0xFFFFFFu) / (float)0x1000000;
        }

        float EventRand(float a, float b) => a + NextEventRandom() * (b - a);

        void ResetEvents()
        {
            _events.Clear();
            _eventRng = SeedEventRng();
        }

        void CaptureEvents(PlateSaveData d)
        {
            d.eventRng = _eventRng;
            d.events.Clear();
            d.events.AddRange(_events);
        }

        void RestoreEvents(PlateSaveData d)
        {
            _events.Clear();
            _eventRng = d.eventRng != 0u ? d.eventRng : SeedEventRng();
            if (d.events != null) _events.AddRange(d.events);
        }

        public float UpliftAt(Vector2 pos)
        {
            float sum = 0f;
            for (int i = 0; i < _events.Count; i++)
            {
                var e = _events[i];
                if (e.type != PlateEventType.Uplift) continue;
                float d = Vector2.Distance(pos, e.position);
                if (d >= eventInfluence) continue;
                float t = e.Progress;
                float envelope = Mathf.Sin(t * Mathf.PI);
                float fall = 1f - d / eventInfluence;
                sum += e.strength * envelope * fall * fall;
            }
            return sum;
        }

        public void FireEvent(PlateEventType type, Vector2 position, float strength, float duration)
        {
            var e = new PlateEventData
            {
                type = type,
                boundary = BoundaryOf(type),
                position = position,
                strength = strength,
                duration = Mathf.Max(0.01f, duration),
                age = 0f
            };
            if (_events.Count >= maxActiveEvents) _events.RemoveAt(0);
            _events.Add(e);
            EventStarted?.Invoke(e);
        }

        static BoundaryKind BoundaryOf(PlateEventType t)
        {
            switch (t)
            {
                case PlateEventType.Uplift:
                case PlateEventType.Earthquake: return BoundaryKind.Convergent;
                case PlateEventType.Rift: return BoundaryKind.Divergent;
                default: return BoundaryKind.Transform;
            }
        }

        void StepEvents(float gdt)
        {
            for (int i = _events.Count - 1; i >= 0; i--)
            {
                var e = _events[i];
                e.age += gdt;
                if (e.age >= e.duration)
                {
                    _events.RemoveAt(i);
                    EventEnded?.Invoke(e);
                }
                else _events[i] = e;
            }

            if (!enableEvents || eventRate <= 0f || gdt <= 0f) return;

            float invScale = 1f / Mathf.Max(timeScale, 0.01f);
            for (int i = 0; i < Borders.Count; i++)
            {
                var b = Borders[i];
                Vector2 line = b.p1 - b.p0;
                float len = line.magnitude;
                if (len < 0.01f) continue;
                Vector2 dirLine = line / len;
                float lenUnits = len * 0.01f;
                float closing = Mathf.Abs(b.closing) * invScale;
                float slide = Mathf.Abs(Vector2.Dot(b.b.velocity - b.a.velocity, dirLine)) * invScale;

                switch (b.kind)
                {
                    case BoundaryKind.Convergent:
                        RollEvents(b, PlateEventType.Uplift, upliftRate * closing * lenUnits * gdt, closing);
                        RollEvents(b, PlateEventType.Earthquake, earthquakeRate * closing * lenUnits * gdt, closing);
                        break;
                    case BoundaryKind.Divergent:
                        RollEvents(b, PlateEventType.Rift, riftRate * closing * lenUnits * gdt, closing);
                        break;
                    default:
                        RollEvents(b, PlateEventType.Slip, slipRate * (0.5f + slide) * lenUnits * gdt, slide);
                        break;
                }
            }
        }

        void RollEvents(in Border b, PlateEventType type, float expected, float speed)
        {
            expected *= eventRate;
            if (expected <= 0f) return;
            int n = Mathf.Min(Mathf.FloorToInt(expected), MaxEventsPerBorderStep);
            if (n < MaxEventsPerBorderStep && NextEventRandom() < expected - Mathf.Floor(expected)) n++;
            for (int k = 0; k < n; k++)
            {
                float at = NextEventRandom();
                Vector2 pos = SeamPoint(b, at);
                float strength = Mathf.Clamp01(speed / 3f) * EventRand(0.5f, 1f);
                float duration;
                switch (type)
                {
                    case PlateEventType.Uplift: duration = EventRand(6f, 12f); break;
                    case PlateEventType.Rift: duration = EventRand(8f, 14f); break;
                    case PlateEventType.Earthquake: duration = EventRand(1.5f, 3f); break;
                    default: duration = EventRand(1f, 2f); break;
                }
                FireEvent(type, pos, strength, duration);

                if (type == PlateEventType.Earthquake || type == PlateEventType.Slip)
                {
                    // Along the local seam, in the sense the border runs around plate a (p0/p1 are ordered canonically).
                    Vector2 t = SeamTangent(b, at);
                    Vector2 ab = b.b.position - b.a.position;
                    if (t.x * -ab.y + t.y * ab.x < 0f) t = -t;
                    Vector2 kick = t * (eventKick * strength);
                    b.a.core.pushVel += kick;
                    b.b.core.pushVel -= kick;
                    _dynamic.Add(b.a.core);
                    _dynamic.Add(b.b.core);
                }
            }
        }

        void AddEventCues()
        {
            if (!showEventCues) return;
            for (int i = 0; i < _events.Count; i++)
            {
                var e = _events[i];
                AddPulse(e.position, 2.6f + 3.2f * e.strength, (float)e.type, e.Progress, e.strength);
            }
        }
    }
}
