using UnityEngine;

namespace Drift.Life
{
    // Per-animal animation state baked into the herd mesh for Drift/Animal (see the shader header for the
    // channel layout). Transitions are timed against IslandHerdSystem.Clock: the shader blends from/to
    // over its transition time starting at t0, so a state change needs one rebuild, not one per frame.
    public struct AnimalPose
    {
        public float phase, hopAmplitude, moving, alert, sleep, t0, pitchFrom, pitchTo, restFrom, restTo;
        // The state change at t0 did not flip the gait (a head turn, a gesture): the shader must not play the
        // quarter-second walk blend it otherwise assumes every change to be (hares hopped on every pose change).
        public bool gaitSteady;
    }

    // What one mover was baked with: TemplateBatch's transform arguments.
    public struct BakeTransform
    {
        public Vector3 pos;
        public float yaw, scale, roll, pitch;

        public BakeTransform(Vector3 pos, float yaw, float scale, float roll = 0f, float pitch = 0f)
        {
            this.pos = pos;
            this.yaw = yaw;
            this.scale = scale;
            this.roll = roll;
            this.pitch = pitch;
        }

        public static BakeTransform Lerp(in BakeTransform a, in BakeTransform b, float t) => new BakeTransform(
            Vector3.LerpUnclamped(a.pos, b.pos, t), Mathf.LerpAngle(a.yaw, b.yaw, t), Mathf.LerpUnclamped(a.scale, b.scale, t),
            Mathf.LerpAngle(a.roll, b.roll, t), Mathf.LerpAngle(a.pitch, b.pitch, t));

        public bool Same(in BakeTransform o) =>
            (pos - o.pos).sqrMagnitude < 1e-10f && Mathf.Abs(Mathf.DeltaAngle(yaw, o.yaw)) < 0.01f && Mathf.Abs(scale - o.scale) < 1e-5f
            && Mathf.Abs(Mathf.DeltaAngle(roll, o.roll)) < 0.01f && Mathf.Abs(Mathf.DeltaAngle(pitch, o.pitch)) < 0.01f;
    }

    // The motion channel of one mover (CloseUpMotion / DriftMotion.hlsl): the slide the mesh currently shows, from
    // `from` at bake time `at` to `to` over `duration`. Next() starts the next slide where the mover is drawn right
    // now, so the picture never jumps, whether the next bake comes early, on time or late.
    public struct MotionTrack
    {
        public BakeTransform from, to;
        public double at;
        public float duration;
        public bool valid;

        // Where the mover is drawn at `now`.
        public BakeTransform Shown(double now)
        {
            if (!valid) return to;
            float f = duration > 0f ? Mathf.Clamp01((float)((now - at) / duration)) : 1f;
            return BakeTransform.Lerp(from, to, f);
        }

        // Records a bake at `now` of `target` sliding over `seconds`; false (and start = target) when there is
        // nothing to slide: first bake, a jump beyond snapDistance, or the mover is where it is drawn.
        public bool Next(in BakeTransform target, double now, float seconds, float snapDistance, out BakeTransform start)
        {
            start = valid ? Shown(now) : target;
            if ((target.pos - start.pos).sqrMagnitude > snapDistance * snapDistance) start = target;
            from = start;
            to = target;
            at = now;
            duration = seconds;
            valid = true;
            return !start.Same(target);
        }

        public void Shift(Vector3 delta)
        {
            from.pos += delta;
            to.pos += delta;
        }
    }

    public class TemplateBatch
    {
        Vector3[] _v = new Vector3[1024];
        Vector3[] _n = new Vector3[1024];
        Color[] _c = new Color[1024];
        Vector4[] _uv0 = new Vector4[1024];
        Vector4[] _uv1 = new Vector4[1024];
        Vector4[] _uv2 = new Vector4[1024];
        // Marking channel (UV3, see Markings), only grown by batches that hold animals: plant batches stay small.
        Vector4[] _uv3 = System.Array.Empty<Vector4>();
        // Motion channel (UV4, Shaders/DriftMotion.hlsl): the previous bake's position + CloseUpMotion.Encode, only
        // in batches that called UseMotion; everything added without SetMotion stands still (w = 0).
        Vector4[] _uv4 = System.Array.Empty<Vector4>();
        bool _motion;
        int[] _t = new int[2048];
        int _vc, _tc;
        // Number of UV channels this batch writes (0 none, 1 plants, 3 critters, 4 animals); Apply uploads only those.
        int _uvChannels;
        bool _hasUv => _uvChannels > 0;

        public int VertexCount => _vc;
        public int UvChannels => _uvChannels;
        public bool HasMotion => _motion;
        // First vertex of the last template added (for SetMotion / SetPhase).
        public int LastBase { get; private set; }

        public void Begin()
        {
            _vc = 0;
            _tc = 0;
            _uvChannels = 0;
            _motion = false;
        }

        // Call right after Begin: this batch carries the motion channel.
        public void UseMotion()
        {
            _motion = true;
            if (_uv4.Length < _v.Length) _uv4 = new Vector4[_v.Length];
            System.Array.Clear(_uv4, 0, _vc);
        }

        // The last added template slides in from where it was drawn at the previous bake (pos/yaw/scale/roll/pitch
        // as they were passed then) over `encoded` (CloseUpMotion.Encode). Same maths as AddTransformed.
        public void SetMotion(PlantTemplate tpl, Vector3 pos, float yaw, float scale, float roll, float pitch, float encoded)
        {
            if (!_motion) return;
            Basis(yaw, roll, pitch, out Vector3 ax, out Vector3 ay, out Vector3 az);
            Vector3 sx = ax * scale, sy = ay * scale, sz = az * scale;
            var verts = tpl.vertices;
            var uv4 = _uv4;
            int b = LastBase;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 p = verts[i];
                uv4[b + i] = new Vector4(
                    pos.x + sx.x * p.x + sy.x * p.y + sz.x * p.z,
                    pos.y + sx.y * p.x + sy.y * p.y + sz.y * p.z,
                    pos.z + sx.z * p.x + sy.z * p.y + sz.z * p.z, encoded);
            }
        }

        // Per-template phase in UV0.y (UV0.x = 0: no wind bend): the far flock's wing beat for Drift/VertexColor.
        public void SetPhase(PlantTemplate tpl, float phase)
        {
            int b = LastBase;
            UseUv(1, b);
            var uv0 = _uv0;
            for (int i = 0; i < tpl.vertices.Length; i++) uv0[b + i] = new Vector4(0f, phase, 0f, 0f);
        }

        static void Basis(float yaw, float roll, float pitch, out Vector3 ax, out Vector3 ay, out Vector3 az)
        {
            if (roll == 0f && pitch == 0f)
            {
                float rad = yaw * Mathf.Deg2Rad;
                float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
                ax = new Vector3(cs, 0f, -sn);
                ay = Vector3.up;
                az = new Vector3(sn, 0f, cs);
                return;
            }
            Quaternion q = Quaternion.Euler(pitch, yaw, roll);
            ax = q * Vector3.right;
            ay = q * Vector3.up;
            az = q * Vector3.forward;
        }

        void UseUv(int channels, int baseIndex)
        {
            if (_uvChannels >= channels) return;
            if (_uvChannels < 1) System.Array.Clear(_uv0, 0, baseIndex);
            if (_uvChannels < 2 && channels >= 2) System.Array.Clear(_uv1, 0, baseIndex);
            if (_uvChannels < 3 && channels >= 3) System.Array.Clear(_uv2, 0, baseIndex);
            if (_uvChannels < 4 && channels >= 4)
            {
                if (_uv3.Length < _v.Length) _uv3 = new Vector4[_v.Length];
                System.Array.Clear(_uv3, 0, baseIndex);
            }
            _uvChannels = channels;
        }

        void Reserve(int verts, int tris)
        {
            int nv = _vc + verts;
            if (nv > _v.Length)
            {
                int cap = Mathf.Max(nv, _v.Length * 2);
                System.Array.Resize(ref _v, cap);
                System.Array.Resize(ref _n, cap);
                System.Array.Resize(ref _c, cap);
                System.Array.Resize(ref _uv0, cap);
                System.Array.Resize(ref _uv1, cap);
                System.Array.Resize(ref _uv2, cap);
                if (_uv3.Length > 0) System.Array.Resize(ref _uv3, cap);
                if (_uv4.Length > 0) System.Array.Resize(ref _uv4, cap);
            }
            int nt = _tc + tris;
            if (nt > _t.Length) System.Array.Resize(ref _t, Mathf.Max(nt, _t.Length * 2));
        }

        public void Add(PlantTemplate tpl, Vector3 pos, float yaw, float scale, float roll = 0f, float pitch = 0f, bool wings = false) =>
            Add(tpl, pos, yaw, scale, roll, pitch, wings ? tpl.wing : null, 1f, default, 0f);

        // alpha (per template vertex, may be null) * alphaScale goes to the colour's alpha channel, which the
        // shaders read as the sway/flap weight; tint blends every vertex colour towards a burn/char colour.
        public void Add(PlantTemplate tpl, Vector3 pos, float yaw, float scale, float roll, float pitch,
                        float[] alpha, float alphaScale, Color tint, float tintAmount)
        {
            int baseIndex = AddTransformed(tpl, pos, yaw, scale, roll, pitch, alpha, alphaScale, tint, tintAmount);
            if (_hasUv)
            {
                int n = tpl.vertices.Length;
                System.Array.Clear(_uv0, baseIndex, n);
                if (_uvChannels >= 2) System.Array.Clear(_uv1, baseIndex, n);
                if (_uvChannels >= 3) System.Array.Clear(_uv2, baseIndex, n);
                if (_uvChannels >= 4) System.Array.Clear(_uv3, baseIndex, n);
            }
        }

        // Plant with wind sway for Drift/VertexColor (Phase 5):
        //   UV0 = (sway weight 0..1 from the template height * swayScale, phase, vertex height above the ground
        //   in world units, surface part tpl.part for Drift/Vegetation or 0)   UV1 = UV2 = 0
        // The shader bends a vertex along the global _LifeWind by weight^2 * height, so a zero weight (herds,
        // flocks, borders, the bolt) stays rigid. colorScale multiplies the vertex colours (bloom brightness,
        // seasons, fresh shoots) after the tint blend.
        public void AddPlant(PlantTemplate tpl, Vector3 pos, float yaw, float scale, float swayScale, float phase,
                             Color colorScale, Color tint, float tintAmount)
        {
            UseUv(1, _vc);
            int baseIndex = AddTransformed(tpl, pos, yaw, scale, 0f, 0f, null, 0f, tint, tintAmount, colorScale, tpl.sway, swayScale, phase);
            int n = tpl.vertices.Length;
            if (_uvChannels >= 2) System.Array.Clear(_uv1, baseIndex, n);
            if (_uvChannels >= 3) System.Array.Clear(_uv2, baseIndex, n);
            if (_uvChannels >= 4) System.Array.Clear(_uv3, baseIndex, n);
        }

        // Animal with shader-side animation (Drift/Animal):
        //   UV0 = (pitch lever: local forward * scale, up: local height * scale, phase, hop amplitude)
        //   UV1 = (moving, alert, sleep, t0)   UV2 = (pitchFrom, pitchTo, restFrom, restTo)   UV3 = markings (Markings)
        // A batch that uses this writes UV channels for every vertex it holds, so plain Add calls in the
        // same batch get zeroed UVs. scale is the whole animal (a young one is simply baked smaller, the
        // lever/up channels scale with it); tint/tintAmount blend the vertex colours like Add.
        public void AddAnimal(PlantTemplate tpl, Vector3 pos, float yaw, float scale, in AnimalPose pose) =>
            AddAnimal(tpl, pos, yaw, scale, pose, default, 0f);

        public void AddAnimal(PlantTemplate tpl, Vector3 pos, float yaw, float scale, in AnimalPose pose, Color tint, float tintAmount) =>
            AddAnimal(tpl, pos, yaw, scale, pose, tint, tintAmount, 0f);

        // Detail templates (AnimalModels) bring their own lever / up values and a signed leg lift that goes to
        // the colour alpha (0 on simple templates = no leg animation). roll (degrees about the body axis) is
        // baked into the positions only, for a wallowing animal whose pose is pitch 0 / rest 0.
        public void AddAnimal(PlantTemplate tpl, Vector3 pos, float yaw, float scale, in AnimalPose pose, Color tint, float tintAmount, float roll) =>
            AddAnimal(tpl, pos, yaw, scale, pose, tint, tintAmount, roll, 0f);

        // pitch (degrees about the animal's side axis, positive = nose down) is baked like roll: a fox's nose-dive,
        // a simple penguin tipped onto its belly.
        public void AddAnimal(PlantTemplate tpl, Vector3 pos, float yaw, float scale, in AnimalPose pose, Color tint, float tintAmount, float roll, float pitch) =>
            AddAnimal(tpl, pos, yaw, scale, pose, tint, tintAmount, roll, pitch, Markings.Of(tpl), 0f);

        // marks (per template vertex, Markings.Of / Markings.SimpleMarks, may be null = unmarked) go to UV3 with the
        // individual's seed 0..1 added to the code, so a herd is not a row of clones.
        public void AddAnimal(PlantTemplate tpl, Vector3 pos, float yaw, float scale, in AnimalPose pose, Color tint, float tintAmount, float roll, float pitch,
                              Vector4[] marks, float seed)
        {
            int baseIndex = AddTransformed(tpl, pos, yaw, scale, roll, pitch, null, 1f, tint, tintAmount);
            UseUv(4, baseIndex);
            var verts = tpl.vertices;
            if (marks != null && marks.Length == verts.Length)
            {
                var uv3 = _uv3;
                float s = Markings.SeedCode(seed);
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector4 m = marks[i];
                    m.w += s;
                    uv3[baseIndex + i] = m;
                }
            }
            else System.Array.Clear(_uv3, baseIndex, verts.Length);
            var uv0 = _uv0; var uv1 = _uv1; var uv2 = _uv2;
            var gait = new Vector4(pose.gaitSteady ? pose.moving + 2f : pose.moving, pose.alert, pose.sleep, pose.t0);
            var blend = new Vector4(pose.pitchFrom, pose.pitchTo, pose.restFrom, pose.restTo);
            var lever = tpl.lever;
            if (lever == null)
            {
                for (int i = 0; i < verts.Length; i++)
                {
                    uv0[baseIndex + i] = new Vector4(verts[i].z * scale, verts[i].y * scale, pose.phase, pose.hopAmplitude);
                    uv1[baseIndex + i] = gait;
                    uv2[baseIndex + i] = blend;
                }
                return;
            }
            var up = tpl.up;
            var leg = tpl.leg;
            var c = _c;
            for (int i = 0; i < verts.Length; i++)
            {
                uv0[baseIndex + i] = new Vector4(lever[i] * scale, up[i] * scale, pose.phase, pose.hopAmplitude);
                uv1[baseIndex + i] = gait;
                uv2[baseIndex + i] = blend;
                c[baseIndex + i].a = leg[i] * scale;
            }
        }

        // Critter with shader-side animation (Drift/Critter):
        //   UV0 = (wing tip offset from the midline in world xz: template x * wing weight * flutter, phase, glow)
        //   UV1 = (bob amplitude, 0, 0, 0)   UV2 = 0
        // flutter 1 folds the template's wing vertices (tpl.wing weight, |x| beyond 0.09) up about the body
        // axis; bob is the vertical wobble in world units; glow 1 draws the vertices unlit and blinking.
        public void AddCritter(PlantTemplate tpl, Vector3 pos, float yaw, float scale, float flutter, float bob, float phase, float glow)
        {
            int baseIndex = AddTransformed(tpl, pos, yaw, scale, 0f, 0f, null, 1f, default, 0f);
            UseUv(3, baseIndex);
            float rad = yaw * Mathf.Deg2Rad;
            float cs = Mathf.Cos(rad) * scale * flutter, sn = -Mathf.Sin(rad) * scale * flutter;
            var verts = tpl.vertices;
            var wing = tpl.wing;
            var uv0 = _uv0; var uv1 = _uv1; var uv2 = _uv2;
            var bobV = new Vector4(bob, 0f, 0f, 0f);
            for (int i = 0; i < verts.Length; i++)
            {
                float w = wing[i] * verts[i].x;
                uv0[baseIndex + i] = new Vector4(cs * w, sn * w, phase, glow);
                uv1[baseIndex + i] = bobV;
                uv2[baseIndex + i] = default;
            }
            if (_uvChannels >= 4) System.Array.Clear(_uv3, baseIndex, verts.Length);
        }

        int AddTransformed(PlantTemplate tpl, Vector3 pos, float yaw, float scale, float roll, float pitch,
                           float[] alpha, float alphaScale, Color tint, float tintAmount) =>
            AddTransformed(tpl, pos, yaw, scale, roll, pitch, alpha, alphaScale, tint, tintAmount, Color.white, null, 0f, 0f);

        // One pass per vertex: transform, colour (tint blend, then the colorScale multiply, alpha) and, with
        // sway weights, the UV0 wind channel - a second pass over 60k vertices was measurable.
        int AddTransformed(PlantTemplate tpl, Vector3 pos, float yaw, float scale, float roll, float pitch,
                           float[] alpha, float alphaScale, Color tint, float tintAmount,
                           Color colorScale, float[] sway, float swayScale, float phase)
        {
            var verts = tpl.vertices;
            var norms = tpl.normals;
            var cols = tpl.colors;
            var tris = tpl.triangles;
            var part = sway != null ? tpl.part : null;
            Reserve(verts.Length, tris.Length);
            bool doScale = colorScale.r != 1f || colorScale.g != 1f || colorScale.b != 1f;
            var uv0 = _uv0;

            Basis(yaw, roll, pitch, out Vector3 ax, out Vector3 ay, out Vector3 az);
            Vector3 sx = ax * scale, sy = ay * scale, sz = az * scale;

            int baseIndex = _vc;
            LastBase = baseIndex;
            if (_motion)
            {
                if (_uv4.Length < _v.Length) System.Array.Resize(ref _uv4, _v.Length);
                System.Array.Clear(_uv4, baseIndex, verts.Length);
            }
            var v = _v; var n = _n; var c = _c;
            bool doTint = tintAmount > 0f;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 p = verts[i];
                v[baseIndex + i] = new Vector3(
                    pos.x + sx.x * p.x + sy.x * p.y + sz.x * p.z,
                    pos.y + sx.y * p.x + sy.y * p.y + sz.y * p.z,
                    pos.z + sx.z * p.x + sy.z * p.y + sz.z * p.z);
                Vector3 nn = norms[i];
                n[baseIndex + i] = new Vector3(
                    ax.x * nn.x + ay.x * nn.y + az.x * nn.z,
                    ax.y * nn.x + ay.y * nn.y + az.y * nn.z,
                    ax.z * nn.x + ay.z * nn.y + az.z * nn.z);
                Color col = cols[i];
                if (doTint)
                {
                    col.r += (tint.r - col.r) * tintAmount;
                    col.g += (tint.g - col.g) * tintAmount;
                    col.b += (tint.b - col.b) * tintAmount;
                }
                if (doScale)
                {
                    col.r *= colorScale.r; col.g *= colorScale.g; col.b *= colorScale.b;
                }
                col.a = alpha != null ? alpha[i] * alphaScale : 0f;
                c[baseIndex + i] = col;
                if (sway != null) uv0[baseIndex + i] = new Vector4(sway[i] * swayScale, phase, p.y * scale, part != null ? part[i] : 0f);
            }
            _vc += verts.Length;

            var t = _t;
            int tb = _tc;
            for (int i = 0; i < tris.Length; i++) t[tb + i] = baseIndex + tris[i];
            _tc += tris.Length;
            return baseIndex;
        }

        public void Apply(Mesh m)
        {
            // Clear() keeps the vertex layout, and with it any channel an earlier bake wrote and this one does not: a
            // leftover motion channel would replay old slides, a leftover UV0 would bend a far bird in the wind.
            bool keep = _motion || !m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord4);
            for (int c = _uvChannels; c < 4 && keep; c++)
                if (m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0 + c)) keep = false;
            m.Clear(keep);
            if (_vc > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(_v, 0, _vc);
            m.SetNormals(_n, 0, _vc);
            m.SetColors(_c, 0, _vc);
            if (_uvChannels >= 1) m.SetUVs(0, _uv0, 0, _vc);
            if (_uvChannels >= 2) m.SetUVs(1, _uv1, 0, _vc);
            if (_uvChannels >= 3) m.SetUVs(2, _uv2, 0, _vc);
            if (_uvChannels >= 4) m.SetUVs(3, _uv3, 0, _vc);
            if (_motion) m.SetUVs(4, _uv4, 0, _vc);
            m.SetTriangles(_t, 0, _tc, 0, false);
            m.RecalculateBounds();
        }
    }
}
