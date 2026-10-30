namespace BeamRunner.Core.Audio
{
    /// <summary>One finished unit of the staged bank render: a whole music arrangement, one SFX or one loop.</summary>
    public sealed class AudioBankItem
    {
        private AudioBankItem(AudioBankItemKind kind, MusicArrangement music, SfxId sfx, LoopId loop, RenderedAudio audio, float progress)
        {
            Kind = kind;
            Music = music;
            Sfx = sfx;
            Loop = loop;
            Audio = audio;
            Progress = progress;
        }

        public AudioBankItemKind Kind { get; }

        /// <summary>The arrangement when <see cref="Kind"/> is Music, otherwise null.</summary>
        public MusicArrangement Music { get; }

        /// <summary>The effect id when <see cref="Kind"/> is Sfx.</summary>
        public SfxId Sfx { get; }

        /// <summary>The loop id when <see cref="Kind"/> is Loop.</summary>
        public LoopId Loop { get; }

        /// <summary>The rendered clip for Sfx/Loop items (null for Music; use <see cref="Music"/>.Stems).</summary>
        public RenderedAudio Audio { get; }

        /// <summary>Overall bank progress (0..1) once this item is done.</summary>
        public float Progress { get; }

        public static AudioBankItem ForMusic(MusicArrangement music, float progress)
        {
            return new AudioBankItem(AudioBankItemKind.Music, music, default, default, null, progress);
        }

        public static AudioBankItem ForSfx(SfxId id, RenderedAudio audio, float progress)
        {
            return new AudioBankItem(AudioBankItemKind.Sfx, null, id, default, audio, progress);
        }

        public static AudioBankItem ForLoop(LoopId id, RenderedAudio audio, float progress)
        {
            return new AudioBankItem(AudioBankItemKind.Loop, null, default, id, audio, progress);
        }
    }
}
