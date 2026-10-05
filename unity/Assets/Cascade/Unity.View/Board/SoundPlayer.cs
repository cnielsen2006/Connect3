using System;
using Cascade.Config;
using Cascade.Presentation;
using UnityEngine;

namespace Cascade.Unity.View
{
    /// <summary>
    /// Plays <see cref="SoundCue"/>s (TRD 12.4). The placeholder clips are synthesized at startup (short enveloped tones and noise), so
    /// there are no audio assets to import. Nothing plays while the player's sound setting is off; the master volume, the cascade pitch
    /// rise and the per-cue cooldown are the <c>[audio]</c> dials, read live.
    /// </summary>
    public sealed class SoundPlayer : MonoBehaviour
    {
        const int SampleRate = 22050;
        const int Voices = 8;

        AudioSource[] voices = Array.Empty<AudioSource>();
        AudioClip?[] clips = Array.Empty<AudioClip?>();
        float[] lastPlayed = Array.Empty<float>();
        float[] gain = Array.Empty<float>();
        int next;
        uint noiseState = 12345u;
        DialRegistry? dials;
        Func<bool> enabled = () => true;

        /// <summary>Cues that were started (not muted, not in cooldown). For tests and tools.</summary>
        public int Played { get; private set; }

        /// <param name="dialRegistry">Source of the <c>[audio]</c> dials.</param>
        /// <param name="soundEnabled">The player's sound setting, asked at every cue.</param>
        public void Init(DialRegistry dialRegistry, Func<bool> soundEnabled)
        {
            dials = dialRegistry;
            enabled = soundEnabled;
            // Unity plays nothing without an AudioListener, and the Boot scene has none.
            if (FindFirstObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            int n = Enum.GetValues(typeof(SoundCue)).Length;
            clips = new AudioClip?[n];
            lastPlayed = new float[n];
            gain = new float[n];
            for (int i = 0; i < n; i++) { lastPlayed[i] = float.NegativeInfinity; gain[i] = 1f; }
            Build();
            voices = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                voices[i] = s;
            }
        }

        /// <summary>Starts a cue. <paramref name="depth"/> raises the pitch of <see cref="SoundCue.Clear"/>.</summary>
        public void Play(SoundCue cue, int depth = 0)
        {
            if (dials == null || !enabled()) return;
            int i = (int)cue;
            var clip = clips[i];
            if (clip == null) return;
            float now = Time.unscaledTime;
            if (now - lastPlayed[i] < dials.Audio.CueCooldownMs / 1000f) return;
            lastPlayed[i] = now;
            var v = voices[next];
            next = (next + 1) % voices.Length;
            float semis = cue == SoundCue.Clear ? (float)(dials.Audio.CascadePitchSemitones * Mathf.Min(depth, 12))
                : cue == SoundCue.Stinger ? 2f * Mathf.Max(0, Mathf.Min(depth, 6) - 1) // depth is the celebration tier
                : 0f;
            v.pitch = Mathf.Pow(2f, semis / 12f);
            v.clip = clip;
            v.volume = Mathf.Clamp01((float)dials.Audio.MasterVolume * gain[i]);
            v.Play();
            Played++;
        }

        // ---- synthesis -----------------------------------------------------------------------------------

        void Build()
        {
            Add(SoundCue.Swap, 0.09f, 0.55f, (t, u) => Sine(300 - 100 * u, t) * Decay(u, 5));
            Add(SoundCue.Reject, 0.16f, 0.5f, (t, u) => Sq(150 + 12 * Mathf.Sin(t * 60), t) * 0.5f * Decay(u, 3));
            Add(SoundCue.Clear, 0.24f, 0.8f, (t, u) => (Sine(660, t) + 0.4f * Sine(1320, t) + 0.15f * Sine(1980, t)) * 0.6f * Decay(u, 6));
            Add(SoundCue.Land, 0.09f, 0.35f, (t, u) => Sine(120 - 50 * u, t) * Decay(u, 6));
            Add(SoundCue.SpecialCreate, 0.28f, 0.7f, (t, u) => Sine(380 + 520 * u * u, t) * Mathf.Sin(Mathf.PI * u) * 0.9f);
            Add(SoundCue.LineFire, 0.32f, 0.7f, (t, u) => (Noise() * 0.5f + Sine(500 + 900 * u, t) * 0.25f) * Mathf.Sin(Mathf.PI * u));
            Add(SoundCue.BombFire, 0.4f, 0.9f, (t, u) => (Sine(90 - 55 * u, t) * 0.9f + Noise() * 0.35f * Decay(u, 9)) * Decay(u, 3.5f));
            Add(SoundCue.ColorFire, 0.45f, 0.6f, (t, u) => Sine(500 + 1100 * u, t) * (0.6f + 0.4f * Mathf.Sin(t * 40)) * Mathf.Sin(Mathf.PI * u));
            Add(SoundCue.Shuffle, 0.36f, 0.6f, (t, u) => Noise() * 0.5f * Mathf.Max(0f, Mathf.Sin(u * Mathf.PI * 6)) * (1 - u));
            Add(SoundCue.Win, 0.7f, 0.8f, (t, u) => Arp(t, new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.16f, 0.7f));
            Add(SoundCue.Stinger, 0.38f, 0.75f, (t, u) => Arp(t, new[] { 659.25f, 830.61f, 987.77f, 1318.5f }, 0.07f, 0.35f) + 0.25f * Sine(1975f, t) * Decay(u, 7));
            Add(SoundCue.Lose, 0.55f, 0.7f, (t, u) => Arp(t, new[] { 392f, 311.13f }, 0.25f, 0.55f));
        }

        void Add(SoundCue cue, float seconds, float volume, Func<float, float, float> sample)
        {
            int n = Mathf.Max(1, (int)(seconds * SampleRate));
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate, u = i / (float)n;
                float fade = Mathf.Min(1f, i / 80f) * Mathf.Min(1f, (n - i) / 200f); // no clicks at the ends
                data[i] = Mathf.Clamp(sample(t, u) * fade, -1f, 1f);
            }
            var clip = AudioClip.Create("cue-" + cue, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            clips[(int)cue] = clip;
            gain[(int)cue] = volume;
        }

        static float Sine(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);
        static float Sq(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t) >= 0 ? 1f : -1f;
        static float Decay(float u, float k) => Mathf.Exp(-k * u);

        /// <summary>Deterministic noise (a small LCG): the same clips every run.</summary>
        float Noise()
        {
            noiseState = noiseState * 1664525u + 1013904223u;
            return ((noiseState >> 9) / (float)(1 << 23)) * 2f - 1f;
        }

        static float Arp(float t, float[] notes, float step, float len)
        {
            int k = Mathf.Min((int)(t / step), notes.Length - 1);
            float local = t - k * step;
            return Sine(notes[k], t) * Mathf.Exp(-3f * local / len) * 0.6f;
        }
    }
}
