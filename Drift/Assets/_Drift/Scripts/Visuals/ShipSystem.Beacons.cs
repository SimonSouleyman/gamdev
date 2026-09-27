using Drift.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Visuals
{
    // Beacons on the flotsam that Encounters lays on the player's course, so a pickup reads from afar and at night
    // instead of as a tiny brown dot: a soft golden halo floating over the piece (never smaller on screen than
    // beaconMinAngle), a ring rippling out on the water around it and, at night, a pale pillar of light. In Adventure
    // every collectible piece also gets the outline ring the cozy watch tools draw under a picked animal (sand line,
    // flattened to 0.62, slow pulse) - alpha-blended with a faint dark edge, because the additive glows vanish on the
    // bright day sea (owner, 2026-09-25: "Das Treibgut ist tagsüber schlecht sichtbar").
    // One static mesh of BeaconSlots slots (Shaders/FlotsamBeacon.shader places every vertex from the _Beacons array),
    // so a rebuild only refills one Vector4 array. The object hangs under the ship mesh's child, so ShipSystem's
    // OnDisable takes it away together with that.
    public partial class ShipSystem
    {
        [Header("Leuchtfeuer auf Treibgut")]
        [Tooltip("Material mit Drift/FlotsamBeacon. Leer = Resources/DriftFlotsamBeacon.")]
        public Material beaconMaterial;
        [Tooltip("Treibgut auf dem Kurs bekommt einen Lichthof, einen Ring auf dem Wasser und nachts einen Lichtstrahl (0 = aus).")]
        [Range(0f, 2f)] public float beaconStrength = 1f;
        [Tooltip("Anteil der Leuchtkraft bei Tag (nachts voll).")]
        [Range(0f, 1f)] public float beaconDayShare = 0.5f;
        [Tooltip("Im Gemütlich-Modus leuchtet es nur so stark (Anteil).")]
        [Range(0f, 1f)] public float beaconCozyShare = 0.6f;
        public Color beaconColor = new Color(1f, 0.76f, 0.34f);
        [Tooltip("Radius des Lichthofs über dem Stück (Einheiten).")]
        [Range(0.2f, 3f)] public float beaconHalo = 0.9f;
        [Tooltip("Kleinste Größe von Lichthof und Ring aus der Ferne (Winkel als Tangens): so bleibt Treibgut auch weit vorne sichtbar.")]
        [Range(0f, 0.05f)] public float beaconMinAngle = 0.014f;
        [Tooltip("Radius des Rings auf dem Wasser (Einheiten).")]
        [Range(0.3f, 5f)] public float beaconRing = 1.7f;
        [Tooltip("Höhe des Lichtstrahls bei Nacht (Einheiten, 0 = kein Strahl).")]
        [Range(0f, 12f)] public float beaconBeam = 4.5f;

        [Header("Markierungsring um Treibgut")]
        [Tooltip("Stärke des Markierungsrings um jedes Treibgut-Teil im Abenteuer (wie der Ring unter einem angetippten Tier, 0 = aus).")]
        [Range(0f, 1.5f)] public float flotsamRingStrength = 1f;
        [Tooltip("Den Markierungsring auch im Gemütlich-Modus zeigen.")]
        public bool flotsamRingInCozy = false;
        [Tooltip("Farbe der Ringlinie (wie der Beobachten-Ring: Sand); Alpha = Deckkraft.")]
        public Color flotsamRingColor = new Color(1f, 0.87f, 0.64f, 0.9f);
        [Tooltip("Radius des Rings um ein Teil in Einheiten (bei normaler Teilgröße; wächst mit der Zeichengröße des Treibguts auf der Bahn).")]
        [Range(0.3f, 4f)] public float flotsamRingRadius = 1.2f;
        [Tooltip("Kleinster Ringradius aus der Ferne (Winkel als Tangens), damit der Ring weit vorne nicht zum Punkt wird.")]
        [Range(0f, 0.06f)] public float flotsamRingMinAngle = 0.017f;
        [Tooltip("Stauchung des Rings in der Höhe (der Beobachten-Ring: 0,62).")]
        [Range(0.3f, 1f)] public float flotsamRingAspect = 0.62f;
        [Tooltip("Deckkraft des dunklen Saums um die Linie bei Tag (hebt den Ring vom hellen Wasser ab; nachts weniger).")]
        [Range(0f, 1f)] public float flotsamRingShadow = 0.35f;

        public const int BeaconSlots = 32;
        public const int BeaconParts = 4, OutlinePart = 3;
        const string BeaconName = "FlotsamBeacons";
        const string BeaconShader = "Drift/FlotsamBeacon";
        const string BeaconResource = "DriftFlotsamBeacon";

        static readonly int BeaconsId = Shader.PropertyToID("_Beacons");
        static readonly int BeaconParamsId = Shader.PropertyToID("_BeaconParams");
        static readonly int BeaconColorId = Shader.PropertyToID("_BeaconColor");
        static readonly int BeaconRingsId = Shader.PropertyToID("_BeaconRings");
        static readonly int RingParamsId = Shader.PropertyToID("_RingParams");
        static readonly int RingColorId = Shader.PropertyToID("_RingColor");

        static Mesh _beaconMesh;
        static Material _beaconFallback;
        readonly Vector4[] _beaconData = new Vector4[BeaconSlots];
        readonly Vector4[] _ringData = new Vector4[BeaconSlots];
        MaterialPropertyBlock _beaconBlock;
        MeshRenderer _beaconRenderer;

        // Beacons drawn by the last rebuild (tests and the play-test recorder), and how many of them carry the ring.
        public int BeaconsShown { get; private set; }
        public int FlotsamRingsShown { get; private set; }
        // Beacon slot i as drawn by the last rebuild (tests): the piece (x, hop, z, glow strength) and its outline ring
        // (x = radius in u, y = strength, 0 = none).
        public Vector4 BeaconData(int i) => i >= 0 && i < BeaconSlots ? _beaconData[i] : Vector4.zero;
        public Vector4 FlotsamRingData(int i) => i >= 0 && i < BeaconSlots ? _ringData[i] : Vector4.zero;

        // The outline ring of a piece: in with its fade-in, gone the moment it is collected (the watch marker also
        // simply disappears once the card closes; a shrinking ring around a hopping piece read as a second pickup).
        public static float MarkerRingStrength(float fade, float collect) => collect >= 0f ? 0f : Mathf.Clamp01(fade);

        Material BeaconMaterial()
        {
            if (beaconMaterial != null) return beaconMaterial;
            if (_beaconFallback == null) _beaconFallback = Resources.Load<Material>(BeaconResource);
            if (_beaconFallback == null)
            {
                var shader = Shader.Find(BeaconShader);
                if (shader == null) return null;
                _beaconFallback = new Material(shader) { name = "FlotsamBeacons (runtime)", hideFlags = HideFlags.HideAndDontSave };
            }
            return _beaconFallback;
        }

        static Mesh BeaconMesh()
        {
            if (_beaconMesh != null) return _beaconMesh;
            // Per slot: halo billboard (part 0), ring on the water (1), light pillar (2), outline ring (3). The
            // alpha-blended outline rings come first in the index buffer, so the additive glows land on top of them.
            const int parts = BeaconParts;
            int quads = BeaconSlots * parts;
            var v = new Vector3[quads * 4];
            var uv = new Vector4[quads * 4];
            var tris = new int[quads * 6];
            int q = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int s = 0; s < BeaconSlots; s++)
                    for (int part = 0; part < parts; part++)
                    {
                        if ((part == OutlinePart) != (pass == 0)) continue;
                        for (int c = 0; c < 4; c++)
                            uv[q * 4 + c] = new Vector4(s, part, (c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f);
                        int b = q * 4, t = q * 6;
                        tris[t] = b; tris[t + 1] = b + 2; tris[t + 2] = b + 1;
                        tris[t + 3] = b + 1; tris[t + 4] = b + 2; tris[t + 5] = b + 3;
                        q++;
                    }
            _beaconMesh = new Mesh { name = BeaconName, hideFlags = HideFlags.HideAndDontSave };
            _beaconMesh.vertices = v;
            _beaconMesh.SetUVs(0, uv);
            _beaconMesh.triangles = tris;
            // Every vertex sits at the origin; the shader places them.
            _beaconMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            return _beaconMesh;
        }

        MeshRenderer EnsureBeaconRenderer()
        {
            if (_beaconRenderer != null) return _beaconRenderer;
            if (_go == null) return null;
            Transform t = _go.transform.Find(BeaconName);
            GameObject go = t != null ? t.gameObject : new GameObject(BeaconName);
            go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            go.transform.SetParent(_go.transform, false);
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null) mf = go.AddComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mf.sharedMesh = BeaconMesh();
            _beaconRenderer = mr;
            return mr;
        }

        // Called at the end of DrawFlotsam (every mesh rebuild): the pieces move with the ship mesh's cadence.
        void DrawBeacons()
        {
            int n = 0, rings = 0;
            bool adventure = GameModes.IsAdventure;
            float share = beaconStrength * (adventure ? 1f : beaconCozyShare);
            float ringShare = adventure || flotsamRingInCozy ? flotsamRingStrength : 0f;
            float ringRadius = flotsamRingRadius * (_onRing ? adventureRouteScale : 1f);
            if ((share > 0.001f || ringShare > 0.001f) && _flotsam != null)
            {
                for (int i = 0; i < _flotsam.Length && n < BeaconSlots; i++)
                {
                    ref Flotsam fl = ref _flotsam[i];
                    if (!fl.active || !fl.route || !IsCollectible(fl.kind)) continue;
                    float k = share > 0.001f ? RingReadability.BeaconStrength(fl.fade, fl.collect, _night, beaconDayShare) * share : 0f;
                    float ring = MarkerRingStrength(fl.fade, fl.collect) * ringShare;
                    if (k <= 0.01f && ring <= 0.01f) continue;
                    float hop = fl.collect >= 0f ? 0.9f * Mathf.Sin(Mathf.Clamp01(fl.collect) * Mathf.PI * 0.85f) : 0f;
                    if (ring > 0.01f) rings++;
                    else ring = 0f;
                    _ringData[n] = new Vector4(ringRadius, ring, 0f, 0f);
                    _beaconData[n++] = new Vector4(fl.pos.x, hop, fl.pos.y, k);
                }
            }
            for (int i = n; i < BeaconSlots; i++)
            {
                _beaconData[i] = Vector4.zero;
                _ringData[i] = Vector4.zero;
            }
            BeaconsShown = n;
            FlotsamRingsShown = rings;

            if (n == 0 && _beaconRenderer == null) return;
            var mr = EnsureBeaconRenderer();
            if (mr == null) return;
            var mat = BeaconMaterial();
            mr.enabled = n > 0 && mat != null;
            if (!mr.enabled) return;
            if (mr.sharedMaterial != mat) mr.sharedMaterial = mat;
            _beaconBlock ??= new MaterialPropertyBlock();
            _beaconBlock.SetVectorArray(BeaconsId, _beaconData);
            _beaconBlock.SetVector(BeaconParamsId, new Vector4(beaconHalo, beaconMinAngle, beaconRing, beaconBeam));
            Color c = beaconColor.linear;
            _beaconBlock.SetVector(BeaconColorId, new Vector4(c.r, c.g, c.b, Mathf.Clamp01(_night)));
            _beaconBlock.SetVectorArray(BeaconRingsId, _ringData);
            _beaconBlock.SetVector(RingParamsId, new Vector4(flotsamRingMinAngle, flotsamRingAspect, flotsamRingShadow, 0f));
            Color rc = flotsamRingColor.linear;
            _beaconBlock.SetVector(RingColorId, new Vector4(rc.r, rc.g, rc.b, flotsamRingColor.a));
            mr.SetPropertyBlock(_beaconBlock);
        }
    }
}
