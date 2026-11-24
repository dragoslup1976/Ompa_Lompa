using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace OmpaLompa.Audio
{
    /// <summary>
    /// ONE character with several states, driven by the voice.
    /// By default the WALK clip plays. Each entry of 'Reactions' says: on this voice event,
    /// show this animation (optional) and do this movement. Add, remove or edit entries in the
    /// Inspector to create new states, no code needed.
    /// Hierarchy: this object is the character; each animation is a child Quad with a VideoPlayer.
    /// </summary>
    public class BambouCreature : MonoBehaviour
    {
        public enum Trigger
        {
            WhileBlowing,   // active as long as you blow (movement loops)
            ShoutHigh,      // high-pitched shout
            ShoutLow,       // low-pitched shout
            AnyShout        // any shout (immediate, no pitch needed)
        }

        public enum Move
        {
            None,
            Jump,    // up and down
            Slide,   // glides sideways and back
            Sway,    // swings left and right
            Shake,   // quick vibration
            Pulse,   // grows and shrinks
            Lift     // rises with the blow intensity and STAYS up while you keep blowing (use with "While Blowing")
        }

        [System.Serializable]
        public class Reaction
        {
            public string name = "New reaction";
            public Trigger trigger = Trigger.AnyShout;
            [Tooltip("Quad with the animation to show. Leave empty to keep the walk animation.")]
            public Renderer animation;
            public Move movement = Move.None;
            [Tooltip("Direction of the movement. (0,0,0) = default: up for Jump, right for the others.")]
            public Vector3 direction = Vector3.zero;
            [Tooltip("Shape of the movement over time (both axes 0..1). Rises then falls = goes and comes back. Used by Jump, Slide and Pulse.")]
            public AnimationCurve curve = DefaultBump();
            [Tooltip("Jump/Slide/Sway/Shake: distance, relative to the size of the character (1 = half its height; Shake: ~0.1). Pulse: extra scale (0.2 = +20 %).")]
            public float strength = 1f;
            [Tooltip("Shouts: how long the reaction lasts (s). While blowing: length of one movement cycle (s).")]
            [Min(0.1f)] public float duration = 1f;
            [Tooltip("Shouts: higher pitch = stronger movement. Blowing: harder blow = stronger movement.")]
            public bool scaleWithVoice = true;
            [Tooltip("Blowing only: the animation plays faster the harder you blow.")]
            public bool speedFollowsIntensity = true;

            public static AnimationCurve DefaultBump() => new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.5f, 1f, 0f, 0f), new Keyframe(1f, 0f, 0f, 0f));
        }

        [Header("Sources")]
        [SerializeField] VoiceEventDetector detector;
        [Tooltip("Default animation, shown whenever no reaction is active.")]
        [SerializeField] Renderer walkQuad;

        [Header("Settings")]
        [Tooltip("Pitch 0 (low voice) .. 1 (high voice). Shouts at or above it count as HIGH.")]
        [SerializeField, Range(0f, 1f)] float highPitchThreshold = 0.5f;
        public enum PitchFallback { High, Low, Nothing }
        [Tooltip("What to do when a shout is detected but its pitch could not be measured (noisy or breathy shout).")]
        [SerializeField] PitchFallback ifPitchUnknown = PitchFallback.High;
        [Tooltip("Seconds to wait for the pitch before using the fallback.")]
        [SerializeField, Min(0.1f)] float pitchWaitSeconds = 0.5f;
        [Tooltip("Seconds the blow reaction stays after the blow stops (avoids flicker).")]
        [SerializeField, Min(0f)] float returnDelay = 0.3f;
        [Tooltip("Playback speed at blow intensity 0 (x) and 1 (y), for reactions with 'Speed Follows Intensity'.")]
        [SerializeField] Vector2 blowSpeedRange = new Vector2(0.7f, 1.5f);
        [Tooltip("Lift: seconds to follow a stronger blow (smaller = rises faster).")]
        [SerializeField, Min(0.01f)] float liftRiseSeconds = 0.25f;
        [Tooltip("Lift: seconds to come back down when the blow gets weaker or stops (larger = floats down slowly).")]
        [SerializeField, Min(0.01f)] float liftFallSeconds = 0.6f;

        [Header("Auto walk (wanders left and right by itself)")]
        [SerializeField] bool autoWalk = true;
        [Tooltip("Constant walking speed, relative to the size of the character. Only used when 'Sync Steps To Animation' is OFF (otherwise see Walk Tempo and Step Length).")]
        [SerializeField, Min(0f)] float walkSpeed = 1f;
        [Tooltip("How far it goes to the left and to the right of its starting point (world units). Shown as a yellow line in the Scene view.")]
        [SerializeField, Min(0f)] float walkRange = 4f;
        [Tooltip("Seconds it waits at each end before turning around.")]
        [SerializeField, Min(0f)] float turnPause = 0.3f;
        [Tooltip("Also turns around at random moments, not only at the ends.")]
        [SerializeField] bool randomTurns = false;
        [SerializeField] Vector2 randomTurnInterval = new Vector2(3f, 8f);
        [Tooltip("ON if the walk animation shows the character facing RIGHT. OFF if it faces left. Used to flip it when it turns.")]
        [SerializeField] bool clipFacesRight = true;
        [Tooltip("ON: keeps walking while a reaction (jump, blow...) plays. OFF: stops during reactions.")]
        [SerializeField] bool walkWhileReacting = false;

        [Header("Blow freeze")]
        [Tooltip("Seconds the character stays completely still (legs stop too) after the blow ends.")]
        [SerializeField, Min(0f)] float freezeAfterBlowSeconds = 1f;

        [Header("Steps (the movement follows the legs)")]
        [Tooltip("ON: the character only moves as the walk animation moves, so the feet do not glide. 'Walk Tempo' sets the speed of the animation.")]
        [SerializeField] bool syncStepsToAnimation = true;
        [Tooltip("Tempo of the walk animation (1 = normal, 2 = twice as fast). The distance covered follows it. Used when 'Sync Steps' is ON.")]
        [SerializeField, Range(0.1f, 4f)] float walkTempo = 1f;
        [Tooltip("How many steps (foot plants) the walk clip contains in ONE loop. Change it until the feet stop gliding.")]
        [SerializeField, Range(1, 8)] int stepsPerLoop = 2;
        [Tooltip("Distance of one step, relative to the height of the character (0.5 = half its height).")]
        [SerializeField, Min(0f)] float stepLength = 0.5f;
        [Tooltip("Speed during ONE step (x = 0..1 of the step). High = the body moves forward, near 0 = the foot is planted.")]
        [SerializeField] AnimationCurve stepSpeed = DefaultStepCurve();
        [Tooltip("Shifts the moment of the movement inside the loop (0..1) to line it up with the legs.")]
        [SerializeField, Range(0f, 1f)] float stepPhaseOffset = 0f;
        [Tooltip("Pauses the walk animation whenever the character stands still (ends, after a blow, during reactions).")]
        [SerializeField] bool pauseLegsWhenStill = true;

        [Header("Video quality")]
        [Tooltip("Smooths the videos when they are shown smaller than their resolution (removes the pixelated look).")]
        [SerializeField] bool smoothVideo = true;
        [Tooltip("Largest side of the internal picture (pixels). Lower = lighter on the GPU.")]
        [SerializeField, Range(256, 4096)] int maxTextureSize = 1920;
        [Tooltip("Sharpness when the picture is seen at an angle (not needed for a flat quad).")]
        [SerializeField, Range(1, 16)] int anisoLevel = 4;

        [Header("Reactions")]
        [SerializeField] List<Reaction> reactions = new List<Reaction>();

        class VideoRelay { public Renderer renderer; public RenderTexture source, smooth; }
        readonly List<VideoRelay> relays = new List<VideoRelay>();

        Vector3 basePos, baseScale;
        float walkX, centerX, pauseUntil, nextTurnTime;
        int facing = 1; // 1 = moving right, -1 = moving left
        float liftCurrent;                    // current lift height (smoothed), kept even after the reaction ends
        Vector3 liftDir = Vector3.up;
        float sizeFactor = 1f; // movements are relative to the size of the character (2 units tall = 1)
        Reaction active;
        float activeStart, strengthScale = 1f;
        float lastBlowTime = -999f;
        float testBlowUntil;
        bool pitchPending;
        float shoutTime;
        float lastPhase = -1f;     // walk-clip phase of the previous frame (steps), -1 = unknown
        float stepCurveAvg = 1f;
        float stallTime;           // how long the walk clip time has not advanced while it should
        string walkStatus = "-";

        void Awake()
        {
            if (detector == null) detector = FindFirstObjectByType<VoiceEventDetector>();
            basePos = transform.position;
            baseScale = transform.localScale;
            walkX = centerX = basePos.x;
            ScheduleRandomTurn();
            ComputeStepAverage();
            // A bigger character moves proportionally more: Strength is relative to its height.
            sizeFactor = walkQuad != null ? Mathf.Max(0.01f, walkQuad.bounds.size.y / 2f) : 1f;
            if (smoothVideo) SetupSmoothVideo();
            SetVisual(null);
        }

        // The video is drawn into a texture, then copied each frame into a second texture that has mipmaps
        // (pre-shrunk versions). That is what keeps a 4K video clean when it is shown small.
        void SetupSmoothVideo()
        {
            foreach (var rend in GetComponentsInChildren<Renderer>(true))
            {
                var vp = rend.GetComponent<VideoPlayer>();
                if (vp == null || vp.clip == null) continue;

                int w = (int)vp.clip.width, h = (int)vp.clip.height;
                float k = Mathf.Min(1f, (float)maxTextureSize / Mathf.Max(w, h));
                w = Mathf.Max(16, Mathf.RoundToInt(w * k));
                h = Mathf.Max(16, Mathf.RoundToInt(h * k));

                var source = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = rend.name + "_src" };
                var smooth = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
                {
                    name = rend.name + "_smooth",
                    useMipMap = true,
                    autoGenerateMips = true,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = anisoLevel,
                    wrapMode = TextureWrapMode.Clamp
                };
                source.Create();
                smooth.Create();
                ClearWhite(source);
                ClearWhite(smooth); // white = transparent with the white-key shader (no black flash at the start)

                vp.renderMode = VideoRenderMode.RenderTexture;
                vp.targetTexture = source;
                rend.material.SetTexture("_MainTex", smooth); // material instance, the asset is untouched

                relays.Add(new VideoRelay { renderer = rend, source = source, smooth = smooth });
            }
        }

        static void ClearWhite(RenderTexture rt)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.white);
            RenderTexture.active = previous;
        }

        void LateUpdate()
        {
            foreach (var r in relays)
                if (r.renderer != null && r.renderer.enabled) Graphics.Blit(r.source, r.smooth);
        }

        void OnDestroy()
        {
            foreach (var r in relays)
            {
                if (r.source != null) r.source.Release();
                if (r.smooth != null) r.smooth.Release();
            }
            relays.Clear();
        }

        void OnEnable()
        {
            if (detector == null) detector = FindFirstObjectByType<VoiceEventDetector>();
            if (detector == null) return;
            detector.ShoutDetected += OnShout;
            detector.ShoutPitchMeasured += OnShoutPitch;
        }

        void OnDisable()
        {
            if (detector == null) return;
            detector.ShoutDetected -= OnShout;
            detector.ShoutPitchMeasured -= OnShoutPitch;
        }

        // ---- Voice events -------------------------------------------------------------

        void OnShout()
        {
            pitchPending = true;   // wait for the pitch; if it never comes, the fallback is used
            shoutTime = Time.time;
            Reaction r = Find(Trigger.AnyShout);
            if (r != null) Begin(r, 1f);
        }

        void OnShoutPitch(float pitchHz, float pitch01, float peakRms)
        {
            pitchPending = false;
            bool high = pitch01 >= highPitchThreshold;
            Reaction r = Find(high ? Trigger.ShoutHigh : Trigger.ShoutLow);
            if (r == null) return;
            // 0.8x at the threshold, up to 1.4x for the highest pitch (low shouts: 0.8x .. 1x).
            float t = high ? Mathf.InverseLerp(highPitchThreshold, 1f, pitch01)
                           : 1f - Mathf.InverseLerp(0f, highPitchThreshold, pitch01);
            Begin(r, r.scaleWithVoice ? Mathf.Lerp(0.8f, 1.4f, t) : 1f);
        }

        // ---- State machine ------------------------------------------------------------

        void Update()
        {
            if (pitchPending && Time.time - shoutTime > pitchWaitSeconds)
            {
                pitchPending = false;
                if (ifPitchUnknown != PitchFallback.Nothing)
                {
                    Reaction r = Find(ifPitchUnknown == PitchFallback.High ? Trigger.ShoutHigh : Trigger.ShoutLow);
                    if (r != null && (active == null || active.trigger != Trigger.WhileBlowing)) Begin(r, 1f);
                }
            }

            if (detector != null && detector.IsBlowing) lastBlowTime = Time.unscaledTime;
            bool blowHeld = Time.unscaledTime - lastBlowTime < returnDelay || Time.unscaledTime < testBlowUntil;
            Reaction blowReaction = Find(Trigger.WhileBlowing);

            if (blowReaction != null && blowHeld)
            {
                if (active != blowReaction) Begin(blowReaction, 1f); // blowing wins over shout reactions
            }
            else if (active != null && active.trigger == Trigger.WhileBlowing)
            {
                End();
            }

            if (active != null && active.trigger != Trigger.WhileBlowing &&
                Time.time - activeStart >= active.duration)
                End();

            UpdateWalk();
            ApplyMovement();
        }

        static AnimationCurve DefaultStepCurve() => new AnimationCurve(
            new Keyframe(0f, 0.15f, 0f, 0f), new Keyframe(0.5f, 1.85f, 0f, 0f), new Keyframe(1f, 0.15f, 0f, 0f));

        void ComputeStepAverage()
        {
            if (stepSpeed == null || stepSpeed.length == 0) { stepCurveAvg = 1f; return; }
            float sum = 0f; const int n = 64;
            for (int i = 0; i < n; i++) sum += Mathf.Max(0f, stepSpeed.Evaluate((i + 0.5f) / n));
            stepCurveAvg = Mathf.Max(0.01f, sum / n);
        }

        // Position in the walk loop, counted in steps (0..stepsPerLoop).
        bool TryStepPhase(out float phase)
        {
            phase = 0f;
            var vp = PlayerOf(walkQuad);
            if (vp == null || !vp.isPrepared || vp.length < 0.01) return false;
            phase = (float)(vp.time / vp.length) * stepsPerLoop;
            return true;
        }

        // Plays the walk animation only while the character really moves.
        void SetLegs(bool moving)
        {
            var vp = PlayerOf(walkQuad);
            if (vp == null) return;
            if (syncStepsToAnimation) vp.playbackSpeed = Mathf.Max(0.05f, walkTempo);
            if (!pauseLegsWhenStill) return;
            if (moving) { if (vp.isPaused) vp.Play(); }
            else if (vp.isPlaying) vp.Pause();
        }

        public string WalkStatus => walkStatus;

        void UpdateWalk()
        {
            if (!autoWalk) { walkStatus = "Auto walk OFF"; return; }

            bool blowFrozen = Time.unscaledTime < lastBlowTime + freezeAfterBlowSeconds
                              || Time.unscaledTime < testBlowUntil;
            bool canWalk = (active == null || walkWhileReacting) && !blowFrozen && Time.time >= pauseUntil;
            SetLegs(canWalk);
            if (!canWalk)
            {
                lastPhase = -1f; stallTime = 0f;
                walkStatus = active != null && !walkWhileReacting ? "Stopped: reaction '" + active.name + "'"
                           : blowFrozen ? "Stopped: freeze after blow"
                           : "Stopped: turning around";
                return;
            }

            float dx = 0f;
            bool synced = false;
            if (syncStepsToAnimation && TryStepPhase(out float phase))
            {
                float delta = 0f;
                if (lastPhase >= 0f)
                {
                    delta = phase - lastPhase;
                    if (delta < -stepsPerLoop * 0.5f) delta += stepsPerLoop; // the loop restarted
                    else if (delta < 0f) delta = 0f;
                }
                lastPhase = phase;

                // Safety: if the clip time does not move, do not stay stuck, walk at a constant speed.
                stallTime = delta > 0f ? 0f : stallTime + Time.deltaTime;
                if (stallTime < 0.3f)
                {
                    float u = Mathf.Repeat(phase + stepPhaseOffset * stepsPerLoop, 1f);
                    float speed = (stepSpeed != null && stepSpeed.length > 0) ? Mathf.Max(0f, stepSpeed.Evaluate(u)) / stepCurveAvg : 1f;
                    dx = delta * stepLength * 2f * sizeFactor * speed;
                    synced = true;
                }
            }
            if (!synced) dx = walkSpeed * 2f * sizeFactor * Time.deltaTime; // no sync: constant speed
            walkStatus = synced ? "Walking (steps follow the legs)" : "Walking (constant speed, clip time unavailable)";
            walkX += facing * dx;

            float minX = centerX - walkRange, maxX = centerX + walkRange;
            // Turn only when still heading towards that end: otherwise, standing exactly on the limit,
            // it would turn again every frame and stay stuck.
            if (walkX >= maxX && facing > 0) { walkX = maxX; Turn(-1); }
            else if (walkX <= minX && facing < 0) { walkX = minX; Turn(1); }
            else if (randomTurns && Time.time >= nextTurnTime) Turn(-facing);
        }

        void Turn(int newFacing)
        {
            facing = newFacing;
            pauseUntil = Time.time + turnPause;
            ScheduleRandomTurn();
        }

        void ScheduleRandomTurn() =>
            nextTurnTime = Time.time + Random.Range(randomTurnInterval.x, Mathf.Max(randomTurnInterval.x, randomTurnInterval.y));

        void OnDrawGizmosSelected()
        {
            float cx = Application.isPlaying ? centerX : transform.position.x;
            Vector3 p = transform.position;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(new Vector3(cx - walkRange, p.y, p.z), new Vector3(cx + walkRange, p.y, p.z));
            Gizmos.DrawWireCube(new Vector3(cx - walkRange, p.y, p.z), new Vector3(0.1f, 0.6f, 0.1f));
            Gizmos.DrawWireCube(new Vector3(cx + walkRange, p.y, p.z), new Vector3(0.1f, 0.6f, 0.1f));
        }

        void Begin(Reaction r, float scale)
        {
            if (active != null && active != r) EndVisual(active);
            active = r;
            activeStart = Time.time;
            strengthScale = scale;
            SetVisual(r.animation);
        }

        void End()
        {
            if (active != null) EndVisual(active);
            active = null;
            SetVisual(null);
        }

        void EndVisual(Reaction r)
        {
            var vp = PlayerOf(r.animation);
            if (vp != null) vp.playbackSpeed = 1f;
        }

        /// <summary>Shows 'anim' (null = the walk animation) and hides every other animation.</summary>
        void SetVisual(Renderer anim)
        {
            if (walkQuad != null) walkQuad.enabled = anim == null;
            foreach (var r in reactions)
            {
                if (r == null || r.animation == null || r.animation == walkQuad) continue;
                r.animation.enabled = r.animation == anim;
            }
            lastPhase = -1f;
            // Restart the shown animation from its first frame.
            var vp = PlayerOf(anim);
            if (vp != null && vp.isPrepared) vp.frame = 0;
        }

        // ---- Movement -----------------------------------------------------------------

        void ApplyMovement()
        {
            Vector3 pos = new Vector3(autoWalk ? walkX : basePos.x, basePos.y, basePos.z);
            Vector3 scale = baseScale;
            float liftTarget = 0f;

            if (active != null)
            {
                float elapsed = Time.time - activeStart;
                bool loop = active.trigger == Trigger.WhileBlowing;
                float dur = Mathf.Max(0.1f, active.duration);
                float u = loop ? Mathf.Repeat(elapsed, dur) / dur : Mathf.Clamp01(elapsed / dur);

                float intensity = Time.unscaledTime < testBlowUntil ? 1f
                                  : (detector != null ? detector.BlowIntensity : 1f);
                float k = active.strength * (loop ? (active.scaleWithVoice ? intensity : 1f) : strengthScale);

                if (loop && active.speedFollowsIntensity && detector != null)
                {
                    var vp = PlayerOf(active.animation);
                    if (vp != null) vp.playbackSpeed = Mathf.Lerp(blowSpeedRange.x, blowSpeedRange.y, intensity);
                }

                float shape = Shape(active, u);
                Vector3 dir = Direction(active);
                switch (active.movement)
                {
                    case Move.Jump:
                    case Move.Slide: pos += dir * (shape * k * sizeFactor); break;
                    case Move.Sway:  pos += dir * (Mathf.Sin(u * Mathf.PI * 2f) * k * sizeFactor); break;
                    case Move.Shake: pos += dir * (Mathf.Sin(elapsed * 40f) * k * sizeFactor * (loop ? 1f : 1f - u)); break;
                    case Move.Pulse: scale *= 1f + shape * k; break;
                    case Move.Lift:  liftTarget = k; liftDir = dir; break; // k already follows the blow intensity
                }
            }

            // Lift: follows the intensity, stays while you keep blowing, floats back down after.
            float tau = liftTarget > liftCurrent ? liftRiseSeconds : liftFallSeconds;
            liftCurrent += (liftTarget - liftCurrent) * (1f - Mathf.Exp(-Time.deltaTime / tau));
            if (liftTarget <= 0f && liftCurrent < 0.001f) liftCurrent = 0f;
            pos += liftDir * (liftCurrent * sizeFactor);

            // Flip so the character faces the direction it walks.
            if (autoWalk) scale.x *= facing * (clipFacesRight ? 1f : -1f);

            transform.position = pos;
            transform.localScale = scale;
        }

        // ---- Helpers ------------------------------------------------------------------

        Reaction Find(Trigger trigger)
        {
            foreach (var r in reactions)
                if (r != null && r.trigger == trigger) return r;
            return null;
        }

        static VideoPlayer PlayerOf(Renderer r) => r != null ? r.GetComponent<VideoPlayer>() : null;

        static float Shape(Reaction r, float u) =>
            (r.curve != null && r.curve.length > 0) ? r.curve.Evaluate(u) : Mathf.Sin(u * Mathf.PI);

        static Vector3 Direction(Reaction r)
        {
            if (r.direction.sqrMagnitude > 1e-6f) return r.direction.normalized;
            return (r.movement == Move.Jump || r.movement == Move.Lift) ? Vector3.up : Vector3.right;
        }

        // ---- Used by the custom Inspector (live state + test buttons) -------------------

        public VoiceEventDetector Detector => detector;
        public string ActiveReactionName => active != null ? active.name : "Walk (default)";
        public int ReactionCount => reactions.Count;
        public string ReactionName(int i) => reactions[i] != null ? reactions[i].name : "-";

        /// <summary>Plays reaction 'index' as if the voice had triggered it (Play mode only).</summary>
        public void TestReaction(int index)
        {
            if (!Application.isPlaying || index < 0 || index >= reactions.Count || reactions[index] == null) return;
            var r = reactions[index];
            if (r.trigger == Trigger.WhileBlowing) testBlowUntil = Time.unscaledTime + 2f; // 2 s of full blow
            else Begin(r, 1f);
        }

        // Test without a microphone: right click on the component title.
        [ContextMenu("Test high shout")] void TestHigh() => OnShoutPitch(600f, 0.9f, 0.3f);
        [ContextMenu("Test low shout")]  void TestLow()  => OnShoutPitch(150f, 0.1f, 0.3f);

#if UNITY_EDITOR
        static readonly string[] ClipPaths =
        {
            "Assets/ANIMATION BAMBOU/bambou_marche.mp4",
            "Assets/ANIMATION BAMBOU/bambou_branches.mp4",
            "Assets/ANIMATION BAMBOU/bambou blush final.mp4",
        };
        const float QuadHeight = 2f;
        const float FloorY = -0.5f;

        // Called when the component is added (or Reset in the Inspector): builds the whole character
        // from the three clips, as children of THIS object. Use it on an EMPTY GameObject.
        void Reset()
        {
            detector = FindFirstObjectByType<VoiceEventDetector>();

            if (GetComponent<Renderer>() != null || GetComponent<VideoPlayer>() != null)
            {
                Debug.LogWarning("[BambouCreature] Add this component to an EMPTY GameObject " +
                                 "(not to a Quad or a video object), then use Reset.");
                return;
            }

            // Remove what a previous Reset created.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child.name.StartsWith("Anim_")) Undo.DestroyObjectImmediate(child);
            }

            Undo.RecordObject(transform, "Place character");
            transform.position = new Vector3(0f, FloorY + QuadHeight * 0.5f, 0f);
            transform.localScale = Vector3.one;

            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
            var keyShader = Shader.Find("OmpaLompa/VideoWhiteKey");
            var shader = keyShader != null ? keyShader : Shader.Find("Sprites/Default");

            string[] names = { "Anim_Walk", "Anim_Blow", "Anim_Blush" };
            var rends = new Renderer[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(ClipPaths[i]);
                if (clip == null) { Debug.LogError("[BambouCreature] Clip not found: " + ClipPaths[i]); continue; }

                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Undo.RegisterCreatedObjectUndo(quad, "Create animation");
                quad.name = names[i];
                DestroyImmediate(quad.GetComponent<Collider>());
                quad.transform.SetParent(transform, false);
                quad.transform.localPosition = Vector3.zero;
                float width = clip.height > 0 ? QuadHeight * clip.width / clip.height : QuadHeight;
                quad.transform.localScale = new Vector3(width, QuadHeight, 1f);

                // Own material per animation (the video texture is written into it).
                string matPath = $"Assets/Materials/BambouVideo_{i + 1}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                else if (mat.shader != shader)
                {
                    mat.shader = shader;
                    EditorUtility.SetDirty(mat);
                }

                var renderer = quad.GetComponent<Renderer>();
                renderer.sharedMaterial = mat;
                renderer.enabled = i == 0; // only the walk animation is visible at rest

                var vp = quad.AddComponent<VideoPlayer>();
                vp.source = VideoSource.VideoClip;
                vp.clip = clip;
                vp.playOnAwake = true;
                vp.isLooping = true;
                vp.waitForFirstFrame = true;
                vp.audioOutputMode = VideoAudioOutputMode.None;
                vp.renderMode = VideoRenderMode.MaterialOverride;
                vp.targetMaterialRenderer = renderer;
                vp.targetMaterialProperty = "_MainTex";

                rends[i] = renderer;
            }
            AssetDatabase.SaveAssets();

            walkQuad = rends[0];
            reactions = new List<Reaction>
            {
                new Reaction { name = "Blow: branches", trigger = Trigger.WhileBlowing, animation = rends[1],
                               movement = Move.Lift, strength = 1.5f, duration = 1f,
                               scaleWithVoice = true, speedFollowsIntensity = true },
                new Reaction { name = "High shout: jump", trigger = Trigger.ShoutHigh, animation = null,
                               movement = Move.Jump, strength = 1.5f, duration = 0.6f, scaleWithVoice = true },
                new Reaction { name = "Low shout: blush", trigger = Trigger.ShoutLow, animation = rends[2],
                               movement = Move.Pulse, strength = 0.15f, duration = 1.5f, scaleWithVoice = false },
            };

            RefreshPreview();
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        void OnValidate()
        {
            ComputeStepAverage();
            // Keeps the preview picture up to date in the Scene view (the video itself only plays in Play mode).
            EditorApplication.delayCall += () => { if (this != null) RefreshPreview(); };
        }

        /// <summary>Gives Anim_Blow and Anim_Blush exactly the size and position of Anim_Walk.</summary>
        public void MatchSizesToWalk()
        {
            Transform walk = null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "Anim_Walk") { walk = t; break; }
            if (walk == null) { Debug.LogWarning("[BambouCreature] Anim_Walk not found."); return; }

            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t == walk || !t.name.StartsWith("Anim_")) continue;
                Undo.RecordObject(t, "Match animation size");
                t.localScale = walk.localScale;
                t.localPosition = walk.localPosition;
                t.localRotation = walk.localRotation;
            }
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        /// <summary>Shows a still frame of each animation in the Scene view while the game is not running.</summary>
        public void RefreshPreview()
        {
            if (Application.isPlaying) return;
            string[,] map =
            {
                { "Anim_Walk",  "Assets/ANIMATION BAMBOU/Preview/preview_walk.png" },
                { "Anim_Blow",  "Assets/ANIMATION BAMBOU/Preview/preview_blow.png" },
                { "Anim_Blush", "Assets/ANIMATION BAMBOU/Preview/preview_blush.png" },
            };
            foreach (var rend in GetComponentsInChildren<Renderer>(true))
            {
                for (int i = 0; i < map.GetLength(0); i++)
                {
                    if (rend.name != map[i, 0] || rend.sharedMaterial == null) continue;
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(map[i, 1]);
                    if (tex == null || rend.sharedMaterial.GetTexture("_MainTex") == tex) continue;
                    rend.sharedMaterial.SetTexture("_MainTex", tex);
                    EditorUtility.SetDirty(rend.sharedMaterial);
                }
            }
        }
#endif
    }
}
