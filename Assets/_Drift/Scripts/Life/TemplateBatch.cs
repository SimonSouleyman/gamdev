using UnityEngine;

namespace Drift.Life
{
    // Per-animal animation state baked into the herd mesh for Drift/Animal (see the shader header for the
    // channel layout). Transitions are timed against IslandHerdSystem.Clock: the shader blends from/to
    // over its transition time starting at t0, so a state change needs one rebuild, not one per frame.
    public struct AnimalPose
    {
        public float phase, hopAmplitude, moving, alert, sleep, t0, pitchFrom, pitchTo, restFrom, restTo;
    }

    public class TemplateBatch
    {
        Vector3[] _v = new Vector3[1024];
        Vector3[] _n = new Vector3[1024];
        Color[] _c = new Color[1024];
        Vector4[] _uv0 = new Vector4[1024];
        Vector4[] _uv1 = new Vector4[1024];
        Vector4[] _uv2 = new Vector4[1024];
        int[] _t = new int[2048];
        int _vc, _tc;
        // Number of UV channels this batch writes (0 none, 1 plants, 3 animals/critters); Apply uploads only those.
        int _uvChannels;
        bool _hasUv => _uvChannels > 0;

        public int VertexCount => _vc;
        public int UvChannels => _uvChannels;

        public void Begin()
        {
            _vc = 0;
            _tc = 0;
            _uvChannels = 0;
        }

        void UseUv(int channels, int baseIndex)
        {
            if (_uvChannels >= channels) return;
            if (_uvChannels < 1) System.Array.Clear(_uv0, 0, baseIndex);
            if (_uvChannels < 2 && channels >= 2) System.Array.Clear(_uv1, 0, baseIndex);
            if (_uvChannels < 3 && channels >= 3) System.Array.Clear(_uv2, 0, baseIndex);
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
            }
        }

        // Plant with wind sway for Drift/VertexColor (Phase 5):
        //   UV0 = (sway weight 0..1 from the template height * swayScale, phase, vertex height above the ground
        //   in world units, 0)   UV1 = UV2 = 0
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
        }

        // Animal with shader-side animation (Drift/Animal):
        //   UV0 = (pitch lever: local forward * scale, up: local height * scale, phase, hop amplitude)
        //   UV1 = (moving, alert, sleep, t0)   UV2 = (pitchFrom, pitchTo, restFrom, restTo)
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
        public void AddAnimal(PlantTemplate tpl, Vector3 pos, float yaw, float scale, in AnimalPose pose, Color tint, float tintAmount, float roll, float pitch)
        {
            int baseIndex = AddTransformed(tpl, pos, yaw, scale, roll, pitch, null, 1f, tint, tintAmount);
            UseUv(3, baseIndex);
            var verts = tpl.vertices;
            var uv0 = _uv0; var uv1 = _uv1; var uv2 = _uv2;
            var gait = new Vector4(pose.moving, pose.alert, pose.sleep, pose.t0);
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
            Reserve(verts.Length, tris.Length);
            bool doScale = colorScale.r != 1f || colorScale.g != 1f || colorScale.b != 1f;
            var uv0 = _uv0;

            Vector3 ax, ay, az;
            if (roll == 0f && pitch == 0f)
            {
                float rad = yaw * Mathf.Deg2Rad;
                float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
                ax = new Vector3(cs, 0f, -sn);
                ay = Vector3.up;
                az = new Vector3(sn, 0f, cs);
            }
            else
            {
                Quaternion q = Quaternion.Euler(pitch, yaw, roll);
                ax = q * Vector3.right;
                ay = q * Vector3.up;
                az = q * Vector3.forward;
            }
            Vector3 sx = ax * scale, sy = ay * scale, sz = az * scale;

            int baseIndex = _vc;
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
                if (sway != null) uv0[baseIndex + i] = new Vector4(sway[i] * swayScale, phase, p.y * scale, 0f);
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
            m.Clear();
            if (_vc > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(_v, 0, _vc);
            m.SetNormals(_n, 0, _vc);
            m.SetColors(_c, 0, _vc);
            if (_uvChannels >= 1) m.SetUVs(0, _uv0, 0, _vc);
            if (_uvChannels >= 2) m.SetUVs(1, _uv1, 0, _vc);
            if (_uvChannels >= 3) m.SetUVs(2, _uv2, 0, _vc);
            m.SetTriangles(_t, 0, _tc, 0, false);
            m.RecalculateBounds();
        }
    }
}
