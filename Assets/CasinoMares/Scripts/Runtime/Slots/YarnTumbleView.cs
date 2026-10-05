using CasinoMares.Audio;
using CasinoMares.Rules;
using UnityEngine;

namespace CasinoMares.Slots
{
    /// <summary>
    /// Plays a Yarn Tumble spin out on every client from the machine's seed: the old symbols fall out and the new
    /// ones drop in column by column, winning groups glow and get batted away (the seated cat swats at them), the
    /// rest fall into the gaps and new symbols tumble in from the top, chain after chain, while the multiplier lamps
    /// under the grid light up. The grid is one mesh of quads, clipped to the window.
    /// </summary>
    public class YarnTumbleView : MonoBehaviour
    {
        const int Size = YarnTumbleRules.Size;
        /// <summary>The window's width and height in the grid's local space.</summary>
        const float WindowSize = 0.6f;
        const int Lamps = 6;
        const int MaxQuads = YarnTumbleRules.Cells * 3 + Lamps;

        // The drop-in, in seconds from the start of the spin (see YarnTumbleTiming.DropIn).
        const float OutStagger = 0.07f;
        const float OutTime = 0.3f;
        const float InStart = 0.25f;
        const float InStagger = 0.09f;
        const float InTime = 0.45f;
        const float BounceTime = 0.12f;
        const float BounceHeight = 0.1f;

        [SerializeField] SlotMachine _machine;
        [SerializeField] SlotCelebration _celebration;
        [SerializeField] MeshFilter _grid;
        [SerializeField] MeshRenderer _sign;

        Mesh _mesh;
        Vector3[] _vertices;
        Vector2[] _uvs;
        int _quads;
        int _lastQuads;
        /// <summary>Nothing moves at rest, so the mesh is only rebuilt once then.</summary>
        bool _restDrawn;
        static int[] _idleGrid;

        int _lastSpin = -1;
        bool _landed;
        int _columnsLanded;
        int _glowed;
        int _batted;
        readonly int[] _resting = new int[YarnTumbleRules.Cells];
        readonly int[] _previous = new int[YarnTumbleRules.Cells];

        void Awake()
        {
            var renderer = _grid.GetComponent<MeshRenderer>();
            var material = new Material(renderer.sharedMaterial) { name = "YarnTumbleGrid (runtime)" };
            material.SetTexture("_BaseMap", YarnTumbleSymbols.Atlas);
            renderer.sharedMaterial = material;
            if (_sign != null)
            {
                var sign = new Material(_sign.sharedMaterial) { name = "YarnTumbleSign (runtime)" };
                sign.SetTexture("_BaseMap", YarnTumbleSymbols.Sign);
                // Brighter than white, so the bloom makes it glow like the other marquees.
                sign.SetColor("_BaseColor", new Color(1.2f, 1.2f, 1.2f));
                _sign.sharedMaterial = sign;
            }

            _vertices = new Vector3[MaxQuads * 4];
            _uvs = new Vector2[MaxQuads * 4];
            var triangles = new int[MaxQuads * 6];
            for (var q = 0; q < MaxQuads; q++)
            {
                // Corners go bottom left, top left, top right, bottom right as the player sees them: clockwise.
                triangles[q * 6] = q * 4;
                triangles[q * 6 + 1] = q * 4 + 1;
                triangles[q * 6 + 2] = q * 4 + 2;
                triangles[q * 6 + 3] = q * 4;
                triangles[q * 6 + 4] = q * 4 + 2;
                triangles[q * 6 + 5] = q * 4 + 3;
            }

            _mesh = new Mesh { name = "YarnTumbleGrid" };
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            _mesh.uv = _uvs;
            _mesh.triangles = triangles;
            _mesh.bounds = new Bounds(new Vector3(0f, -0.05f, 0f), new Vector3(WindowSize + 0.3f, WindowSize + 0.4f, 0.1f));
            _grid.sharedMesh = _mesh;
        }

        void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
        }

        void LateUpdate()
        {
            _quads = 0;
            if (_machine.Object == null || !_machine.Object.IsValid)
            {
                if (_restDrawn)
                    return;
                _restDrawn = true;
                DrawGrid(_idleGrid ??= YarnTumbleRules.Play(0).Final);
                DrawLamps(0, -1f);
                Upload();
                return;
            }

            var spin = _machine.Tumble;
            var cascades = spin.Cascades.Count;
            if (_lastSpin < 0)
            {
                _lastSpin = _machine.SpinId;
                _landed = true;
                _restDrawn = false;
                spin.Final.CopyTo(_resting, 0);
                spin.Final.CopyTo(_previous, 0);
            }

            if (_machine.SpinId != _lastSpin)
            {
                _lastSpin = _machine.SpinId;
                _landed = _restDrawn = false;
                _columnsLanded = _glowed = _batted = 0;
                _resting.CopyTo(_previous, 0);
                Sfx.PlayAt(SoundId.WheelSpin, transform.position, 0.8f);
                _celebration.OnSpinStarted();
            }

            var chain = YarnTumbleTiming.ChainAt(_machine.SpinElapsed, cascades, out var time);
            if (chain < 0)
            {
                DrawDropIn(spin.Start, time);
                DrawLamps(0, -1f);
            }
            else if (chain < cascades)
            {
                DrawChain(spin.Cascades[chain], spin.Before(chain), chain, time);
                DrawLamps(chain + 1, time < YarnTumbleTiming.Glow ? time : -1f);
            }
            else
            {
                if (_landed && _restDrawn)
                    return;
                _restDrawn = _landed;
                DrawGrid(spin.Final);
                DrawLamps(cascades, -1f);
                if (!_landed)
                {
                    _landed = _restDrawn = true;
                    spin.Final.CopyTo(_resting, 0);
                    _celebration.OnLanded();
                }
            }

            Upload();
        }

        void DrawGrid(int[] grid)
        {
            for (var column = 0; column < Size; column++)
            for (var row = 0; row < Size; row++)
                Quad(grid[YarnTumbleRules.Index(column, row)], column + 0.5f, row + 0.5f, 1f, 0f, true);
        }

        /// <summary>The last spin's symbols fall out the bottom, then the new ones drop in, column by column.</summary>
        void DrawDropIn(int[] grid, float time)
        {
            for (var column = 0; column < Size; column++)
            {
                var falling = Mathf.Clamp01((time - column * OutStagger) / OutTime);
                var inAt = InStart + column * InStagger;
                if (time >= inAt + InTime && _columnsLanded <= column)
                {
                    _columnsLanded = column + 1;
                    Sfx.PlayAt(SoundId.CardPlace, transform.position + Vector3.up, 0.6f);
                }

                for (var row = 0; row < Size; row++)
                {
                    var cell = YarnTumbleRules.Index(column, row);
                    if (falling < 1f)
                        Quad(_previous[cell], column + 0.5f, row + 0.5f - Size * falling * falling, 1f, 0f, true);
                    Quad(grid[cell], column + 0.5f, row + 0.5f + Landing(time - inAt, InTime, Size), 1f, 0f, true);
                }
            }
        }

        void DrawChain(YarnTumbleRules.Cascade cascade, int[] before, int chain, float time)
        {
            var front = transform.position + transform.forward * 0.3f + Vector3.up;
            if (_glowed <= chain)
            {
                _glowed = chain + 1;
                Sfx.PlayAt(SoundId.Notify, front, 0.7f);
            }

            var batAt = YarnTumbleTiming.Glow;
            if (time < batAt + YarnTumbleTiming.Bat)
            {
                if (time >= batAt && _batted <= chain)
                {
                    _batted = chain + 1;
                    _celebration.Swat();
                    Sfx.PlayAt(SoundId.ChipBet, front);
                }

                var pulse = 1f + 0.07f * Mathf.Sin(time * 22f);
                var flight = Mathf.Clamp01((time - batAt) / YarnTumbleTiming.Bat);
                for (var column = 0; column < Size; column++)
                for (var row = 0; row < Size; row++)
                {
                    var cell = YarnTumbleRules.Index(column, row);
                    var symbol = before[cell];
                    if (!cascade.Won[cell])
                    {
                        Quad(symbol, column + 0.5f, row + 0.5f, 1f, 0f, true);
                    }
                    else if (time < batAt)
                    {
                        Quad(YarnTumbleSymbols.GlowCell, column + 0.5f, row + 0.5f, 1.08f * pulse, -0.003f, true);
                        Quad(symbol, column + 0.5f, row + 0.5f, 0.92f * pulse, 0f, true);
                    }
                    else
                    {
                        // Swatted up and away to the player's left, shrinking as it goes.
                        Quad(symbol, column + 0.5f - flight * 0.9f, row + 0.5f + flight * 0.6f, 0.92f * (1f - flight * flight), 0.002f, true);
                    }
                }

                return;
            }

            var falling = time - batAt - YarnTumbleTiming.Bat;
            for (var column = 0; column < Size; column++)
            for (var row = 0; row < Size; row++)
            {
                var cell = YarnTumbleRules.Index(column, row);
                var drop = cascade.FromRow[cell] - row;
                var offset = drop > 0 ? Landing(falling, YarnTumbleTiming.Fall, drop) : 0f;
                Quad(cascade.After[cell], column + 0.5f, row + 0.5f + offset, 1f, 0f, true);
            }
        }

        /// <summary>The ×1 ×2 ×3... lamps under the grid: the first <paramref name="lit"/> are on, the newest pulsing while it glows.</summary>
        void DrawLamps(int lit, float glowTime)
        {
            const float spacing = (float)Size / Lamps;
            for (var lamp = 0; lamp < Lamps; lamp++)
            {
                var on = lamp < lit || (lamp == Lamps - 1 && lit > Lamps);
                var newest = lamp == Mathf.Min(lit, Lamps) - 1;
                var size = 0.68f * (newest && glowTime >= 0f ? 1f + 0.12f * Mathf.Sin(glowTime * 22f) : 1f);
                Quad(YarnTumbleSymbols.LampCell(lamp, on), (lamp + 0.5f) * spacing, -0.55f, size, 0f, false);
            }
        }

        /// <summary>
        /// How far above its place a symbol still is, <paramref name="time"/> into a fall of <paramref name="rows"/>
        /// rows lasting <paramref name="duration"/>: it speeds up as it falls, then bounces once.
        /// </summary>
        static float Landing(float time, float duration, float rows)
        {
            if (time <= 0f)
                return rows;
            if (time < duration)
            {
                var t = time / duration;
                return rows * (1f - t * t);
            }

            var bounce = (time - duration) / BounceTime;
            return bounce < 1f ? BounceHeight * Mathf.Sin(bounce * Mathf.PI) : 0f;
        }

        /// <summary>
        /// Adds a quad showing atlas <paramref name="cell"/>, centred at (x, y) in grid units (a cell is one unit,
        /// from the window's bottom left), <paramref name="size"/> units across, nudged towards the player by
        /// <paramref name="depth"/>, and when <paramref name="clip"/>, cut to the window.
        /// </summary>
        void Quad(int cell, float x, float y, float size, float depth, bool clip)
        {
            if (_quads >= MaxQuads || size <= 0f)
                return;

            var half = size / 2f;
            float x0 = x - half, x1 = x + half, y0 = y - half, y1 = y + half;
            var uv = YarnTumbleSymbols.Uv(cell);
            float u0 = uv.xMin, u1 = uv.xMax, v0 = uv.yMin, v1 = uv.yMax;
            if (clip && (!Clip(ref x0, ref x1, ref u0, ref u1) || !Clip(ref y0, ref y1, ref v0, ref v1)))
                return;

            var q = _quads++ * 4;
            _vertices[q] = Local(x0, y0, depth);
            _vertices[q + 1] = Local(x0, y1, depth);
            _vertices[q + 2] = Local(x1, y1, depth);
            _vertices[q + 3] = Local(x1, y0, depth);
            _uvs[q] = new Vector2(u0, v0);
            _uvs[q + 1] = new Vector2(u0, v1);
            _uvs[q + 2] = new Vector2(u1, v1);
            _uvs[q + 3] = new Vector2(u1, v0);
        }

        /// <summary>Cuts a span to the window (0 to Size), moving its texture coordinates along; false if nothing's left.</summary>
        static bool Clip(ref float a0, ref float a1, ref float t0, ref float t1)
        {
            var length = a1 - a0;
            var span = t1 - t0;
            if (a0 < 0f)
            {
                t0 += -a0 / length * span;
                a0 = 0f;
            }

            if (a1 > Size)
            {
                t1 -= (a1 - Size) / length * span;
                a1 = Size;
            }

            return a1 > a0;
        }

        /// <summary>
        /// Grid units to the grid's local space. The player faces the machine's front (its +Z), so their right is
        /// local -X.
        /// </summary>
        static Vector3 Local(float x, float y, float depth) =>
            new(-(x / Size - 0.5f) * WindowSize, (y / Size - 0.5f) * WindowSize, depth);

        void Upload()
        {
            // Collapse the quads left over from the last frame.
            for (var i = _quads * 4; i < _lastQuads * 4; i++)
                _vertices[i] = Vector3.zero;
            _lastQuads = _quads;
            _mesh.vertices = _vertices;
            _mesh.uv = _uvs;
        }
    }
}
