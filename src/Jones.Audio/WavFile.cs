namespace Jones.Audio;

/// <summary>
/// Wraps 16-bit mono PCM in a RIFF/WAVE header.
///
/// This exists so rendered sound can be written to a file and measured — length, peak,
/// silence — rather than asserted to work. The tests use it; so can anyone checking a
/// track by ear.
/// </summary>
public static class WavFile
{
    public static byte[] Build(ReadOnlySpan<short> samples, int sampleRate)
    {
        const int channels = 1;
        const int bitsPerSample = 16;
        var blockAlign = channels * bitsPerSample / 8;
        var byteRate = sampleRate * blockAlign;
        var dataBytes = samples.Length * 2;

        var buffer = new byte[44 + dataBytes];
        var w = new MemoryStream(buffer);
        using var bw = new BinaryWriter(w);

        bw.Write("RIFF"u8);
        bw.Write(36 + dataBytes);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);                       // PCM chunk size
        bw.Write((short)1);                 // format: PCM
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)blockAlign);
        bw.Write((short)bitsPerSample);
        bw.Write("data"u8);
        bw.Write(dataBytes);
        foreach (var s in samples) bw.Write(s);

        return buffer;
    }
}
