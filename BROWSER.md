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

---

## It is built, and it is public

`https://iamzain-web.github.io/jones-in-the-fast-lane/` — anonymous, no sign-in. The
question above was answered with the third option: **fetched lazily, screen redrawn when
the bytes land.** `HttpArtStore` fetches each cel the first time it is drawn, records a
404 so it is never asked for twice, and raises `ArtStore.Arrived`; `MainViewModel`
coalesces those to one rebuild per frame.

### Deploying

The site is an orphan `gh-pages` branch holding a publish of `wwwroot`, nothing else:

```
dotnet publish src/Jones.App/Jones.App.Browser/Jones.App.Browser.csproj -c Release -o <out>
```

- **Publish into an EMPTY directory.** `dotnet publish` does not clean, so hashed
  assemblies accumulate: one run left eight `dotnet.native.*.wasm` of ~30 MB each, 417 MB
  of runtime for a build that needs 38.
- **Drop the `.gz`/`.br` sidecars.** A static host serves neither and the loader never asks
  for them. They were 380 MB of the 475.
- **`.nojekyll` must be at the root** or Pages silently skips every directory beginning with
  an underscore — which is the entire runtime.
- **Push as `iamzain-web`.** The remote URL carries the user for a reason; the bare URL
  resolves to a different stored credential and GitHub answers 403.

### Two faults that no check in this repo could see

**The page never ran.** `async Task Main` makes the compiler synthesise an entry point
whose body is `<Main>(args).GetAwaiter().GetResult()` — a blocking wait, which the Mono
interpreter answers with `NIY encountered in method Program:Main` and `exit(1)`, before a
line of ours. Build clean, publish clean, 200 for all 1,318 files, game dead. **Return the
Task; never `await` in this entry point.**

**Then it ran Sierra's town.** `SpriteVm.Interchangeable` keeps a container when a sprite
is unchanged — but a sprite resolves its cel through the `RenderScale.HighRes` *converter*
on its binding, so a container that is not recreated never looks again, and art that
arrives after the screen was built is decoded, cached and never drawn. The arrival rebuild
drops the comparer for that one pass.

Both were found by looking at the rendered page. Neither was visible from an exit code, a
network log, or 693 passing tests.

### Known, and not fixed here

Art URLs are unversioned, so a changed cel keeps its URL and a returning tester can be
served the old one out of the browser cache until Pages' `max-age` expires. The runtime is
content-hashed and immune; the art is not.
