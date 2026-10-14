# The browser head

`src/Jones.App/Jones.App.Browser` — the port compiled to WebAssembly, so the game runs on a
page with nothing to install. This file records what works, what does not, and the two
things that are not merely unfinished but *design decisions nobody has taken yet*.

## It needs a workload the other heads do not

```
dotnet workload install wasm-tools
```

Avalonia draws through Skia. On every other head Skia is a native library loaded at run
time; in WebAssembly there is no dynamic linker to load one with, so `libSkiaSharp.a` must
be **relinked into the wasm binary at build time**. That is what `WasmBuildNative` in the
csproj turns on, and it needs Emscripten, which is what the workload brings.

**Without the workload the build still succeeds.** It emits one warning —

> `@(NativeFileReference) is not empty, but the native references won't be linked in,
> because neither $(WasmBuildNative), nor $(RunAOTCompilation) are 'true'`

— and the page it produces shows the Avalonia splash and then stops, with this in the
browser console and nothing at all on the page:

```
System.DllNotFoundException: libSkiaSharp
   at SkiaSharp.SKImageInfo..cctor()
```

A build that reports `0 Error(s)` and produces an app that cannot start is worth the
paragraph. Check the warning, not the exit code.

## What it would show today

Only what is compiled into `Jones.App` itself: `Assets/game`, which is **838 cels of
Sierra's 1x art**, and two fonts — 0.9 MB in total. So:

| | state |
|---|---|
| art | Sierra's 1990 pixels at factor 1. None of the rebuilt buildings or cast. |
| text | **nothing printed.** `SciText.Load` is never called — there are no text resources. |
| speech, music, lip sync | none. |
| saves | untested. |

The desktop and Android heads each do a block of work before Avalonia starts — find the
assets, load the strings, point the lip-sync tables at a directory. `Program.cs` here does
none of it, because there is no filesystem to point at.

## The two decisions nobody has taken

Both are the same shape, and the shape is why this is not just a to-do list.

**1. Everything shared reads through a path or a stream, synchronously.** `SciText.Load`,
`LipSync.SetDirectory` and `Subtitles.Load` call `System.IO.File` with a path.
`SciArt.LoadCel` takes a stream. A browser has neither: it has `fetch`, which is
asynchronous, and a wasm UI thread that must not block on it.

So each resource needs one of:

- **embedded** as `AvaloniaResource`, which is synchronous and works today — but puts the
  bytes in the download. The 1x art already arrives this way;
- **fetched up front**, before Avalonia starts, into memory — one wait at the start, and
  every later read is synchronous;
- **fetched lazily** and the screen redrawn when the bytes land, which is the only option
  that scales to 57 MB of art and the only one that needs a change to how `SciArt` hands
  back a cel.

**2. The upscaled art is 57 MB.** `png4x` plus `png4x-cast0` is what makes the game look
like ours rather than Sierra's, and it is also a 57 MB page load. That is a product
decision, not a technical one.

`ArtStore` (added for Android, which has the same no-filesystem problem) is the seam a
browser implementation would plug into: `Locate`, `Exists`, `Open`. Its `Open` is
synchronous, so a browser store has to have the bytes already — which makes the choice
above *the* question for this head, not an implementation detail.
