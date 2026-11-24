using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sporeborn.Runtime
{
    public sealed class WorkshopSounds : IDisposable
    {
        private const int SampleRate = 22050;
        private readonly AudioSource _source;
        private readonly AudioClip[] _clips;

        public enum Cue
        {
            Portion,
            Cut,
            Pack,
            Printer,
            Start,
            Finished,
            Sale
        }

        public WorkshopSounds(AudioSource source)
        {
            _source = source;
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.volume = 0.65f;
            _clips = new[]
            {
                Create(Cue.Portion, 0.10f, 210f, 150f, 0.62f),
                Create(Cue.Cut, 0.13f, 530f, 240f, 0.75f),
                Create(Cue.Pack, 0.10f, 160f, 95f, 0.35f),
                Create(Cue.Printer, 0.22f, 380f, 540f, 0.32f),
                Create(Cue.Start, 0.15f, 240f, 410f, 0.12f),
                Create(Cue.Finished, 0.34f, 660f, 880f, 0.04f),
                Create(Cue.Sale, 0.30f, 490f, 1050f, 0.06f)
            };
        }

        public void Play(Cue cue)
        {
            _source.PlayOneShot(_clips[(int)cue]);
        }

        public void Dispose()
        {
            if (_source != null)
            {
                _source.Stop();
            }

            foreach (AudioClip clip in _clips)
            {
                Object.Destroy(clip);
            }
        }

        private static AudioClip Create(Cue cue, float seconds, float startHz, float endHz, float noiseMix)
        {
            var samples = new float[Mathf.RoundToInt(seconds * SampleRate)];
            var random = new System.Random((int)cue);
            float phase = 0f;
            float noise = 0f;
            for (int index = 0; index < samples.Length; index++)
            {
                float progress = index / (samples.Length - 1f);
                phase += 2f * Mathf.PI * Mathf.Lerp(startHz, endHz, progress) / SampleRate;
                noise = Mathf.Lerp(noise, (float)random.NextDouble() * 2f - 1f, 0.37f);
                float attack = Mathf.Min(1f, index / (SampleRate * 0.006f));
                float fade = (1f - progress) * (1f - progress);
                samples[index] = 0.36f * attack * fade * Mathf.Lerp(Mathf.Sin(phase), noise, noiseMix);
            }

            AudioClip clip = AudioClip.Create(cue.ToString(), samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
