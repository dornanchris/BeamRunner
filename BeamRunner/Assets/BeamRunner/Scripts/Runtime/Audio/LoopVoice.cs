using UnityEngine;

namespace BeamRunner.Audio
{
    /// <summary>
    /// A continuously modulated ambience loop (engine hum, slipstream wind): smoothed volume/pitch/low-pass,
    /// started when it becomes audible and stopped when it fades out.
    /// </summary>
    internal sealed class LoopVoice
    {
        private readonly AudioSource _source;
        private readonly AudioLowPassFilter _filter;
        private float _volume;
        private float _pitch = 1f;
        private float _cutoff = 22000f;

        public LoopVoice(Transform root, string name, bool withFilter)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            _source = go.AddComponent<AudioSource>();
            AudioSourceSetup.Configure(_source, 32);
            _source.loop = true;
            _source.volume = 0f;
            if (withFilter)
            {
                _filter = go.AddComponent<AudioLowPassFilter>();
                _filter.cutoffFrequency = _cutoff;
                _filter.lowpassResonanceQ = 1f;
            }
        }

        public AudioClip Clip
        {
            get => _source.clip;
            set => _source.clip = value;
        }

        public float TargetVolume;
        public float TargetPitch = 1f;
        public float TargetCutoff = 22000f;
        public float AttackSeconds = 0.15f;
        public float ReleaseSeconds = 0.4f;

        public void Tick(float dt, float busGain)
        {
            if (_source.clip == null)
            {
                return;
            }

            float target = Mathf.Clamp01(TargetVolume);
            float time = target > _volume ? AttackSeconds : ReleaseSeconds;
            _volume = Mathf.MoveTowards(_volume, target, dt / Mathf.Max(0.01f, time));
            _pitch = Mathf.Lerp(_pitch, TargetPitch, 1f - Mathf.Exp(-dt * 6f));
            _cutoff = Mathf.Lerp(_cutoff, TargetCutoff, 1f - Mathf.Exp(-dt * 6f));

            float v = _volume * busGain;
            if (v > 0.0005f)
            {
                if (!_source.isPlaying)
                {
                    _source.Play();
                }

                _source.volume = v;
                _source.pitch = Mathf.Clamp(_pitch, 0.1f, 3f);
                if (_filter != null)
                {
                    _filter.cutoffFrequency = Mathf.Clamp(_cutoff, 10f, 22000f);
                }
            }
            else if (_source.isPlaying)
            {
                _source.Stop();
            }
        }

        public void Stop()
        {
            _volume = 0f;
            TargetVolume = 0f;
            _source.Stop();
        }

        /// <summary>Stops and drops the clip (the loop stays silent until a new <see cref="Clip"/> is set).</summary>
        public void ClearClip()
        {
            Stop();
            _source.clip = null;
        }
    }
}
