using NukedOPL3Sharp;

namespace Jones.Audio;

/// <summary>
/// SCI1's AdLib sound driver: turns the MIDI in a sound resource into OPL2 register
/// writes, using the instruments from <see cref="AdLibBank"/>.
///
/// This is a port of ScummVM's <c>engines/sci/sound/drivers/adlib.cpp</c> (the SCI1 code
/// path, <c>_isSCI0 == false</c>), which is the reverse-engineered behaviour of Sierra's
/// own ADL.DRV. Nothing here is invented — the register layout, the frequency table, the
/// two velocity tables and the voice-allocation rules all come from that driver, because
/// the whole point is that these timbres are the game's and not a guess.
///
/// DELIBERATELY NOT PORTED, and why:
/// - Stereo. ScummVM optionally drives a pair of OPL2 chips for left/right and scales the
///   two by MIDI pan. A real AdLib card is one mono OPL2, which is what the game was
///   written for, so pan is accepted and ignored here.
/// - The rhythm key map and <c>findVoiceLateSci11</c>. Both require a 5382-byte patch
///   bank; this game ships the 1344-byte one, so channel 9 is an ordinary melodic channel
///   and the late-SCI11 voice search is unreachable.
/// - <c>_voiceQueue</c>/<c>queueMoveToBack</c>, which exist only to feed that same
///   unreachable search.
/// </summary>
public sealed class SciAdLibDriver
{
    public const int Voices = 9;
    public const int MidiChannels = 16;

    /// <summary>
    /// The OPL2's own rate, 14.318182 MHz / 288. The emulator always runs its chip model
    /// at this rate internally; <see cref="SampleRate"/> is what it resamples to.
    /// </summary>
    public const int OplNativeRate = 49716;

    /// <summary>
    /// The rate the emulator is asked to produce, and therefore the rate the sequencer
    /// counts ticks in and a sound device has to accept.
    ///
    /// It is NOT the chip's native 49716 Hz, and that is deliberate: asked for 49716 Hz,
    /// this machine's waveOut device consumed buffers at 83% of real time, which plays the
    /// music a sixth too slow and a couple of semitones flat. 48 kHz is a rate every
    /// device handles, and Nuked's own resampler bridges the two, so the chip still runs
    /// at 49716 Hz where it matters — only the output is converted.
    /// </summary>
    public const int SampleRate = 48000;

    /// <summary>Base register offset of each channel's operator pair.</summary>
    private static readonly byte[] RegisterOffset =
        [0x00, 0x01, 0x02, 0x08, 0x09, 0x0A, 0x10, 0x11, 0x12];

    /// <summary>
    /// One octave of OPL F-numbers, four entries per semitone so the pitch wheel can step
    /// between them.
    /// </summary>
    private static readonly int[] AdlibFreq =
    [
        0x157, 0x15c, 0x161, 0x166, 0x16b, 0x171, 0x176, 0x17b,
        0x181, 0x186, 0x18c, 0x192, 0x198, 0x19e, 0x1a4, 0x1aa,
        0x1b0, 0x1b6, 0x1bd, 0x1c3, 0x1ca, 0x1d0, 0x1d7, 0x1de,
        0x1e5, 0x1ec, 0x1f3, 0x1fa, 0x202, 0x209, 0x211, 0x218,
        0x220, 0x228, 0x230, 0x238, 0x241, 0x249, 0x252, 0x25a,
        0x263, 0x26c, 0x275, 0x27e, 0x287, 0x290, 0x29a, 0x2a4,
    ];

    private static readonly byte[] VelocityMap1 =
    [
        0x00, 0x0c, 0x0d, 0x0e, 0x0f, 0x11, 0x12, 0x13,
        0x14, 0x16, 0x17, 0x18, 0x1a, 0x1b, 0x1c, 0x1d,
        0x1f, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26,
        0x27, 0x28, 0x29, 0x2a, 0x2b, 0x2d, 0x2d, 0x2e,
        0x2f, 0x30, 0x31, 0x32, 0x32, 0x33, 0x34, 0x34,
        0x35, 0x36, 0x36, 0x37, 0x38, 0x38, 0x39, 0x3a,
        0x3b, 0x3b, 0x3b, 0x3c, 0x3c, 0x3c, 0x3d, 0x3d,
        0x3d, 0x3e, 0x3e, 0x3e, 0x3e, 0x3f, 0x3f, 0x3f,
    ];

    private static readonly byte[] VelocityMap2 =
    [
        0x00, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1a,
        0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x21, 0x21,
        0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29,
        0x2a, 0x2b, 0x2c, 0x2d, 0x2e, 0x2f, 0x2f, 0x30,
        0x31, 0x32, 0x32, 0x33, 0x34, 0x34, 0x35, 0x36,
        0x36, 0x37, 0x38, 0x38, 0x39, 0x39, 0x3a, 0x3a,
        0x3b, 0x3b, 0x3b, 0x3c, 0x3c, 0x3c, 0x3d, 0x3d,
        0x3d, 0x3e, 0x3e, 0x3e, 0x3e, 0x3f, 0x3f, 0x3f,
    ];

    private sealed class Channel
    {
        public byte Patch;
        public byte Volume = 63;
        public byte Pan = 64;
        public byte HoldPedal;
        public int ExtraVoices;
        public int PitchWheel = 8192;
        public int LastVoice;
        public bool EnableVelocity;
        public int VoiceCount;
        public int MappedVoices;

        public void Reset()
        {
            Patch = 0; Volume = 63; Pan = 64; HoldPedal = 0; ExtraVoices = 0;
            PitchWheel = 8192; LastVoice = 0; EnableVelocity = false;
            VoiceCount = 0; MappedVoices = 0;
        }
    }

    private sealed class Voice
    {
        public int MidiChannel = -1;
        public int MappedChannel = -1;
        public int Note = -1;
        public int Patch = -1;
        public int Velocity;
        public bool IsSustained;
        public int Age;

        public void Reset()
        {
            MidiChannel = -1; MappedChannel = -1; Note = -1; Patch = -1;
            Velocity = 0; IsSustained = false; Age = 0;
        }
    }

    private readonly Opl3Chip _chip;
    private readonly AdLibBank _bank;
    private readonly Channel[] _channels = new Channel[MidiChannels];
    private readonly Voice[] _voices = new Voice[Voices];

    /// <summary>0..15, as <c>sndMASTER_VOLUME</c> sets it. 15 is the driver's default.</summary>
    private int _masterVolume = 15;

    public SciAdLibDriver(Opl3Chip chip, AdLibBank bank)
    {
        _chip = chip;
        _bank = bank;
        for (var i = 0; i < MidiChannels; i++) _channels[i] = new Channel();
        for (var i = 0; i < Voices; i++) _voices[i] = new Voice();
        Reset();
    }

    public int MasterVolume
    {
        get => _masterVolume;
        set
        {
            _masterVolume = Math.Clamp(value, 0, 15);
            RenewNotes(-1, true);
        }
    }

    /// <summary>Silence the chip and put every register back the way the driver opens it.</summary>
    public void Reset()
    {
        foreach (var c in _channels) c.Reset();
        foreach (var v in _voices) v.Reset();

        // The emulator wires its own channel/operator objects together here, so this has
        // to come before any register write - not just before the first note.
        _chip.Reset(SampleRate);

        Write(0xBD, 0);     // no rhythm mode, no global tremolo/vibrato depth
        Write(0x08, 0);     // note-select off
        Write(0x01, 0x20);  // waveform select ENABLED - without this every instrument in
                            // the bank collapses to a plain sine and the bank's waveform
                            // bytes do nothing.
    }

    /// <summary>
    /// Registers go through the emulator's BUFFERED write path, which is not an
    /// optimisation — it is the difference between voice stealing working and not.
    ///
    /// An OPL2 is written a byte at a time through two I/O ports with a mandatory settling
    /// delay, so no two register writes ever reach the chip at the same instant. The
    /// envelope generator is clocked faster than that delay, which is why a key-off
    /// immediately followed by a key-on — what the driver does every time it steals a voice
    /// from one note to give it to another — is seen by the hardware as two distinct
    /// events and restarts the envelope.
    ///
    /// Written unbuffered, both land at the same emulated sample, the release state is
    /// never entered, and the note that stole the voice inherits the dying envelope of the
    /// one it replaced. Measured: a note retriggered that way came back at a third of its
    /// level each time until it faded to nothing. The buffered path queues each write a
    /// couple of samples on, which is the hardware's own write latency, and the envelopes
    /// behave.
    /// </summary>
    private void Write(int reg, int value) => _chip.WriteRegisterBuffered((ushort)reg, (byte)value);

    // ------------------------------------------------------------------
    // MIDI in
    // ------------------------------------------------------------------

    public void Send(byte status, byte data1, byte data2)
    {
        var command = status & 0xF0;
        var channel = status & 0x0F;

        switch (command)
        {
            case 0x80: NoteOff(channel, data1); break;
            case 0x90: NoteOn(channel, data1, data2); break;
            case 0xB0: ControlChange(channel, data1, data2); break;
            case 0xC0: _channels[channel].Patch = data1; break;

            case 0xE0:
                _channels[channel].PitchWheel = (data1 & 0x7F) | ((data2 & 0x7F) << 7);
                RenewNotes(channel, true);
                break;

            // Polyphonic and channel pressure are accepted and dropped, as in the original.
            case 0xA0:
            case 0xD0:
            default:
                break;
        }
    }

    private void ControlChange(int channel, byte controller, byte value)
    {
        switch (controller)
        {
            case 0x07:  // volume
                _channels[channel].Volume = (byte)(value >> 1);
                RenewNotes(channel, true);
                break;

            case 0x0A:  // pan - stored so renewNotes behaves identically, but a single
                        // mono OPL2 has nowhere to put it.
                _channels[channel].Pan = value;
                RenewNotes(channel, true);
                break;

            case 0x40:  // sustain pedal
                _channels[channel].HoldPedal = value;
                if (value == 0)
                    for (var i = 0; i < Voices; i++)
                        if (_voices[i].MidiChannel == channel && _voices[i].IsSustained)
                            VoiceOff(i);
                break;

            case 0x4B:  // how many of the nine chip voices this channel may hold
                VoiceMapping(channel, value);
                break;

            case 0x4E:
                _channels[channel].EnableVelocity = value != 0;
                break;

            case 0x7E:  // SCI_MIDI_CHANNEL_NOTES_OFF
                for (var i = 0; i < Voices; i++)
                    if (_voices[i].MidiChannel == channel && _voices[i].Note != -1)
                        VoiceOff(i);
                break;
        }
    }

    private void NoteOn(int channel, int note, int velocity)
    {
        if (velocity == 0) { NoteOff(channel, note); return; }

        velocity >>= 1;
        if (note < 12 || note > 107) return;

        // Retriggering a note already sounding on this channel reuses its voice.
        for (var i = 0; i < Voices; i++)
        {
            if (_voices[i].MidiChannel == channel && _voices[i].Note == note)
            {
                VoiceOff(i);
                VoiceOn(i, note, velocity);
                return;
            }
        }

        var voice = FindVoice(channel);
        if (voice == -1) return;   // the chip is full; the note is simply lost, as it was
        VoiceOn(voice, note, velocity);
    }

    private void NoteOff(int channel, int note)
    {
        for (var i = 0; i < Voices; i++)
        {
            if (_voices[i].MidiChannel == channel && _voices[i].Note == note)
            {
                if (_channels[channel].HoldPedal != 0) _voices[i].IsSustained = true;
                else VoiceOff(i);
                return;
            }
        }
    }

    private void VoiceOn(int voice, int note, int velocity)
    {
        var channel = _voices[voice].MidiChannel;
        var patch = _channels[channel].Patch;

        _voices[voice].Age = 0;
        ++_channels[channel].VoiceCount;

        if (patch != _voices[voice].Patch) SetPatch(voice, patch);

        _voices[voice].Velocity = velocity;
        SetNote(voice, note, true);
    }

    private void VoiceOff(int voice)
    {
        var channel = _voices[voice].MidiChannel;
        _voices[voice].IsSustained = false;
        SetNote(voice, _voices[voice].Note, false);
        _voices[voice].Note = -1;
        _voices[voice].Age = 0;
        if (channel >= 0) --_channels[channel].VoiceCount;
    }

    private int FindVoice(int channel)
    {
        var voice = -1;
        var oldestVoice = -1;
        var oldestAge = 0;

        // Round-robin from the channel's last voice, over the voices mapped to it.
        for (var i = 0; i < Voices; i++)
        {
            var v = (_channels[channel].LastVoice + i + 1) % Voices;
            if (_voices[v].MappedChannel != channel) continue;

            if (_voices[v].Note == -1)
            {
                voice = v;
                _voices[voice].MidiChannel = channel;
                break;
            }

            if (_voices[v].Age >= oldestAge)
            {
                oldestAge = _voices[v].Age;
                oldestVoice = v;
            }
        }

        if (voice == -1)
        {
            // Nothing free. Steal the longest-held note - unless every candidate started
            // on this very tick, in which case the new note is dropped instead.
            if (oldestAge == 0) return -1;
            VoiceOff(oldestVoice);
            voice = oldestVoice;
            _voices[voice].MidiChannel = channel;
        }

        _channels[channel].LastVoice = voice;
        return voice;
    }

    private void RenewNotes(int channel, bool key)
    {
        for (var i = 0; i < Voices; i++)
            if ((channel == -1 || _voices[i].MidiChannel == channel) && _voices[i].Note != -1)
                SetNote(i, _voices[i].Note, key);
    }

    // ------------------------------------------------------------------
    // Voice mapping - controller 0x4B
    // ------------------------------------------------------------------

    private void VoiceMapping(int channel, int voices)
    {
        var curVoices = 0;
        for (var i = 0; i < Voices; i++)
            if (_voices[i].MappedChannel == channel) curVoices++;
        curVoices += _channels[channel].ExtraVoices;

        if (curVoices < voices)
        {
            AssignVoices(channel, voices - curVoices);
        }
        else if (curVoices > voices)
        {
            ReleaseVoices(channel, curVoices - voices);
            DonateVoices();
        }
    }

    private void AssignVoices(int channel, int voices)
    {
        for (var i = 0; i < Voices; i++)
        {
            if (_voices[i].MappedChannel != -1) continue;
            if (_voices[i].Note != -1) VoiceOff(i);
            _voices[i].MappedChannel = channel;
            ++_channels[channel].MappedVoices;
            if (--voices == 0) return;
        }

        // Nothing left on the chip: the channel is owed the rest, and gets them back from
        // donateVoices() when another channel gives some up.
        _channels[channel].ExtraVoices += voices;
    }

    private void ReleaseVoices(int channel, int voices)
    {
        if (_channels[channel].ExtraVoices >= voices)
        {
            _channels[channel].ExtraVoices -= voices;
            return;
        }

        voices -= _channels[channel].ExtraVoices;
        _channels[channel].ExtraVoices = 0;

        // Idle voices first...
        for (var i = 0; i < Voices; i++)
        {
            if (_voices[i].MappedChannel == channel && _voices[i].Note == -1)
            {
                _voices[i].MappedChannel = -1;
                --_channels[channel].MappedVoices;
                if (--voices == 0) return;
            }
        }

        // ...then cut sounding ones short.
        for (var i = 0; i < Voices; i++)
        {
            if (_voices[i].MappedChannel == channel)
            {
                VoiceOff(i);
                _voices[i].MappedChannel = -1;
                --_channels[channel].MappedVoices;
                if (--voices == 0) return;
            }
        }
    }

    private void DonateVoices()
    {
        var freeVoices = 0;
        for (var i = 0; i < Voices; i++)
            if (_voices[i].MappedChannel == -1) freeVoices++;

        if (freeVoices == 0) return;

        for (var i = 0; i < MidiChannels; i++)
        {
            if (_channels[i].ExtraVoices >= freeVoices)
            {
                AssignVoices(i, freeVoices);
                _channels[i].ExtraVoices -= freeVoices;
                return;
            }

            if (_channels[i].ExtraVoices > 0)
            {
                AssignVoices(i, _channels[i].ExtraVoices);
                freeVoices -= _channels[i].ExtraVoices;
                _channels[i].ExtraVoices = 0;
            }
        }
    }

    // ------------------------------------------------------------------
    // Register writing
    // ------------------------------------------------------------------

    private void SetPatch(int voice, int patch)
    {
        if (!_bank.IsValid(patch)) patch = 0;
        _voices[voice].Patch = patch;

        var p = _bank[patch];
        SetOperator(RegisterOffset[voice], p.Op0);
        SetOperator(RegisterOffset[voice] + 3, p.Op1);
        Write(0xC0 + voice, (p.Feedback << 1) | (p.Algorithm ? 1 : 0));
    }

    private void SetOperator(int reg, AdLibOperator op)
    {
        Write(0x40 + reg, (op.KbScaleLevel << 6) | op.TotalLevel);
        Write(0x60 + reg, (op.AttackRate << 4) | op.DecayRate);
        Write(0x80 + reg, (op.SustainLevel << 4) | op.ReleaseRate);
        Write(0x20 + reg,
            (op.AmplitudeMod ? 0x80 : 0) |
            (op.Vibrato ? 0x40 : 0) |
            (op.EnvelopeType ? 0x20 : 0) |
            (op.KbScaleRate ? 0x10 : 0) |
            op.FrequencyMult);
        Write(0xE0 + reg, op.WaveForm);
    }

    private void SetNote(int voice, int note, bool key)
    {
        var channel = _voices[voice].MidiChannel;
        if (channel < 0) return;

        _voices[voice].Note = note;

        // Four table slots per semitone, so the pitch wheel moves in quarter-tone-ish
        // steps of one slot per 171 units of bend.
        var index = note << 2;
        var pitchWheel = _channels[channel].PitchWheel;
        int sign;

        if (pitchWheel == 0x2000) { pitchWheel = 0; sign = 0; }
        else if (pitchWheel > 0x2000) { pitchWheel -= 0x2000; sign = 1; }
        else { pitchWheel = 0x2000 - pitchWheel; sign = -1; }

        pitchWheel /= 171;
        index = sign == 1 ? index + pitchWheel : index - pitchWheel;
        index = Math.Clamp(index, 0, 0x1FC);

        var freq = AdlibFreq[index % 48];
        Write(0xA0 + voice, freq & 0xFF);

        var oct = index / 48;
        if (oct > 0) --oct;
        if (oct > 7) oct = 7;

        Write(0xB0 + voice, (key ? 0x20 : 0) | (oct << 2) | (freq >> 8));
        SetVelocity(voice);
    }

    private void SetVelocity(int voice)
    {
        var patchIndex = _voices[voice].Patch;
        if (!_bank.IsValid(patchIndex)) return;

        var patch = _bank[patchIndex];
        var pan = _channels[_voices[voice].MidiChannel].Pan;

        // The carrier always carries the level. The modulator only does in additive mode,
        // where it reaches the output directly; in this bank nothing is additive.
        SetVelocityReg(RegisterOffset[voice] + 3, CalcVelocity(voice, 1), patch.Op1.KbScaleLevel, pan);

        if (patch.Algorithm)
            SetVelocityReg(RegisterOffset[voice], CalcVelocity(voice, 0), patch.Op0.KbScaleLevel, pan);
    }

    private void SetVelocityReg(int regOffset, int velocity, int kbScaleLevel, int pan)
    {
        _ = pan;   // mono OPL2; see the class remarks
        Write(0x40 + regOffset, (kbScaleLevel << 6) | (63 - velocity));
    }

    private int CalcVelocity(int voice, int op)
    {
        var patch = _bank[_voices[voice].Patch];
        var oper = op == 0 ? patch.Op0 : patch.Op1;

        var velocity = _channels[_voices[voice].MidiChannel].Volume + 1;
        velocity = velocity * (VelocityMap1[_voices[voice].Velocity] + 1) / 64;
        velocity = velocity * (_masterVolume + 1) / 16;

        if (--velocity < 0) velocity = 0;

        return VelocityMap2[velocity] * (63 - oper.TotalLevel) / 63;
    }

    // ------------------------------------------------------------------

    /// <summary>
    /// Ages the sounding notes so <see cref="FindVoice"/> can tell which to steal.
    ///
    /// The original ran this off the OPL card's own timer rather than the sequence clock;
    /// here it is stepped once per sound tick. The counter is only ever compared against
    /// other voices' counters, so the rate changes nothing except at the boundary where
    /// every candidate note started on the same tick and the new note is dropped instead
    /// of stealing.
    /// </summary>
    public void Tick()
    {
        for (var i = 0; i < Voices; i++)
            if (_voices[i].Note != -1) _voices[i].Age++;
    }

    /// <summary>Key-off everything without disturbing the mapping. Used when a sound stops.</summary>
    public void AllNotesOff()
    {
        for (var i = 0; i < Voices; i++)
            if (_voices[i].Note != -1) VoiceOff(i);
    }

    /// <summary>Test seam: how many chip voices are currently holding a note.</summary>
    public int SoundingVoices => _voices.Count(v => v.Note != -1);

    /// <summary>Test seam: how many chip voices are mapped to a MIDI channel.</summary>
    public int MappedVoices => _voices.Count(v => v.MappedChannel != -1);
}
