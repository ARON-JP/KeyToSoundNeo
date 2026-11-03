using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using NAudio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KeyToSoundNeo;

public sealed class AudioEngine : IDisposable
{
    private const int TargetSampleRate = 44100;
    private readonly ConcurrentDictionary<string, Lazy<Task<AudioClip>>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _lastPlayed = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _reportedErrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new();
    private readonly object _randomLock = new();
    private bool _disposed;

    public event Action<string, Exception>? PlaybackFailed;

    public void Warm(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            _ = GetClip(path).ContinueWith(
                task => ReportFailure(path, task.Exception?.GetBaseException() ?? new InvalidOperationException("Audio decoding failed.")),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
    }

    public Task PreloadAsync(string path) => GetClip(path);

    public void Play(IEnumerable<string> paths, SoundProfile profile, string rateKey)
    {
        if (_disposed)
            return;

        var candidates = paths.ToArray();
        var available = candidates.Where(File.Exists).ToArray();
        if (available.Length == 0)
        {
            if (candidates.Length > 0)
                ReportFailure(candidates[0], new FileNotFoundException("The assigned sound file could not be found.", candidates[0]));
            return;
        }

        string selected;
        double variation;
        lock (_randomLock)
        {
            selected = available[_random.Next(available.Length)];
            variation = (_random.NextDouble() * 2 - 1) * Math.Clamp(profile.PitchVariation, 0, 0.06);
        }

        var now = Environment.TickCount64;
        var hasPrevious = _lastPlayed.TryGetValue(rateKey, out var previous);
        _lastPlayed[rateKey] = now;
        var elapsed = hasPrevious ? Math.Max(30, now - previous) : long.MaxValue;
        var speedFactor = hasPrevious ? Math.Clamp(1.0 - elapsed / 600.0, 0.0, 1.0) : 0;
        var volume = Math.Clamp(profile.Volume * (1 - profile.SpeedVolume * speedFactor), 0.0, 1.0);
        _ = PlayAsync(selected, volume, 1 + variation);
    }

    private Task<AudioClip> GetClip(string path) =>
        _cache.GetOrAdd(path, p => new Lazy<Task<AudioClip>>(() => Task.Run(() => Decode(p)))).Value;

    private async Task PlayAsync(string path, double volume, double pitch)
    {
        try
        {
            var clip = await GetClip(path).ConfigureAwait(false);
            if (_disposed)
                return;
            var adjustedFormat = WaveFormat.CreateIeeeFloatWaveFormat(
                Math.Clamp((int)(clip.Format.SampleRate * pitch), 22050, 96000), clip.Format.Channels);
            var source = new RawSourceWaveStream(new MemoryStream(clip.Data, writable: false), adjustedFormat);
            var output = new WaveOutEvent { Volume = (float)volume, DesiredLatency = 60 };
            output.PlaybackStopped += (_, _) =>
            {
                output.Dispose();
                source.Dispose();
            };
            output.Init(source);
            output.Play();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or MmException or AggregateException)
        {
            System.Diagnostics.Debug.WriteLine($"Audio playback failed for '{path}': {ex.Message}");
            ReportFailure(path, ex);
        }
    }

    private void ReportFailure(string path, Exception exception)
    {
        if (_reportedErrors.TryAdd(path, 0))
            PlaybackFailed?.Invoke(path, exception);
    }

    private static AudioClip Decode(string path)
    {
        WaveStream reader;
        try
        {
            reader = new AudioFileReader(path);
        }
        catch (Exception first) when (first is InvalidOperationException or FormatException or ArgumentException)
        {
            try
            {
                reader = new MediaFoundationReader(path);
            }
            catch (Exception second) when (second is InvalidOperationException or FormatException or ArgumentException or COMException)
            {
                throw new InvalidOperationException($"Unsupported audio format: {path}", new AggregateException(first, second));
            }
        }

        using (reader)
        {
        var sampleProvider = reader is AudioFileReader audioFileReader
            ? audioFileReader
            : reader.ToSampleProvider();
        if (sampleProvider.WaveFormat.SampleRate != TargetSampleRate)
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, TargetSampleRate);

        var buffer = new float[8192];
        using var memory = new MemoryStream();
        var format = sampleProvider.WaveFormat;
        while (true)
        {
            var read = sampleProvider.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;
            var bytes = new byte[read * sizeof(float)];
            Buffer.BlockCopy(buffer, 0, bytes, 0, bytes.Length);
            memory.Write(bytes, 0, bytes.Length);
        }
        return new AudioClip(memory.ToArray(), format);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _cache.Clear();
    }

    private sealed record AudioClip(byte[] Data, WaveFormat Format);
}
