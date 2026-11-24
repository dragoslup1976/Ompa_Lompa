using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace OmpaLompa.Audio
{
    /// <summary>What the detector currently "hears". Used by the debug UI and by other scripts.</summary>
    public enum VoiceState { Quiet, Shout, Blow }

    /// <summary>
    /// Turns the raw numbers from <see cref="MicrophoneInput"/> into two game events:
    ///  - OnShout : loud and short sound that is NOT dominated by low frequencies.
    ///  - OnBlow  : sound above the blow threshold, dominated by low frequencies, lasting a fraction of a second.
    /// Thresholds are the max of a manual minimum and (background noise x multiplier), where the
    /// background noise is measured automatically for a couple of seconds at startup.
    /// Hook up the UnityEvents in the Inspector, or subscribe to the C# events from code.
    /// </summary>
    [DisallowMultipleComponent]
    public class VoiceEventDetector : MonoBehaviour
    {
        [SerializeField] MicrophoneInput microphone;

        [Header("Events")]
        public UnityEvent OnShout;
        public UnityEvent OnBlow;
        /// <summary>Raised when a sustained blow stops (useful to stop the wind).</summary>
        public UnityEvent OnBlowEnd;
        /// <summary>Fired once per shout, when its pitch is known. Argument: 0 (low voice) .. 1 (high voice).</summary>
        public UnityEvent<float> OnShoutPitch;

        /// <summary>C# equivalents of the UnityEvents.</summary>
        public event Action ShoutDetected;
        public event Action BlowDetected;
        public event Action BlowEnded;
        /// <summary>(pitchHz, pitchNormalized 0..1, peakRms) - once per shout.</summary>
        public event Action<float, float, float> ShoutPitchMeasured;

        [Header("Calibration")]
        [SerializeField] bool calibrateOnStart = true;
        [Tooltip("Seconds of background noise (music, nature sounds, room) measured at startup. Stay quiet!")]
        [SerializeField, Min(0.5f)] float calibrationSeconds = 2f;
        [Tooltip("Seconds ignored at the very beginning (mic start-up clicks).")]
        [SerializeField, Min(0f)] float calibrationSettleSeconds = 0.3f;

        [Header("Shout (loud + short)")]
        [Tooltip("Minimum RMS for a shout, even in a silent room.")]
        [SerializeField, Range(0.001f, 1f)] float shoutMinRms = 0.12f;
        [Tooltip("Shout threshold is also at least this many times the measured background noise.")]
        [SerializeField, Min(1f)] float shoutNoiseMultiplier = 4f;
        [Tooltip("The sound must stay above the threshold at least this long (rejects clicks and knocks).")]
        [SerializeField, Min(0.01f)] float shoutMinDuration = 0.06f;
        [Tooltip("Longer than this and it is no longer a 'short' shout (only used when Fire On Onset is off).")]
        [SerializeField, Min(0.1f)] float shoutMaxDuration = 0.5f;
        [Tooltip("ON: fire as soon as Min Duration has passed (lowest latency, a long scream still counts). " +
                 "OFF: fire when the sound ends, only if it lasted between Min and Max Duration.")]
        [SerializeField] bool fireShoutOnOnset = true;
        [Tooltip("A shout ends when the level drops below threshold x this ratio (hysteresis).")]
        [SerializeField, Range(0.3f, 1f)] float shoutReleaseRatio = 0.7f;

        [Tooltip("A loud sound is NOT a shout if more than this share of its power is in the low band (that is a blow).")]
        [SerializeField, Range(0.3f, 1f)] float shoutMaxLowRatio = 0.85f;

        [Header("Shout pitch (voice height)")]
        [Tooltip("Pitch (Hz) that maps to 0 (lowest voice).")]
        [SerializeField, Min(50f)] float shoutPitchLowHz = 120f;
        [Tooltip("Pitch (Hz) that maps to 1 (highest voice).")]
        [SerializeField, Min(100f)] float shoutPitchHighHz = 700f;
        [Tooltip("Seconds measured from the start of a shout before the pitch is reported (the attack is noisy). " +
                 "Smaller = reacts faster, less stable.")]
        [SerializeField, Min(0.05f)] float shoutPitchMaxWait = 0.25f;
        [Tooltip("Minimum number of frames with a clear pitch needed to report it.")]
        [SerializeField, Range(2, 20)] int shoutPitchMinFrames = 3;

        [Header("Blow (continuous + low frequencies)")]
        [Tooltip("Minimum RMS for a blow, even in a silent room.")]
        [SerializeField, Range(0.001f, 1f)] float blowMinRms = 0.04f;
        [Tooltip("Blow threshold is also at least this many times the measured background noise.")]
        [SerializeField, Min(1f)] float blowNoiseMultiplier = 2.5f;
        [Tooltip("Share of the power under the low-band limit needed to call it a blow. " +
                 "Voice is typically 0.2-0.6, a blow 0.8+.")]
        [SerializeField, Range(0f, 1f)] float lowFreqRatioThreshold = 0.7f;
        [Tooltip("How long the blow must last before it is detected (small = reacts almost instantly).")]
        [SerializeField, Min(0.02f)] float blowStartDelay = 0.15f;
        [Tooltip("A blow may dip below the conditions for this long without being cancelled.")]
        [SerializeField, Min(0f)] float blowDropoutGrace = 0.2f;
        [Tooltip("RMS at which BlowIntensity reaches 1 (full-strength blow).")]
        [SerializeField, Range(0.02f, 1f)] float blowFullScaleRms = 0.25f;
        [Tooltip("Seconds for BlowIntensity to follow an increase (smaller = more reactive).")]
        [SerializeField, Min(0.01f)] float blowIntensityAttack = 0.08f;
        [Tooltip("Seconds for BlowIntensity to follow a decrease (larger = the intensity is 'held' longer).")]
        [SerializeField, Min(0.01f)] float blowIntensityRelease = 0.35f;

        [Header("Cooldown")]
        [Tooltip("Minimum seconds between two events (of any kind).")]
        [SerializeField, Min(0f)] float cooldownSeconds = 0.75f;

        [Header("Creature sounds (avoid false triggers)")]
        [Tooltip("Sources playing project sounds (creatures, effects). While one plays, thresholds are raised.")]
        [SerializeField] AudioSource[] suppressWhilePlaying;
        [Tooltip("Thresholds are multiplied by this while a creature sound is playing.")]
        [SerializeField, Min(1f)] float playbackThresholdMultiplier = 2f;
        [Tooltip("Keep the raised thresholds this long after the sound ends (room echo).")]
        [SerializeField, Min(0f)] float playbackTailSeconds = 0.2f;

        [Header("Display")]
        [Tooltip("How long the state stays on SCREAM after a shout (for UI / debugging).")]
        [SerializeField, Min(0f)] float shoutDisplaySeconds = 0.6f;

        // ---- Public read-only data -------------------------------------------------------

        public VoiceState State { get; private set; } = VoiceState.Quiet;
        public bool IsCalibrating { get; private set; }
        public float CalibrationProgress { get; private set; }
        /// <summary>Background noise RMS measured during calibration.</summary>
        public float NoiseFloor { get; private set; }
        public bool IsBlowing { get; private set; }
        /// <summary>Blow strength 0..1, smoothed. Stays above 0 only while a blow is detected and sustained.</summary>
        public float BlowIntensity { get; private set; }
        /// <summary>How long the current blow has lasted (seconds, 0 when not blowing).</summary>
        public float BlowSeconds => IsBlowing ? Time.unscaledTime - blowStart : 0f;
        /// <summary>Pitch of the latest frame in Hz (0 = none). Live value, for debugging.</summary>
        public float CurrentPitchHz { get; private set; }
        /// <summary>Pitch of the last shout in Hz.</summary>
        public float ShoutPitchHz { get; private set; }
        /// <summary>Pitch of the last shout, 0 (low) .. 1 (high), on a musical (logarithmic) scale.</summary>
        public float ShoutPitchNormalized { get; private set; }
        /// <summary>Loudness peak (RMS) of the last shout.</summary>
        public float ShoutPeakRms { get; private set; }
        public bool IsSuppressed => suppressed;
        public MicrophoneInput MicInput => microphone;

        /// <summary>Current effective thresholds (include noise floor and playback suppression).</summary>
        public float ShoutThreshold { get; private set; }
        public float BlowThreshold { get; private set; }
        public float LowFreqRatioThreshold => lowFreqRatioThreshold;

        // ---- Internal state --------------------------------------------------------------

        // Calibration accumulators.
        bool calibStarted;
        float calibStartTime;
        double calibSum, calibSumSq;
        int calibCount;

        // Shout segment tracking.
        bool shoutActive;
        bool shoutHandled;
        float shoutStart, shoutLastLoud;
        float shoutLowSum;
        int shoutSamples;
        bool shoutFired, shoutPitchDone;
        float shoutPeak;
        readonly List<float> shoutPitches = new List<float>(64);

        // Blow tracking.
        float smoothLowRatio;
        bool blowCandidate;
        float blowStart, blowLastOk;
        float voicedUntil;   // a clear pitch (voice) was heard recently: a voice is never the start of a blow

        float lastEventTime = -999f;
        float stateHoldUntil;
        float suppressUntil;
        bool suppressed;
        bool wasCapturing;

        void Reset() => microphone = GetComponent<MicrophoneInput>();

        void Awake()
        {
            if (microphone == null) microphone = GetComponent<MicrophoneInput>();
            if (microphone == null) microphone = FindFirstObjectByType<MicrophoneInput>();
            NoiseFloor = 0f;
            RefreshThresholds();
        }

        void Update()
        {
            if (microphone == null || !microphone.IsCapturing)
            {
                wasCapturing = false;
                return;
            }

            // Start calibration the first time the mic delivers data.
            if (!wasCapturing)
            {
                wasCapturing = true;
                if (calibrateOnStart && !calibStarted) Recalibrate();
            }

            Process(microphone.Rms, microphone.LowFreqRatio, Time.unscaledDeltaTime, Time.unscaledTime,
                    microphone.Pitch);
        }

        // ---- Public API ------------------------------------------------------------------

        /// <summary>Re-measures the background noise. Keep the room "normal" (music on) and quiet for the duration.</summary>
        public void Recalibrate()
        {
            IsCalibrating = true;
            calibStarted = false; // start time is latched on the next Process call
            calibSum = calibSumSq = 0;
            calibCount = 0;
            CalibrationProgress = 0f;
            CancelActivity();
        }

        /// <summary>
        /// Call when a project sound (creature voice, effect) starts and is not covered by
        /// 'Suppress While Playing', to raise the thresholds for 'seconds'.
        /// </summary>
        public void SuppressFor(float seconds)
        {
            suppressUntil = Mathf.Max(suppressUntil, Time.unscaledTime + seconds);
        }

        /// <summary>
        /// Feeds one measurement into the state machine. Normally called from Update;
        /// public so the logic can be driven with synthetic values (tests, simulations).
        /// </summary>
        public void Process(float rms, float lowRatio, float dt, float now, float pitchHz = 0f)
        {
            CurrentPitchHz = pitchHz;
            if (pitchHz > 0f) voicedUntil = now + 0.25f;
            if (IsCalibrating)
            {
                UpdateCalibration(rms, now);
                return;
            }

            UpdateSuppression(now);
            RefreshThresholds();

            // Smooth the ratio so single odd frames don't flip the blow/shout decision.
            float alpha = 1f - Mathf.Exp(-dt / 0.08f);
            smoothLowRatio += (lowRatio - smoothLowRatio) * alpha;

            UpdateBlow(rms, now, dt);
            UpdateShout(rms, lowRatio, pitchHz, now);
            UpdateState(now);
        }

        // ---- Calibration -----------------------------------------------------------------

        void UpdateCalibration(float rms, float now)
        {
            if (!calibStarted)
            {
                calibStarted = true;
                calibStartTime = now;
            }

            float elapsed = now - calibStartTime;
            CalibrationProgress = Mathf.Clamp01(elapsed / (calibrationSettleSeconds + calibrationSeconds));

            if (elapsed >= calibrationSettleSeconds)
            {
                calibSum += rms;
                calibSumSq += (double)rms * rms;
                calibCount++;
            }

            if (elapsed >= calibrationSettleSeconds + calibrationSeconds)
            {
                // mean + 2 sigma covers music dynamics without chasing single peaks.
                if (calibCount > 0)
                {
                    double mean = calibSum / calibCount;
                    double var = Math.Max(0, calibSumSq / calibCount - mean * mean);
                    NoiseFloor = (float)(mean + 2.0 * Math.Sqrt(var));
                }
                IsCalibrating = false;
                CalibrationProgress = 1f;
                RefreshThresholds();
                Debug.Log($"[VoiceEventDetector] Calibrated: noise floor {NoiseFloor:F4} -> " +
                          $"shout threshold {ShoutThreshold:F3}, blow threshold {BlowThreshold:F3}");
            }
        }

        void UpdateSuppression(float now)
        {
            bool playing = false;
            if (suppressWhilePlaying != null)
            {
                foreach (var src in suppressWhilePlaying)
                    if (src != null && src.isPlaying) { playing = true; break; }
            }
            if (playing) suppressUntil = Mathf.Max(suppressUntil, now + playbackTailSeconds);
            suppressed = now < suppressUntil;
        }

        void RefreshThresholds()
        {
            float boost = suppressed ? playbackThresholdMultiplier : 1f;
            ShoutThreshold = Mathf.Max(shoutMinRms, NoiseFloor * shoutNoiseMultiplier) * boost;
            BlowThreshold = Mathf.Max(blowMinRms, NoiseFloor * blowNoiseMultiplier) * boost;
        }

        // ---- Blow ------------------------------------------------------------------------

        void UpdateBlow(float rms, float now, float dt)
        {
            bool blowLike = rms >= BlowThreshold && smoothLowRatio >= lowFreqRatioThreshold;
            if (!IsBlowing && now < voicedUntil) blowLike = false; // voice, not wind

            if (blowLike)
            {
                blowLastOk = now;
                if (!blowCandidate)
                {
                    blowCandidate = true;
                    blowStart = now;
                }
            }
            else if (blowCandidate && now - blowLastOk > blowDropoutGrace)
            {
                blowCandidate = false;
                if (IsBlowing) EndBlow();
            }

            if (blowCandidate && !IsBlowing && now - blowStart >= blowStartDelay && CooldownReady(now))
            {
                IsBlowing = true;
                lastEventTime = now;
                OnBlow?.Invoke();
                BlowDetected?.Invoke();
            }

            // Intensity 0..1: follows the loudness while the blow lasts, holds through short dropouts.
            float target = 0f;
            if (IsBlowing)
            {
                target = blowLike
                    ? Mathf.InverseLerp(BlowThreshold, Mathf.Max(blowFullScaleRms, BlowThreshold * 1.5f), rms)
                    : BlowIntensity;
            }
            float tau = target > BlowIntensity ? blowIntensityAttack : blowIntensityRelease;
            BlowIntensity += (target - BlowIntensity) * (1f - Mathf.Exp(-dt / tau));
            if (!IsBlowing && BlowIntensity < 0.001f) BlowIntensity = 0f;
        }

        void EndBlow()
        {
            IsBlowing = false;
            OnBlowEnd?.Invoke();
            BlowEnded?.Invoke();
        }

        // ---- Shout -----------------------------------------------------------------------

        void UpdateShout(float rms, float lowRatio, float pitchHz, float now)
        {
            if (!shoutActive)
            {
                if (rms >= ShoutThreshold)
                {
                    shoutActive = true;
                    shoutHandled = false;
                    shoutStart = shoutLastLoud = now;
                    shoutLowSum = 0f;
                    shoutSamples = 0;
                    shoutFired = false;
                    shoutPitchDone = false;
                    shoutPeak = rms;
                    shoutPitches.Clear();
                }
                else return;
            }

            shoutLowSum += lowRatio;
            shoutSamples++;
            shoutPeak = Mathf.Max(shoutPeak, rms);
            if (pitchHz > 0f && rms >= ShoutThreshold * shoutReleaseRatio) shoutPitches.Add(pitchHz);
            if (rms >= ShoutThreshold * shoutReleaseRatio) shoutLastLoud = now;

            float duration = shoutLastLoud - shoutStart;
            bool ended = now - shoutLastLoud > 0.08f; // short grace so one quiet frame doesn't split a shout
            float meanLow = shoutLowSum / Mathf.Max(1, shoutSamples);
            bool voiceLike = meanLow < shoutMaxLowRatio && !IsBlowing;

            if (!shoutHandled)
            {
                if (fireShoutOnOnset)
                {
                    if (duration >= shoutMinDuration)
                    {
                        shoutHandled = true;
                        if (voiceLike) TryFireShout(now);
                    }
                }
                else if (ended)
                {
                    shoutHandled = true;
                    if (voiceLike && duration >= shoutMinDuration && duration <= shoutMaxDuration)
                        TryFireShout(now);
                }
            }

            // Report the pitch once the attack has passed (or when the shout ends).
            if (shoutFired && !shoutPitchDone)
            {
                bool waited = now - shoutStart >= shoutPitchMaxWait;
                if (shoutPitches.Count >= shoutPitchMinFrames && (waited || ended)) FireShoutPitch();
                else if (ended) shoutPitchDone = true; // not enough clear pitch: no pitch event
            }

            if (ended) shoutActive = false;
        }

        void FireShoutPitch()
        {
            shoutPitchDone = true;
            shoutPitches.Sort();
            float hz = shoutPitches[shoutPitches.Count / 2]; // median: robust against octave glitches
            float lo = Mathf.Log(shoutPitchLowHz);
            float hi = Mathf.Log(Mathf.Max(shoutPitchHighHz, shoutPitchLowHz * 1.1f));
            ShoutPitchHz = hz;
            ShoutPitchNormalized = Mathf.InverseLerp(lo, hi, Mathf.Log(hz));
            ShoutPeakRms = shoutPeak;
            OnShoutPitch?.Invoke(ShoutPitchNormalized);
            ShoutPitchMeasured?.Invoke(ShoutPitchHz, ShoutPitchNormalized, ShoutPeakRms);
        }

        void TryFireShout(float now)
        {
            if (!CooldownReady(now)) return;
            lastEventTime = now;
            shoutFired = true;
            stateHoldUntil = now + shoutDisplaySeconds;
            State = VoiceState.Shout;
            OnShout?.Invoke();
            ShoutDetected?.Invoke();
        }

        // ---- Helpers ---------------------------------------------------------------------

        bool CooldownReady(float now) => now - lastEventTime >= cooldownSeconds;

        void UpdateState(float now)
        {
            if (IsBlowing) State = VoiceState.Blow;
            else if (State == VoiceState.Blow || (State == VoiceState.Shout && now >= stateHoldUntil))
                State = VoiceState.Quiet;
        }

        void CancelActivity()
        {
            shoutActive = false;
            shoutFired = false;
            shoutPitches.Clear();
            blowCandidate = false;
            if (IsBlowing) EndBlow();
            BlowIntensity = 0f;
            State = VoiceState.Quiet;
        }
    }
}
