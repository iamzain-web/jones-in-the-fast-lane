using System;
using Jones.App.Audio;
using Jones.Net;

namespace Jones.App.Net;

/// <summary>
/// The host's sound player while hosting: plays locally exactly as before AND sends every
/// call to the joiners, who replay it through their own player.
///
/// <para>
/// THE HOST'S MUTES ARE THE HOST'S. The F2/F3/speech switches are checked in the view model
/// before it ever calls the player, so a host who silences the music would silence every
/// joiner too. Those call sites send the muted call to <see cref="NetworkOnly"/> instead,
/// which reaches the joiners without making a sound here; each joiner then applies its OWN
/// switches (<see cref="NetClient"/>). And the switches themselves stop sound through
/// <see cref="Inner"/>, so muting the host does not cut anyone else off mid-song.
/// </para>
///
/// <para>
/// <see cref="Enabled"/> and <see cref="SpeechPosition"/> are local and never sent. The lip
/// sync reads the position, and the mouth frames reach the joiner as part of the picture.
/// </para>
/// </summary>
public sealed class BroadcastAudioPlayer(IAudioPlayer inner, Action<AudioMsg> send) : IAudioPlayer
{
    public IAudioPlayer Inner { get; } = inner;

    /// <summary>Sends without playing.</summary>
    public IAudioPlayer NetworkOnly { get; } = new Sender(send);

    public void PlaySpeech(int audioId) { Inner.PlaySpeech(audioId); NetworkOnly.PlaySpeech(audioId); }
    public TimeSpan? SpeechPosition => Inner.SpeechPosition;
    public void StopSpeech() { Inner.StopSpeech(); NetworkOnly.StopSpeech(); }
    public void PlayMusic(int soundResource, bool loop = true) { Inner.PlayMusic(soundResource, loop); NetworkOnly.PlayMusic(soundResource, loop); }
    public void StopMusic() { Inner.StopMusic(); NetworkOnly.StopMusic(); }
    public void CutMusic() { Inner.CutMusic(); NetworkOnly.CutMusic(); }
    public void PauseMusic() { Inner.PauseMusic(); NetworkOnly.PauseMusic(); }

    public void PlayEffect(int soundResource, bool loop = false, bool resumeMusicWhenDone = false)
    {
        Inner.PlayEffect(soundResource, loop, resumeMusicWhenDone);
        NetworkOnly.PlayEffect(soundResource, loop, resumeMusicWhenDone);
    }

    public void EndEffectLoop() { Inner.EndEffectLoop(); NetworkOnly.EndEffectLoop(); }
    public void PlayEffect2(int soundResource) { Inner.PlayEffect2(soundResource); NetworkOnly.PlayEffect2(soundResource); }
    public void StopEffects() { Inner.StopEffects(); NetworkOnly.StopEffects(); }

    public bool Enabled
    {
        get => Inner.Enabled;
        set => Inner.Enabled = value;
    }

    private sealed class Sender(Action<AudioMsg> send) : IAudioPlayer
    {
        public void PlaySpeech(int audioId) => send(new AudioMsg { Op = AudioOp.Speech, Id = audioId });
        public TimeSpan? SpeechPosition => null;
        public void StopSpeech() => send(new AudioMsg { Op = AudioOp.StopSpeech });
        public void PlayMusic(int soundResource, bool loop = true) => send(new AudioMsg { Op = AudioOp.Music, Id = soundResource, Loop = loop });
        public void StopMusic() => send(new AudioMsg { Op = AudioOp.StopMusic });
        public void CutMusic() => send(new AudioMsg { Op = AudioOp.CutMusic });
        public void PauseMusic() => send(new AudioMsg { Op = AudioOp.PauseMusic });

        public void PlayEffect(int soundResource, bool loop = false, bool resumeMusicWhenDone = false) =>
            send(new AudioMsg { Op = AudioOp.Effect, Id = soundResource, Loop = loop, Resume = resumeMusicWhenDone });

        public void EndEffectLoop() => send(new AudioMsg { Op = AudioOp.EndEffectLoop });
        public void PlayEffect2(int soundResource) => send(new AudioMsg { Op = AudioOp.Effect2, Id = soundResource });
        public void StopEffects() => send(new AudioMsg { Op = AudioOp.StopEffects });
        public bool Enabled { get; set; } = true;
    }
}
