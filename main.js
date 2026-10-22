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
let bus = null;        // master gain: everything plays through here
let analyser = null;   // taps the bus, so "is there sound" is a measurement
let probe = null;      // scratch buffer for the analyser
let node = null;       // pulls from the ring, continuously
let speech = null;     // the one speech clip; the original never overlaps two
let pushedPeak = 0;    // loudest sample handed over since the page loaded
let pushedFrames = 0;  // how many frames in total
let starved = 0;       // frames the node wanted and the ring could not give

// THE RING. Four seconds of interleaved stereo.
//
// WHY A RING AND NOT A SCHEDULE. The first version cut the stream into 50ms AudioBuffers
// and booked each one to start where the last ended. That is seamless only while the
// arithmetic is perfect, and on this head it cannot be: one thread, the browser's event
// loop on it, and the game's own screen rebuilds stalling it for runs of ticks at a time.
// Every stall left a hole, every catch-up left an overlap, and every boundary was a
// discontinuity in the waveform - which is a CLICK. Zain, listening to it: "the audio
// sounds horrible and broken". The meter said signal was flowing and it was right; signal
// flowing and signal being continuous are different claims, and only one of them was
// measured.
//
// A ring has no boundaries. The node asks for samples at the rate the hardware consumes
// them and takes whatever is there; a stall costs silence for its duration and nothing
// else, and the stream either side of it is unbroken.
const RING_SECONDS = 4;
let ring = null, wi = 0, ri = 0, filled = 0;

globalThis.jonesAudioStart = (sampleRate) => {
    try {
        const Ctx = window.AudioContext || window.webkitAudioContext;
        if (!Ctx) return 0;
        actx = new Ctx({ sampleRate });

        ring = new Float32Array(actx.sampleRate * RING_SECONDS * 2);
        wi = 0; ri = 0; filled = 0;
        pushedPeak = 0; pushedFrames = 0; starved = 0;

        // 0.8, because the synthesiser's peaks measured 1.09 and everything above 1.0 is
        // hard-clipped by the output - which is its own kind of "horrible and broken".
        bus = actx.createGain();
        bus.gain.value = 0.8;

        analyser = actx.createAnalyser();
        analyser.fftSize = 2048;
        probe = new Float32Array(analyser.fftSize);

        // A ScriptProcessor rather than an AudioWorklet: a worklet runs on the audio
        // thread and would need a SharedArrayBuffer to reach, which needs COOP/COEP
        // headers, which a static host does not send. This runs on the main thread - the
        // same thread that stalls - but it PULLS from the ring instead of scheduling into
        // the future, so a stall is a gap rather than a seam.
        node = actx.createScriptProcessor(4096, 0, 2);
        node.onaudioprocess = (e) => {
            const L = e.outputBuffer.getChannelData(0);
            const R = e.outputBuffer.getChannelData(1);
            for (let i = 0; i < L.length; i++) {
                if (filled >= 2) {
                    L[i] = ring[ri]; R[i] = ring[ri + 1];
                    ri = (ri + 2) % ring.length;
                    filled -= 2;
                } else {
                    L[i] = 0; R[i] = 0;
                    starved++;
                }
            }
        };
        node.connect(bus);
        bus.connect(analyser);
        bus.connect(actx.destination);

        const wake = () => { if (actx && actx.state === 'suspended') actx.resume(); };
        for (const e of ['pointerdown', 'keydown', 'touchstart'])
            window.addEventListener(e, wake, { passive: true });

        return actx.sampleRate;
    } catch { return 0; }
};

// Copies a chunk into the ring. Returns how many seconds are now waiting, which is what
// the C# side paces itself by.
globalThis.jonesAudioPush = (bytes) => {
    if (!actx || !ring) return 0;
    try {
        // `bytes` IS NOT A TYPED ARRAY. .NET's MemoryView marshalling hands over a wrapper
        // around a span of the WebAssembly heap - it has `length` and `slice`, and it does
        // NOT support indexing. `new Uint8Array(view)` therefore produces an array of the
        // RIGHT LENGTH AND ALL ZEROES: every frame count and buffer depth stays perfectly
        // correct while the game plays silence. `slice()` is its own copy-out.
        const copy = bytes.slice();
        const f = new Float32Array(copy.buffer, copy.byteOffset, copy.byteLength >> 2);

        let peak = 0;
        for (let i = 0; i < f.length; i++) { const v = f[i] < 0 ? -f[i] : f[i]; if (v > peak) peak = v; }
        if (peak > pushedPeak) pushedPeak = peak;

        // Drop rather than overwrite what has not been played yet: a full ring means the
        // game is producing faster than the hardware consumes, and the oldest samples are
        // the ones about to be heard.
        const room = ring.length - filled;
        const take = Math.min(f.length, room);
        for (let i = 0; i < take; i++) { ring[wi] = f[i]; wi = (wi + 1) % ring.length; }
        filled += take;
        pushedFrames += take >> 1;

        return filled / 2 / actx.sampleRate;
    } catch (e) {
        if (!globalThis.__jonesAudioComplained) {
            globalThis.__jonesAudioComplained = true;
            console.warn('Jones: audio push failed:', e);
        }
        return 0;
    }
};

globalThis.jonesAudioStopAll = () => {
    try { filled = 0; ri = wi; } catch { /* nothing to undo */ }
};

globalThis.jonesAudioStatus = () => {
    if (!actx) return { state: 'no context' };

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
        bufferedAhead: +(filled / 2 / actx.sampleRate).toFixed(3),
        starvedFrames: starved,          // every one of these is an audible hole
        pushedFrames,
        pushedPeak: +pushedPeak.toFixed(4),
        outputPeak: +outPeak.toFixed(4),
        outputRms: +Math.sqrt(sum / probe.length).toFixed(4),
        speaking: !!(speech && !speech.paused && !speech.ended),
    };
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
