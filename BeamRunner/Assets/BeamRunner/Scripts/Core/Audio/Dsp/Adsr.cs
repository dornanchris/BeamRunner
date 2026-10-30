namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Per-sample ADSR envelope: linear attack, exponential decay toward the sustain level and
    /// exponential release. Decay/release times are the time to fall by 60 dB.
    /// Mutable struct — keep it in a local or field.
    /// </summary>
    public struct Adsr
    {
        private const int Idle = 0;
        private const int AttackStage = 1;
        private const int DecayStage = 2;
        private const int SustainStage = 3;
        private const int ReleaseStage = 4;

        private int _stage;
        private float _attackInc;
        private float _decayCoef;
        private float _sustain;
        private float _releaseCoef;

        /// <summary>Current envelope level.</summary>
        public float Level;

        public Adsr(float attack, float decay, float sustain, float release, float sampleRate)
        {
            _stage = Idle;
            Level = 0f;
            _attackInc = attack <= 0f ? 1f : 1f / (attack * sampleRate);
            _decayCoef = DspMath.DecayCoef(decay, sampleRate);
            _sustain = sustain;
            _releaseCoef = DspMath.DecayCoef(release, sampleRate);
        }

        /// <summary>True once the release has finished (or before the first gate).</summary>
        public bool IsIdle => _stage == Idle;

        public void Gate(bool on)
        {
            if (on)
            {
                _stage = AttackStage;
            }
            else if (_stage != Idle)
            {
                _stage = ReleaseStage;
            }
        }

        public float Next()
        {
            switch (_stage)
            {
                case AttackStage:
                    Level += _attackInc;
                    if (Level >= 1f)
                    {
                        Level = 1f;
                        _stage = DecayStage;
                    }

                    break;
                case DecayStage:
                    Level = _sustain + (Level - _sustain) * _decayCoef;
                    if (Level - _sustain < 1e-5f)
                    {
                        Level = _sustain;
                        _stage = SustainStage;
                    }

                    break;
                case ReleaseStage:
                    Level *= _releaseCoef;
                    if (Level < 1e-5f)
                    {
                        Level = 0f;
                        _stage = Idle;
                    }

                    break;
            }

            return Level;
        }
    }
}
