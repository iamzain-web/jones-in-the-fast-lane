// BEFORE THE RUNTIME STARTS, because .NET reaches these by name through [JSImport] and a
// name that is not there yet is a binding failure rather than a null.
//
// WHY THE GAME NEEDS THEM. SaveStore and SettingsStore write files, and in WebAssembly the
// filesystem is Emscripten's: RAM wearing a filesystem's clothes. Saving worked, Restore
// worked in the same session, and the save was gone the moment the tab closed - with
// nothing logged, because nothing had failed. See DurableSaves.
//
// Each one swallows its own failure. A private window, storage disabled, or a full quota
// must leave the game playable rather than throwing out of a save.
globalThis.jonesStorageGet = (key) => {
    try { return localStorage.getItem(key); } catch { return null; }
};
globalThis.jonesStorageSet = (key, value) => {
    try { localStorage.setItem(key, value); return true; } catch { return false; }
};
globalThis.jonesStorageRemove = (key) => {
    try { localStorage.removeItem(key); } catch { /* nothing to undo */ }
};

// Takes the loading screen away. Called from C# once the town's own art has arrived, NOT
// when Avalonia starts drawing: the art is fetched over the network and the 1x cels
// compiled into the assembly are what the game falls back to meanwhile, so an earlier
// handover shows Sierra's 1990 town for a second before ours replaces it.
globalThis.jonesReady = () => {
    const boot = document.getElementById('boot');
    if (!boot) return;
    boot.classList.add('gone');
    setTimeout(() => boot.remove(), 400);
};

// ----------------------------------------------------------------------------------
// SOUND. There was none at all on this head: Desktop has WindowsAudioPlayer, Android has
// AndroidAudioPlayer, and the browser fell through to SilentAudioPlayer - so no music, no
// effects, no speech, and nothing saying why.
//
// TWO PATHS, BECAUSE THE GAME HAS TWO. The music and effects are SYNTHESISED in managed
// code by Jones.Audio, which hands back stereo PCM and needs somewhere to put it; the
// speech is 533 ordinary .wav files, which a browser plays better than any of this.
// ----------------------------------------------------------------------------------

let actx = null;       // the output context
let cursor = 0;        // where the next chunk is scheduled, in context time
let speech = null;     // the one speech clip; the original never overlaps two

// THE AUTOPLAY RULE. No browser lets a page make a sound before it has been interacted
// with, so the context is created suspended and resumed by the player's first click. The
// game's own first click is the one that starts it, so nobody ever notices.
globalThis.jonesAudioStart = (sampleRate) => {
    try {
        const Ctx = window.AudioContext || window.webkitAudioContext;
        if (!Ctx) return 0;
        actx = new Ctx({ sampleRate });
        cursor = 0;
        const wake = () => { if (actx && actx.state === 'suspended') actx.resume(); };
        for (const e of ['pointerdown', 'keydown', 'touchstart'])
            window.addEventListener(e, wake, { passive: true });
        return actx.sampleRate;
    } catch { return 0; }
};

// Takes a chunk of interleaved stereo float samples and schedules it to play after
// whatever is already queued. Returns how many seconds are now buffered ahead of the
// playhead, which is what the C# side paces itself by.
globalThis.jonesAudioPush = (bytes) => {
    if (!actx) return 0;
    try {
        const f = new Float32Array(bytes.buffer, bytes.byteOffset, bytes.byteLength >> 2);
        const frames = f.length >> 1;
        if (frames <= 0) return 0;

        const buf = actx.createBuffer(2, frames, actx.sampleRate);
        const L = buf.getChannelData(0), R = buf.getChannelData(1);
        for (let i = 0; i < frames; i++) { L[i] = f[2 * i]; R[i] = f[2 * i + 1]; }

        const src = actx.createBufferSource();
        src.buffer = buf;
        src.connect(actx.destination);

        // RE-ANCHOR AFTER A STALL. This head has one thread and the game can hold it for
        // longer than the buffer lasts; when that happens the cursor is already in the
        // past and scheduling there plays everything at once. Starting afresh costs a
        // gap - which is what the player already heard - instead of a burst.
        const now = actx.currentTime;
        if (cursor < now + 0.02) cursor = now + 0.08;
        src.start(cursor);
        cursor += frames / actx.sampleRate;
        return cursor - now;
    } catch { return 0; }
};

// Lets anyone - including whoever is debugging this next - SEE whether sound is running,
// which is otherwise only observable by listening. Audio on this head has already been
// absent once without a single error to say so.
globalThis.jonesAudioStatus = () => actx
    ? { state: actx.state, rate: actx.sampleRate, bufferedAhead: +(cursor - actx.currentTime).toFixed(3) }
    : { state: 'no context' };

globalThis.jonesAudioStopAll = () => {
    try { cursor = 0; } catch { /* nothing to undo */ }
};

globalThis.jonesSpeechPlay = (url) => {
    try {
        if (speech) { speech.pause(); speech = null; }
        speech = new Audio(url);
        speech.play().catch(() => { /* refused before a gesture; the subtitle still shows */ });
    } catch { /* a missing clip is a silent line, not a dead game */ }
};

globalThis.jonesSpeechStop = () => {
    try { if (speech) { speech.pause(); speech = null; } } catch { /* nothing to undo */ }
};

// Seconds into the clip, or -1 for "not speaking". The lip sync follows THIS rather than a
// clock started at the call, because the audio element does not begin the instant it is
// asked to - see IAudioPlayer.SpeechPosition.
globalThis.jonesSpeechPosition = () => {
    try {
        if (!speech || speech.paused || speech.ended) return -1;
        return speech.currentTime;
    } catch { return -1; }
};

import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
