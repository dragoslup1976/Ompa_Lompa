using OmpaLompa.Audio;
using UnityEditor;
using UnityEngine;

namespace OmpaLompa.EditorTools
{
    /// <summary>Inspector for BambouCreature: live state while playing + a test button per reaction.</summary>
    [CustomEditor(typeof(BambouCreature))]
    public class BambouCreatureEditor : Editor
    {
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            var c = (BambouCreature)target;

            if (Application.isPlaying)
            {
                var d = c.Detector;
                string live = $"State: {c.ActiveReactionName}\nWalk: {c.WalkStatus}";
                if (d != null)
                    live += $"\nBlow intensity: {d.BlowIntensity:F2}   Blowing: {d.IsBlowing}" +
                            $"\nPitch now: {d.CurrentPitchHz:F0} Hz   Last shout: {d.ShoutPitchHz:F0} Hz  (0..1: {d.ShoutPitchNormalized:F2})";
                var mic = Object.FindFirstObjectByType<OmpaLompa.Audio.MicrophoneInput>();
                if (mic != null && d != null)
                    live += $"\nMic level (Rms): {mic.Rms:F3}   low-frequency share: {mic.LowFreqRatio:F2}" +
                            $"\nNoise floor: {d.NoiseFloor:F3}   Blow threshold: {d.BlowThreshold:F3}";
                EditorGUILayout.HelpBox(live, MessageType.None);
            }

            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Test the reactions", EditorStyles.boldLabel);
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Press Play to use the test buttons.", MessageType.Info);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                for (int i = 0; i < c.ReactionCount; i++)
                    if (GUILayout.Button("Play: " + c.ReactionName(i))) c.TestReaction(i);
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Size", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Resize Anim_Walk, then press the button: the other animations get the same size and position.", MessageType.None);
                if (GUILayout.Button("Match all animations to Walk size")) c.MatchSizesToWalk();
                if (GUILayout.Button("Refresh Scene-view preview")) c.RefreshPreview();
            }
        }
    }
}
