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
let bus = null;        // everything plays through here so it can be measured
let analyser = null;   // taps the bus: what actually reaches the output
let probe = null;      // scratch buffer for the analyser
let cursor = 0;        // where the next chunk is scheduled, in context time
let speech = null;     // the one speech clip; the original never overlaps two
let pushedPeak = 0;    // loudest sample handed over since the page loaded
let pushedFrames = 0;  // how many frames in total

// THE AUTOPLAY RULE. No browser lets a page make a sound before it has been interacted
// with, so the context is created suspended and resumed by the player's first click. The
// game's own first click is the one that starts it, so nobody ever notices.
globalThis.jonesAudioStart = (sampleRate) => {
    try {
        const Ctx = window.AudioContext || window.webkitAudioContext;
        if (!Ctx) return 0;
        actx = new Ctx({ sampleRate });
        cursor = 0;

        // A BUS WITH A TAP ON IT. Everything plays through `bus`, and `analyser` watches
        // what passes - so "is there sound" is a MEASUREMENT rather than an opinion.
        // Queued is not the same as audible: a buffer full of zeroes schedules exactly as
        // happily as a buffer full of music, and this head has already been silent twice
        // while every indicator read healthy.
        bus = actx.createGain();
        analyser = actx.createAnalyser();
        analyser.fftSize = 2048;
        probe = new Float32Array(analyser.fftSize);
        bus.connect(analyser);
        bus.connect(actx.destination);
        pushedPeak = 0;
        pushedFrames = 0;
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
        // COPIED, NOT VIEWED, AND THAT IS THE WHOLE BUG. `bytes` is a view straight onto
        // the WebAssembly heap, and its byteOffset is wherever .NET happened to put the
        // array - not necessarily a multiple of four. Constructing a Float32Array over an
        // unaligned offset throws RangeError, which the catch below turned into a quiet
        // `return 0`: no exception in C#, no error in the console, the pump running every
        // 60ms, and not one sample ever scheduled. The copy is always aligned.
        // `bytes` IS NOT A TYPED ARRAY. .NET's MemoryView marshalling hands over a wrapper
        // around a span of the WebAssembly heap - it has `length` and `slice`, and it does
        // NOT support indexing. `new Uint8Array(view)` therefore produces an array of the
        // RIGHT LENGTH AND ALL ZEROES, which is the most expensive possible failure: every
        // frame count, every buffer depth and every scheduling decision stays perfectly
        // correct while the game plays silence. The synthesiser was rendering peaks of
        // 0.55 and the speakers got nothing.
        //
        // `slice()` is the MemoryView's own copy-out, and returns a real Uint8Array.
        const copy = bytes.slice();
        const f = new Float32Array(copy.buffer, copy.byteOffset, copy.byteLength >> 2);
        const frames = f.length >> 1;
        if (frames <= 0) return 0;

        const buf = actx.createBuffer(2, frames, actx.sampleRate);
        const L = buf.getChannelData(0), R = buf.getChannelData(1);
        for (let i = 0; i < frames; i++) { L[i] = f[2 * i]; R[i] = f[2 * i + 1]; }

        // Measured on the way past: the loudest sample in this chunk. If the synthesiser
        // is producing silence this stays at zero no matter how much gets scheduled.
        let peak = 0;
        for (let i = 0; i < f.length; i++) { const v = f[i] < 0 ? -f[i] : f[i]; if (v > peak) peak = v; }
        if (peak > pushedPeak) pushedPeak = peak;
        pushedFrames += frames;

        const src = actx.createBufferSource();
        src.buffer = buf;
        src.connect(bus);

        // RE-ANCHOR AFTER A STALL. This head has one thread and the game can hold it for
        // longer than the buffer lasts; when that happens the cursor is already in the
        // past and scheduling there plays everything at once. Starting afresh costs a
        // gap - which is what the player already heard - instead of a burst.
        const now = actx.currentTime;
        if (cursor < now + 0.02) cursor = now + 0.08;
        src.start(cursor);
        cursor += frames / actx.sampleRate;
        return cursor - now;
    } catch (e) {
        // SAID ONCE. Returning 0 in silence is exactly how this went unnoticed.
        if (!globalThis.__jonesAudioComplained) {
            globalThis.__jonesAudioComplained = true;
            console.warn('Jones: audio push failed:', e);
        }
        return 0;
    }
};

// Lets anyone - including whoever is debugging this next - SEE whether sound is running,
// which is otherwise only observable by listening. Audio on this head has already been
// absent once without a single error to say so.
globalThis.jonesAudioStatus = () => {
    if (!actx) return { state: 'no context' };

    // What is passing through the output RIGHT NOW, read off the tap.
    let outPeak = 0, sum = 0;
    try {
        analyser.getFloatTimeDomainData(probe);
        for (let i = 0; i < probe.length; i++) {
            const v = probe[i] < 0 ? -probe[i] : probe[i];
            if (v > outPeak) outPeak = v;
            sum += probe[i] * probe[i];
        }
    } catch { /* leave the zeroes */ }

    return {
        state: actx.state,
        rate: actx.sampleRate,
        bufferedAhead: +(cursor - actx.currentTime).toFixed(3),
        pushedFrames,
        pushedPeak: +pushedPeak.toFixed(4),       // loudest sample ever handed over
        outputPeak: +outPeak.toFixed(4),          // loudest sample leaving the bus now
        outputRms: +Math.sqrt(sum / probe.length).toFixed(4),
        speaking: !!(speech && !speech.paused && !speech.ended),
    };
};

globalThis.jonesAudioStopAll = () => {
    try { cursor = 0; } catch { /* nothing to undo */ }
};

globalThis.jonesSpeechPlay = (url) => {
    try {
        if (speech) { speech.pause(); speech = null; }
        speech = new Audio(url);
        speech.crossOrigin = 'anonymous';

        // Routed through the same bus as the music, so the analyser measures the voices
        // too - and so a future volume control has one place to sit.
        try {
            if (actx && bus) actx.createMediaElementSource(speech).connect(bus);
        } catch { /* some browsers refuse a second source; it still plays to the device */ }

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
