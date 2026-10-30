using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BeamRunner.Core.Audio;
using UnityEngine;

namespace BeamRunner.Audio
{
    /// <summary>A music arrangement whose stems have been uploaded into AudioClips (no PCM arrays retained).</summary>
    internal sealed class LoadedArrangement
    {
        public LoadedArrangement(MusicArrangement source, AudioClip[] clips)
        {
            Mode = source.Mode;
            SampleRate = source.SampleRate;
            LoopFrames = source.LoopFrames;
            BeatsPerLoop = source.BeatsPerLoop;
            BeatsPerBar = source.BeatsPerBar;
            Clips = clips;
            Stems = new MusicStem[source.StemCount];
            for (int i = 0; i < Stems.Length; i++)
            {
                Stems[i] = source.StemIds[i];
            }
        }

        public MusicMode Mode { get; }

        public int SampleRate { get; }

        public int LoopFrames { get; }

        public int BeatsPerLoop { get; }

        public int BeatsPerBar { get; }

        public AudioClip[] Clips { get; }

        public MusicStem[] Stems { get; }

        public double LoopSeconds => (double)LoopFrames / SampleRate;

        public double SecondsPerBeat => LoopSeconds / BeatsPerLoop;
    }

    /// <summary>
    /// Renders the procedural bank on a worker thread (<see cref="Task.Run(Action)"/>) and turns the results into
    /// AudioClips on the main thread, a few chunks per frame within a time budget so boot never hitches. Clip
    /// creation must happen on the main thread; rendering must not. The worker pauses when too much rendered PCM
    /// is waiting, which bounds peak managed memory. On WebGL (no threads) items render on the main thread,
    /// one per frame.
    /// </summary>
    internal sealed class AudioLoader
    {
        private const int ChunkFrames = 32768;
        private const long MaxPendingSamples = 12000000;

        private readonly int _sampleRate;
        private readonly bool _parallel;
        private readonly ConcurrentQueue<AudioBankItem> _queue = new ConcurrentQueue<AudioBankItem>();
        private readonly List<AudioClip> _stemClips = new List<AudioClip>(8);
        private readonly List<AudioClip> _allClips = new List<AudioClip>(64);
        private readonly Stopwatch _budget = new Stopwatch();
        private readonly Action<LoadedArrangement> _onMusic;
        private readonly Action<SfxId, AudioClip> _onSfx;
        private readonly Action<LoopId, AudioClip> _onLoop;

        private Task _task;
        private IEnumerator<AudioBankItem> _mainThreadRender;
        private long _pendingSamples;
        private volatile bool _cancel;
        private volatile bool _workerDone;
        private volatile float _renderProgress;
        private volatile string _error;

        private AudioBankItem _current;
        private int _stemIndex;
        private RenderedAudio _currentAudio;
        private AudioClip _currentClip;
        private int _offsetFrames;
        private float[] _chunk;

        /// <param name="parallel">Render on every core (boot). False keeps rendering on the single worker thread, e.g. for
        /// a reload during gameplay, so the main thread keeps its core.</param>
        public AudioLoader(int sampleRate, bool parallel, Action<LoadedArrangement> onMusic, Action<SfxId, AudioClip> onSfx, Action<LoopId, AudioClip> onLoop)
        {
            _sampleRate = sampleRate;
            _parallel = parallel;
            _onMusic = onMusic;
            _onSfx = onSfx;
            _onLoop = onLoop;
        }

        /// <summary>Render progress of the worker (0..1).</summary>
        public float RenderProgress => _renderProgress;

        /// <summary>Cumulative bank progress of everything uploaded into clips so far (0..1).</summary>
        public float UploadedProgress { get; private set; }

        /// <summary>True when every item has been rendered and uploaded (or loading failed).</summary>
        public bool IsDone { get; private set; }

        /// <summary>Non-null if rendering threw; loading then stops with whatever was uploaded.</summary>
        public string Error => _error;

        public void Start()
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                _mainThreadRender = AudioBank.RenderStaged(_sampleRate, false).GetEnumerator();
                return;
            }

            _task = Task.Run((Action)Worker);
        }

        /// <summary>
        /// Stops the worker at its next item boundary and drops queued PCM so it can be collected. The loader is
        /// finished afterwards (<see cref="IsDone"/>); create a new one to load again.
        /// </summary>
        public void Cancel()
        {
            _cancel = true;
            _mainThreadRender = null;
            while (_queue.TryDequeue(out _))
            {
            }

            _current = null;
            _currentAudio = null;
            _currentClip = null;
            IsDone = true;
        }

        /// <summary>Destroys every clip this loader created.</summary>
        public void DestroyClips()
        {
            for (int i = 0; i < _allClips.Count; i++)
            {
                if (_allClips[i] != null)
                {
                    UnityEngine.Object.Destroy(_allClips[i]);
                }
            }

            _allClips.Clear();
        }

        private void Worker()
        {
            try
            {
                foreach (AudioBankItem item in AudioBank.RenderStaged(_sampleRate, _parallel))
                {
                    if (_cancel)
                    {
                        // Returning disposes the iterator; its locals were nulled, so the PCM becomes garbage.
                        return;
                    }

                    Interlocked.Add(ref _pendingSamples, CountSamples(item));
                    _queue.Enqueue(item);
                    _renderProgress = item.Progress;

                    // Back-pressure: don't race far ahead of the main-thread upload.
                    while (!_cancel && Interlocked.Read(ref _pendingSamples) > MaxPendingSamples)
                    {
                        Thread.Sleep(4);
                    }

                    if (_cancel)
                    {
                        // Check before MoveNext: the next item may take a second or more to render.
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                _error = e.GetType().Name + ": " + e.Message;
            }
            finally
            {
                _workerDone = true;
            }
        }

        private static long CountSamples(AudioBankItem item)
        {
            if (item.Kind != AudioBankItemKind.Music)
            {
                return item.Audio != null ? item.Audio.Samples.Length : 0;
            }

            long n = 0;
            for (int i = 0; i < item.Music.StemCount; i++)
            {
                n += item.Music.Stems[i].Samples.Length;
            }

            return n;
        }

        /// <summary>Main thread: uploads rendered audio into clips for at most <paramref name="budgetMs"/>.</summary>
        public void Pump(double budgetMs)
        {
            if (IsDone)
            {
                return;
            }

            try
            {
                if (_mainThreadRender != null && _queue.IsEmpty && _currentAudio == null)
                {
                    RenderOneOnMainThread();
                }

                _budget.Reset();
                _budget.Start();
                while (_budget.Elapsed.TotalMilliseconds < budgetMs)
                {
                    if (_currentAudio == null)
                    {
                        if (!BeginNextAudio())
                        {
                            break;
                        }

                        if (_currentAudio == null)
                        {
                            continue;
                        }
                    }

                    UploadChunk();
                }

                _budget.Stop();
                if (_workerDone && _queue.IsEmpty && _current == null && _currentAudio == null)
                {
                    IsDone = true;
                }
            }
            catch (Exception e)
            {
                // AudioClip.Create/SetData or a loaded-callback threw on the main thread. Without this the half-set
                // state (_currentAudio set, _currentClip null) would throw a NullReferenceException every frame from
                // AudioManager.Update — skipping music/loop ticking — and IsReady would never turn true. Stop loading
                // and keep whatever was uploaded (graceful degradation).
                _budget.Stop();
                _error = e.GetType().Name + ": " + e.Message;
                Cancel();
            }
        }

        private void RenderOneOnMainThread()
        {
            try
            {
                if (_mainThreadRender.MoveNext())
                {
                    AudioBankItem item = _mainThreadRender.Current;
                    Interlocked.Add(ref _pendingSamples, CountSamples(item));
                    _queue.Enqueue(item);
                    _renderProgress = item.Progress;
                }
                else
                {
                    _mainThreadRender.Dispose();
                    _mainThreadRender = null;
                    _workerDone = true;
                }
            }
            catch (Exception e)
            {
                _error = e.GetType().Name + ": " + e.Message;
                _mainThreadRender = null;
                _workerDone = true;
            }
        }

        private bool BeginNextAudio()
        {
            if (_current == null)
            {
                if (!_queue.TryDequeue(out _current))
                {
                    return false;
                }

                _stemIndex = 0;
                _stemClips.Clear();
            }

            RenderedAudio a = _current.Kind == AudioBankItemKind.Music ? _current.Music.Stems[_stemIndex] : _current.Audio;
            if (a == null || a.FrameCount <= 0)
            {
                // Nothing to upload (should not happen); skip the item.
                _current = null;
                return true;
            }

            _currentAudio = a;
            _offsetFrames = 0;
            _currentClip = AudioClip.Create(a.Name, a.FrameCount, a.Channels, a.SampleRate, false);
            _allClips.Add(_currentClip);
            return true;
        }

        private void UploadChunk()
        {
            RenderedAudio a = _currentAudio;
            int ch = a.Channels;
            if (_offsetFrames == 0 && a.FrameCount <= ChunkFrames)
            {
                _currentClip.SetData(a.Samples, 0);
                _offsetFrames = a.FrameCount;
            }
            else
            {
                int n = Math.Min(ChunkFrames, a.FrameCount - _offsetFrames);
                float[] buf;
                if (n == ChunkFrames)
                {
                    if (_chunk == null || _chunk.Length != ChunkFrames * ch)
                    {
                        _chunk = new float[ChunkFrames * ch];
                    }

                    buf = _chunk;
                }
                else
                {
                    // The last partial chunk needs an exact-size array (SetData writes the whole array and would wrap).
                    buf = new float[n * ch];
                }

                Array.Copy(a.Samples, (long)_offsetFrames * ch, buf, 0, (long)n * ch);
                _currentClip.SetData(buf, _offsetFrames);
                _offsetFrames += n;
            }

            if (_offsetFrames >= a.FrameCount)
            {
                FinishAudio();
            }
        }

        private void FinishAudio()
        {
            Interlocked.Add(ref _pendingSamples, -_currentAudio.Samples.Length);
            AudioClip clip = _currentClip;
            _currentAudio = null;
            _currentClip = null;
            AudioBankItem item = _current;
            switch (item.Kind)
            {
                case AudioBankItemKind.Music:
                    _stemClips.Add(clip);
                    _stemIndex++;
                    if (_stemIndex >= item.Music.StemCount)
                    {
                        var loaded = new LoadedArrangement(item.Music, _stemClips.ToArray());
                        _current = null;
                        UploadedProgress = item.Progress;
                        _onMusic?.Invoke(loaded);
                    }

                    break;
                case AudioBankItemKind.Sfx:
                    _current = null;
                    UploadedProgress = item.Progress;
                    _onSfx?.Invoke(item.Sfx, clip);
                    break;
                default:
                    _current = null;
                    UploadedProgress = item.Progress;
                    _onLoop?.Invoke(item.Loop, clip);
                    break;
            }
        }
    }
}
