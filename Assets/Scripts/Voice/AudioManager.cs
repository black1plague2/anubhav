using System;
using UnityEngine;

/// <summary>
/// Captures microphone audio at 16 kHz mono and emits fixed 200ms PCM16
/// chunks via a circular buffer. HubClient (or any other consumer) drains
/// chunks with TryDequeueChunk(); if nothing drains fast enough the oldest
/// buffered chunk is silently dropped rather than growing unbounded.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public const int SampleRate = 16000;
    public const float ChunkDurationSeconds = 0.2f;
    public const int ChunkSampleCount = (int)(SampleRate * ChunkDurationSeconds); // 3200 samples

    [Tooltip("Empty = use the first available microphone device.")]
    [SerializeField] private string microphoneDevice;

    [Tooltip("How many 200ms chunks to hold before the oldest is dropped (~10s of backlog by default).")]
    [SerializeField] private int circularBufferChunks = 50;

    [Tooltip("Length in seconds of the internal looping Unity microphone clip. Must be longer than one poll interval's worth of audio.")]
    [SerializeField] private int micClipLengthSeconds = 10;

    public event Action<short[]> OnAudioChunkReady;

    private AudioClip _micClip;
    private string _activeDevice;
    private int _lastReadPosition;
    private float[] _floatReadBuffer;
    private CircularChunkQueue _chunkQueue;

    public bool IsRecording { get; private set; }
    public int QueuedChunkCount => _chunkQueue?.Count ?? 0;

    private void Awake()
    {
        _chunkQueue = new CircularChunkQueue(Mathf.Max(1, circularBufferChunks));
    }

    public bool StartRecording()
    {
        if (IsRecording)
        {
            return true;
        }

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("[AudioManager] No microphone devices found - cannot start recording.");
            return false;
        }

        _activeDevice = string.IsNullOrEmpty(microphoneDevice) ? Microphone.devices[0] : microphoneDevice;
        _micClip = Microphone.Start(_activeDevice, true, micClipLengthSeconds, SampleRate);
        if (_micClip == null)
        {
            Debug.LogError($"[AudioManager] Microphone.Start failed for device '{_activeDevice}'.");
            return false;
        }

        _lastReadPosition = 0;
        IsRecording = true;
        Debug.Log($"[AudioManager] Recording started on '{_activeDevice}' at {SampleRate}Hz, {ChunkDurationSeconds * 1000f:0}ms chunks.");
        return true;
    }

    public void StopRecording()
    {
        if (!IsRecording)
        {
            return;
        }

        Microphone.End(_activeDevice);
        IsRecording = false;
        Debug.Log("[AudioManager] Recording stopped.");
    }

    private void Update()
    {
        if (!IsRecording || _micClip == null)
        {
            return;
        }

        int micPosition = Microphone.GetPosition(_activeDevice);
        int available = micPosition - _lastReadPosition;
        if (available < 0)
        {
            available += _micClip.samples; // mic clip loop wrapped
        }

        while (available >= ChunkSampleCount)
        {
            short[] chunk = ReadChunk(_lastReadPosition);
            _chunkQueue.Enqueue(chunk);
            OnAudioChunkReady?.Invoke(chunk);

            _lastReadPosition = (_lastReadPosition + ChunkSampleCount) % _micClip.samples;
            available -= ChunkSampleCount;
        }
    }

    private short[] ReadChunk(int startSample)
    {
        if (_floatReadBuffer == null || _floatReadBuffer.Length != ChunkSampleCount)
        {
            _floatReadBuffer = new float[ChunkSampleCount];
        }

        // AudioClip.GetData wraps automatically when startSample + length
        // exceeds the (looping) clip's sample count.
        _micClip.GetData(_floatReadBuffer, startSample);

        short[] pcm16 = new short[ChunkSampleCount];
        for (int i = 0; i < ChunkSampleCount; i++)
        {
            float clamped = Mathf.Clamp(_floatReadBuffer[i], -1f, 1f);
            pcm16[i] = (short)Mathf.RoundToInt(clamped * short.MaxValue);
        }

        return pcm16;
    }

    public bool TryDequeueChunk(out short[] chunk)
    {
        return _chunkQueue.TryDequeue(out chunk);
    }

    /// <summary>Converts a PCM16 sample chunk to little-endian bytes for wire transport.</summary>
    public static byte[] ChunkToBytes(short[] chunk)
    {
        byte[] bytes = new byte[chunk.Length * 2];
        Buffer.BlockCopy(chunk, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private void OnDestroy()
    {
        StopRecording();
    }

    /// <summary>Fixed-capacity ring buffer of PCM16 chunks; oldest entries are overwritten once full.</summary>
    private sealed class CircularChunkQueue
    {
        private readonly short[][] _buffer;
        private int _head;
        private int _count;

        public CircularChunkQueue(int capacity)
        {
            _buffer = new short[capacity][];
        }

        public int Count => _count;

        public void Enqueue(short[] chunk)
        {
            int writeIndex = (_head + _count) % _buffer.Length;
            if (_count == _buffer.Length)
            {
                // Full: overwrite the oldest slot and advance head past it.
                _buffer[_head] = chunk;
                _head = (_head + 1) % _buffer.Length;
            }
            else
            {
                _buffer[writeIndex] = chunk;
                _count++;
            }
        }

        public bool TryDequeue(out short[] chunk)
        {
            if (_count == 0)
            {
                chunk = null;
                return false;
            }

            chunk = _buffer[_head];
            _buffer[_head] = null;
            _head = (_head + 1) % _buffer.Length;
            _count--;
            return true;
        }
    }
}
