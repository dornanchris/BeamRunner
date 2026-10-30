using System.Collections.Generic;

namespace BeamRunner.Core.Audio
{
    /// <summary>Everything the bank renders, indexed by id. Filled by <see cref="AudioBank.RenderAll"/>.</summary>
    public sealed class AudioBankData
    {
        private readonly Dictionary<MusicMode, MusicArrangement> _music = new Dictionary<MusicMode, MusicArrangement>();
        private readonly RenderedAudio[] _sfx = new RenderedAudio[AudioBank.SfxCount];
        private readonly RenderedAudio[] _loops = new RenderedAudio[AudioBank.LoopCount];

        public AudioBankData(int sampleRate)
        {
            SampleRate = sampleRate;
        }

        public int SampleRate { get; }

        public void Add(AudioBankItem item)
        {
            switch (item.Kind)
            {
                case AudioBankItemKind.Music:
                    _music[item.Music.Mode] = item.Music;
                    break;
                case AudioBankItemKind.Sfx:
                    _sfx[(int)item.Sfx] = item.Audio;
                    break;
                case AudioBankItemKind.Loop:
                    _loops[(int)item.Loop] = item.Audio;
                    break;
            }
        }

        /// <summary>The arrangement for a mode, or null (Silent / not rendered).</summary>
        public MusicArrangement GetMusic(MusicMode mode)
        {
            return _music.TryGetValue(mode, out MusicArrangement m) ? m : null;
        }

        public RenderedAudio GetSfx(SfxId id)
        {
            int i = (int)id;
            return i >= 0 && i < _sfx.Length ? _sfx[i] : null;
        }

        public RenderedAudio GetLoop(LoopId id)
        {
            int i = (int)id;
            return i >= 0 && i < _loops.Length ? _loops[i] : null;
        }
    }
}
