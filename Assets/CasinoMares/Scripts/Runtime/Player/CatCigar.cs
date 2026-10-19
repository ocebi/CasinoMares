using CasinoMares.Core;
using UnityEngine;

namespace CasinoMares.Player
{
    /// <summary>
    /// The victory cigar (GDD §5.8): a cat that just had a huge win smokes one for five minutes, on every screen.
    /// The owner's cat spots its own huge wins here (any game's round or spin paying out <see cref="HugeWinPayout"/>
    /// or more, stake included, whatever the net: <see cref="ChipRewards.RoundPaid"/>) and has <see cref="PlayerCat.LightCigar"/> stamp the networked end tick; every client,
    /// late joiners included, lights and puts out the cigar from that tick (<see cref="PlayerCat.Render"/> calls
    /// <see cref="SetLit"/>). The cigar is three primitives and a smoke system built on the first light and parked at
    /// the corner of the mouth: they follow the skull bone in LateUpdate (the bone's own scale and axes are unknown,
    /// so nothing is parented to it). A four second breathing cycle swings the ember's glow and puffs the smoke.
    /// Nothing exists, updates or emits while no cigar is lit: the component stays disabled.
    /// </summary>
    [DefaultExecutionOrder(50)] // LateUpdate after CatRigAnchor has pinned the rig, so the cigar reads this frame's skull pose.
    public class CatCigar : MonoBehaviour
    {
        /// <summary>A round's or spin's gross payout (stake included) that earns a cigar, in every game.</summary>
        public const int HugeWinPayout = 500;

        // Dimensions in world units (the prefab's cat is about 0.58 m tall). Oversized on purpose (a real one would be a
        // dot at the mouth from the follow camera): long enough that the tip clears the snout.
        const float CigarLength = 0.12f;
        const float CigarRadius = 0.0064f;
        const float BandWidth = 0.013f;
        /// <summary>A little wider than the cigar so the glow reads from the default camera distance.</summary>
        const float EmberRadius = 0.0088f;
        /// <summary>A soft additive dot behind the ember: what makes it read on a phone, where the sphere is a pixel.</summary>
        const float EmberGlowSize = 0.072f;
        /// <summary>
        /// How far the cigar points down from the muzzle line, and outward from the cat's centre line: mostly forward,
        /// a little to the side, so it sticks out past the snout instead of lying along the cheek.
        /// </summary>
        const float TiltDownDegrees = 8f;
        const float SplayDegrees = 20f;
        /// <summary>Where the mouth corner is from the snout tip when the rig has no lip bone to read it from.</summary>
        const float MouthBack = 0.01f;
        const float MouthSide = 0.015f;
        const float MouthDown = 0.012f;

        // Breathing cycle (4 s): inhale (ember brightens), hold, exhale (smoke), rest.
        const float InhaleTime = 1.2f;
        const float HoldTime = 0.4f;
        const float ExhaleTime = 1.6f;
        const float RestTime = 0.8f;
        /// <summary>The ember's glow at the top of an inhale, over its resting glow.</summary>
        const float InhaleGlow = 3f;

        // Smoke: a puff at the start of each exhale, then a trickle until it ends. Big, dense and tall enough to read from
        // 3-4 m; the mobile caps stay where they were.
        const int MaxSmoke = 36;
        const int MaxSmokeMobile = 12;
        const int PuffMin = 9;
        const int PuffMax = 13;
        const int PuffMobile = 4;
        const float TrickleRate = 7f;
        const float TrickleRateMobile = 3f;
        const float SmokeLife = 3f;
        const float SmokeRise = 0.24f;
        const float SmokeStartSize = 0.03f;
        const float SmokeEndSize = 0.2f;

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        /// <summary>HDR, so the bloom picks it up; the breathing scales it.</summary>
        static readonly Color EmberEmission = new Color(1f, 0.32f, 0.06f) * 2f;
        static readonly Color EmberGlowColor = new Color(1f, 0.4f, 0.1f) * 0.6f;
        /// <summary>Grey through the dust material's warm additive tint (the shader multiplies the vertex colour in).</summary>
        static readonly Color SmokeColor = new Color(0.6f, 0.6f, 0.65f, 0.75f);

        enum Phase { Inhale, Hold, Exhale, Rest }

        [SerializeField] Material _bodyMaterial;
        [SerializeField] Material _bandMaterial;
        [SerializeField] Material _emberMaterial;
        [SerializeField] Material _smokeMaterial;

        PlayerCat _cat;
        bool _bound;
        bool _lit;
        Transform _skull;
        Vector3 _skullLocalPosition;
        Quaternion _skullLocalRotation;
        Transform _anchor;
        GameObject _stick;
        Renderer _ember;
        Transform _emberGlow;
        Renderer _emberGlowRenderer;
        MaterialPropertyBlock _emberBlock;
        MaterialPropertyBlock _glowBlock;
        ParticleSystem _smoke;
        Phase _phase;
        float _phaseStart;
        float _glow = 1f;

        public bool IsLit => _lit;

        void Awake()
        {
            enabled = false;
        }

        // ---------------------------------------------------------------- Owner: spotting huge wins

        /// <summary>The owner's cat listens for its own huge wins. Called from PlayerCat.Spawned on the local cat.</summary>
        public void Bind(PlayerCat cat)
        {
            _cat = cat;
            if (_bound)
                return;
            _bound = true;
            ChipRewards.RoundPaid += OnRoundPaid;
        }

        public void Unbind()
        {
            if (!_bound)
                return;
            _bound = false;
            ChipRewards.RoundPaid -= OnRoundPaid;
        }

        void OnDestroy() => Unbind();

        void OnRoundPaid(int payout)
        {
            if (payout >= HugeWinPayout)
                _cat?.LightCigar();
        }

        // ---------------------------------------------------------------- Everyone: the cigar

        /// <summary>Light or put out the cigar; PlayerCat calls this every frame from the networked end tick.</summary>
        public void SetLit(bool lit)
        {
            if (lit == _lit)
                return;

            if (lit)
            {
                if (_anchor == null && !Build())
                    return;
                _lit = true;
                _stick.SetActive(true);
                _emberGlow.gameObject.SetActive(true);
                _smoke.gameObject.SetActive(true);
                _smoke.Play();
                StartPhase(Phase.Rest);
                FollowSkull();
                enabled = true;
                return;
            }

            // Put out: hide the cigar, let the smoke already in the air drift off, then sleep (see LateUpdate).
            _lit = false;
            _stick.SetActive(false);
            _emberGlow.gameObject.SetActive(false);
            SetTrickle(0f);
            _smoke.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        void LateUpdate()
        {
            if (!_lit)
            {
                if (!_smoke.IsAlive(false))
                {
                    _smoke.gameObject.SetActive(false);
                    enabled = false;
                }

                return;
            }

            FollowSkull();
            UpdateBreathing();
        }

        void FollowSkull()
        {
            if (_skull == null)
                return;
            _anchor.SetPositionAndRotation(_skull.TransformPoint(_skullLocalPosition), _skull.rotation * _skullLocalRotation);

            // The glow is a quad: keep it square to the camera.
            var camera = Camera.main;
            if (camera != null)
                _emberGlow.rotation = Quaternion.LookRotation(_emberGlow.position - camera.transform.position);
        }

        void UpdateBreathing()
        {
            var t = Time.time - _phaseStart;
            float glow;
            switch (_phase)
            {
                case Phase.Inhale:
                    glow = Mathf.Lerp(1f, InhaleGlow, Mathf.SmoothStep(0f, 1f, t / InhaleTime));
                    if (t >= InhaleTime)
                        StartPhase(Phase.Hold);
                    break;
                case Phase.Hold:
                    glow = InhaleGlow;
                    if (t >= HoldTime)
                        StartPhase(Phase.Exhale);
                    break;
                case Phase.Exhale:
                    glow = Mathf.Lerp(InhaleGlow, 1f, Mathf.SmoothStep(0f, 1f, t / (ExhaleTime * 0.5f)));
                    if (t >= ExhaleTime)
                        StartPhase(Phase.Rest);
                    break;
                default:
                    glow = 1f;
                    if (t >= RestTime)
                        StartPhase(Phase.Inhale);
                    break;
            }

            SetGlow(glow);
        }

        void StartPhase(Phase phase)
        {
            _phase = phase;
            _phaseStart = Time.time;
            if (phase == Phase.Exhale)
            {
                var mobile = Platform.MobileQuality;
                _smoke.Emit(mobile ? PuffMobile : Random.Range(PuffMin, PuffMax + 1));
                SetTrickle(mobile ? TrickleRateMobile : TrickleRate);
            }
            else
            {
                SetTrickle(0f);
            }
        }

        void SetTrickle(float rate)
        {
            var emission = _smoke.emission;
            emission.rateOverTime = rate;
        }

        void SetGlow(float glow)
        {
            if (Mathf.Approximately(glow, _glow))
                return;
            _glow = glow;
            _ember.GetPropertyBlock(_emberBlock);
            _emberBlock.SetColor(EmissionColorId, EmberEmission * glow);
            _ember.SetPropertyBlock(_emberBlock);
            _emberGlowRenderer.GetPropertyBlock(_glowBlock);
            _glowBlock.SetColor(ColorId, EmberGlowColor * glow);
            _emberGlowRenderer.SetPropertyBlock(_glowBlock);
        }

        // ---------------------------------------------------------------- Building it, once

        /// <summary>
        /// Find the skull and work out where the mouth corner is in its space, then build the cigar and the smoke under
        /// an anchor on the cat root (unscaled, so the primitives are in world units). False if the rig has no skull.
        /// </summary>
        bool Build()
        {
            Transform snout = null;
            Transform lip = null;
            foreach (var bone in GetComponentsInChildren<Transform>(true))
            {
                var name = bone.name;
                if (_skull == null && (Has(name, "skull") || Has(name, "head")))
                    _skull = bone;
                else if (snout == null && Has(name, "snout"))
                    snout = bone;
                else if (lip == null && Has(name, "l_lipb"))
                    lip = bone;
            }

            if (_skull == null)
            {
                Debug.LogWarning($"{name}: no skull bone for the cigar.", this);
                return false;
            }

            // The builder sets this (the hall's DustMote material); a prefab from before it gets a plain one.
            if (_smokeMaterial == null)
                _smokeMaterial = new Material(Shader.Find("CasinoMares/SoftAdditive"));

            // The muzzle line, tilted down and splayed out to the cat's left.
            var up = transform.up;
            var forward = snout != null ? snout.position - _skull.position : transform.forward;
            forward = Vector3.ProjectOnPlane(forward, up);
            if (forward.sqrMagnitude < 1e-6f)
                forward = transform.forward;
            forward.Normalize();
            var right = Vector3.Cross(up, forward);
            var mouth = lip != null
                ? lip.position
                : (snout != null ? snout.position : _skull.position) - forward * MouthBack - right * MouthSide - up * MouthDown;
            var direction = Quaternion.AngleAxis(-SplayDegrees, up) * (Quaternion.AngleAxis(TiltDownDegrees, right) * forward);
            var rotation = Quaternion.LookRotation(direction, up);
            _skullLocalPosition = _skull.InverseTransformPoint(mouth);
            _skullLocalRotation = Quaternion.Inverse(_skull.rotation) * rotation;

            _anchor = new GameObject("Cigar").transform;
            _anchor.SetParent(transform, false);
            _anchor.SetPositionAndRotation(mouth, rotation);
            BuildStick();
            BuildSmoke();
            return true;
        }

        static bool Has(string name, string part) => name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Body, band and ember along the anchor's +Z (the cylinder primitive stands along Y, so it's turned).</summary>
        void BuildStick()
        {
            _stick = new GameObject("Stick");
            _stick.transform.SetParent(_anchor, false);
            var along = Quaternion.Euler(90f, 0f, 0f);

            var body = Primitive(PrimitiveType.Cylinder, "Body", _bodyMaterial ?? Fallback(ref _bodyMaterial, new Color(0.33f, 0.19f, 0.1f)));
            body.transform.SetLocalPositionAndRotation(new Vector3(0f, 0f, CigarLength * 0.5f), along);
            body.transform.localScale = new Vector3(CigarRadius * 2f, CigarLength * 0.5f, CigarRadius * 2f);

            var band = Primitive(PrimitiveType.Cylinder, "Band", _bandMaterial ?? Fallback(ref _bandMaterial, new Color(0.85f, 0.65f, 0.2f)));
            band.transform.SetLocalPositionAndRotation(new Vector3(0f, 0f, CigarLength * 0.3f), along);
            band.transform.localScale = new Vector3(CigarRadius * 2.3f, BandWidth * 0.5f, CigarRadius * 2.3f);

            var ember = Primitive(PrimitiveType.Sphere, "Ember", _emberMaterial ?? Fallback(ref _emberMaterial, new Color(0.9f, 0.3f, 0.08f), EmberEmission));
            ember.transform.localPosition = new Vector3(0f, 0f, CigarLength);
            ember.transform.localScale = Vector3.one * (EmberRadius * 2f);
            _ember = ember.GetComponent<Renderer>();
            _emberBlock = new MaterialPropertyBlock();

            // The glow sits under the anchor, not the stick, so it can face the camera on its own.
            var glow = Primitive(PrimitiveType.Quad, "EmberGlow", _smokeMaterial);
            glow.transform.SetParent(_anchor, false);
            glow.transform.localPosition = new Vector3(0f, 0f, CigarLength);
            glow.transform.localScale = Vector3.one * EmberGlowSize;
            _emberGlow = glow.transform;
            _emberGlowRenderer = glow.GetComponent<Renderer>();
            _glowBlock = new MaterialPropertyBlock();
            _glow = 0f;
            SetGlow(1f);
        }

        GameObject Primitive(PrimitiveType type, string name, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(_stick.transform, false);
            // A collider on the head would catch paw swipes and the character controller.
            Destroy(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        /// <summary>Only when the prefab predates the builder's cigar materials: a plain lit material, made once.</summary>
        static Material Fallback(ref Material slot, Color color, Color? emission = null)
        {
            slot = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            if (emission.HasValue)
            {
                slot.EnableKeyword("_EMISSION");
                slot.SetColor(EmissionColorId, emission.Value);
            }

            return slot;
        }

        /// <summary>
        /// World-space smoke from the tip: it rises on its own (velocity over lifetime, world up), so the emitter's
        /// orientation doesn't matter, and drifts with a little noise. Emission is driven by the breathing cycle.
        /// </summary>
        void BuildSmoke()
        {
            var go = new GameObject("Smoke");
            go.transform.SetParent(_anchor, false);
            go.transform.localPosition = new Vector3(0f, 0f, CigarLength);
            _smoke = go.AddComponent<ParticleSystem>();
            _smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _smoke.main;
            main.loop = true;
            main.playOnAwake = false;
            main.prewarm = false;
            main.startLifetime = SmokeLife;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.03f);
            main.startSize = SmokeEndSize;
            main.startColor = SmokeColor;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = Platform.MobileQuality ? MaxSmokeMobile : MaxSmoke;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;

            var emission = _smoke.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var shape = _smoke.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = CigarRadius;

            var velocity = _smoke.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = 0f;
            velocity.y = SmokeRise;
            velocity.z = 0f;

            var noise = _smoke.noise;
            noise.enabled = true;
            noise.strength = 0.03f;
            noise.frequency = 0.6f;
            noise.scrollSpeed = 0.2f;

            var size = _smoke.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, SmokeStartSize / SmokeEndSize, 1f, 1f));

            var fade = _smoke.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _smokeMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
