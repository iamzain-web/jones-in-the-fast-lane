# Working rules for this port

This is an exact port of a 1990 SCI1 game from its own decompiled source. The whole point
is fidelity: the user will modify the game *after* it matches, so anything invented now is
a lie that has to be found and removed later.

These rules bind me and every agent I spawn. They exist because each one was broken.

## 1. Never invent user-visible text. Ever.

If the game shows or says something, the words are in the resources. Find them.

The CD build **speaks** almost all of its feedback — there are only 46 `Print` calls in
the entire game — so finding no text in a CD script means the line is a speech clip, NOT
that there is no line. The correct responses, in order:

1. The game speaks it here → play the clip (`MainViewModel.Speak(audioId)`, which also
   drives the mouth and shows the balloon with the subtitle).
2. The game prints it here → use the resource bytes verbatim.
3. Neither → **show nothing**. Silence is a valid port. A plausible-sounding sentence
   never is.

This applies to notices, tooltips, button labels, status lines and error messages alike.
A tooltip with wording I made up is worse than no tooltip.

## 2. The raw resources are the truth. The decompiled listing is not.

`scripts/jones-cd-dos-1.0/src/*.sc` is a decompilation and it *lies about strings*:

- It renders a SPACE inside a string literal as `_`. Porting those literally drew rows of
  dashes after every job title and shop item that the game never had. Raw script 217 holds
  `Cook` + 15 spaces + 2 `|`.
- It prints the same comment against different `Format` calls. Text resource 206 holds
  `"%s  $%d Hr."` (two spaces, under $10) at index 0 and `"%s $%d Hr."` at index 1; the
  listing shows both as identical.

Read strings from:
- `assets/raw/script/<n>.script` — printable-ASCII runs from the bytes
- `assets/raw/text/<n>.text` — NUL-separated, index 0 first

Use the listing for structure and control flow, never for string content.

## 3. Read the source for behaviour, including display

Every visual bug in this port so far came from guessing a view number, a coordinate or a
font when the script stated it outright. The scripts give `dsFONT`, `dsCOORD`, `dsWIDTH`,
`dsALIGN`, `nsLeft`, `nsTop`, `view`, `loop` and `cel` explicitly. Use them.

Worked example of getting this wrong: the newspaper headline was drawn in font 8 at
(14,40), stepping 10px, wrapped at 20 characters. `newspaper.sc:246-265` specifies font 3
at x=11, a 155-wide centred column, vertically centred about y=43, with the line breaks
already embedded in the string.

## 4. Replicate the original's bugs, deliberately and with a comment

Several are already ported: the dead upside kick in `economicIndex`, the 16-bit overflow
that makes a computer cheaper during a boom, the $2 lost to garnishment, Jones's travel
estimate mixing units. Each is commented at the site. Do not "fix" them.

## 5. Verify before claiming

Build, run the tests, and **look at the result** before saying something works. Measuring
beats eyeballing: font metrics from `assets/raw/font/<n>.font` (u16 unused, u16 charCount,
u16 height, then u16 offsets; each glyph is u8 width, u8 height, then 1bpp rows) settle
alignment questions that a screenshot only hints at. Where a group of labels shares one
`doFormat` they must measure to a uniform pixel width; where they don't, the invariant is
the price's right edge.

## 6. Keep the status documents honest

`PARITY.md` drifted into claiming Sound was unimplemented long after speech worked, and
percentages were quoted from it. A status file that flatters progress is worse than none.

## Environment

- **The Bash tool times out on this machine. Use PowerShell.** PowerShell 5.1:
  `Select-String` has no `-Recurse`; no `&&`, `||` or ternary.
- Build: `dotnet build src\Jones.App\Jones.App.Desktop\Jones.App.Desktop.csproj -v q --nologo`
  (kill `Jones.App.Desktop` first if a file is locked). Filter with
  `Select-String ': error |Avalonia error|error AVLN|Build succeeded'` — plain `error`
  also matches the harmless "0 Error(s)" line.
- Tests: `dotnet test src\Jones.Tests\Jones.Tests.csproj --nologo -v q`.
