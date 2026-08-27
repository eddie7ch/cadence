# Recording the demo

The README points at `docs/demo.gif`. This is how to produce it.

The goal is a loop of about forty seconds that shows the parts of Cadence that are hard to convey in prose: a binary watch file going in, and a map, an elevation profile and a set of splits coming out. Nobody watches a two-minute GIF, and GitHub will not let you scrub one, so every second has to earn its place.

---

## Before you press record

Reset to a known state, then seed it deliberately. A demo that opens on an empty dashboard spends its first ten seconds on setup.

```bash
docker compose down --volumes
docker compose up --build --detach
```

Wait for `docker compose ps` to show `api` as `healthy`, then:

1. Register an account at <http://localhost:5173>. Use a plausible name; `test@test.com` reads as unfinished.
2. Upload `samples/canmore-benchlands-trail-run.gpx` and `samples/nose-hill-tempo-run.fit`, and let both finish importing. These two give the trends view something to plot.
3. Check that the heart-rate zone chart on one of them is populated. Both sample files carry heart rate, so it should be - if it is empty, the import has not finished.
4. **Leave `samples/bow-river-pathway-easy-run.gpx` un-uploaded.** That is the file you drop on camera.

Then tidy the capture area:

- A clean browser profile or a guest window. No bookmarks bar, no extension icons, no other tabs.
- Fixed window size. 1280×800 is a good target: legible when GitHub scales it into a README column, and small enough to keep the file under control.
- Notifications off. Do Not Disturb on the OS, and quiet any chat client.
- Light or dark, but decide first and stay there. A theme flip mid-recording looks like a glitch.

---

## Shot list

Roughly forty seconds. Move deliberately - a cursor that darts around is unreadable at 15 fps.

| Time | Shot | Why it is in the demo |
| --- | --- | --- |
| 0:00-0:05 | Activity list with the two seeded activities | Establishes what the app is in one glance |
| 0:05-0:12 | Drag `bow-river-pathway-easy-run.gpx` onto the upload target; the row appears immediately as *Pending* | This is the background queue: the request returned before the work started |
| 0:12-0:18 | The row flips to *Ready* and fills in with distance, pace and elevation | The import finished off the request thread |
| 0:18-0:28 | Open the Canmore trail run. Pan the map, then hover along the elevation profile so the chart cursor tracks | Shows the route geometry and the time series together |
| 0:28-0:34 | Scroll to the splits table, with pace and grade-adjusted pace side by side | The GAP column is the point - on a climb the two differ by a minute or more per kilometre |
| 0:34-0:40 | The trends view, weekly bars and the heart-rate zone breakdown | Shows there is an analytics layer, not just a file viewer |

If you have five more seconds, spend them on the nearby-activities search: drop a pin and watch it filter. That is the `ST_DWithin` query, and it is the only place the spatial index is visible.

End the recording on a full frame that reads well as a still. GitHub shows the first frame until the GIF loads, and it is also what a link preview picks up.

---

## Capture

Record video first, convert second. Capturing straight to GIF gives you no way to trim, and no way to re-encode at a different size without re-shooting.

| Platform | Tool | Notes |
| --- | --- | --- |
| Windows | [ScreenToGif](https://www.screentogif.com/) | Records a fixed region and has a usable frame editor; can export to `.mp4` for the ffmpeg path below |
| macOS | [Kap](https://getkap.co/) | Export as MP4, not GIF |
| macOS, no install | QuickTime, `File → New Screen Recording` | Fine; crop afterwards with ffmpeg |
| Linux (X11) | [Peek](https://github.com/phw/peek) or `ffmpeg -f x11grab` | |
| Linux (Wayland) | `wf-recorder -g "$(slurp)"` | |

Record at the display's native resolution and scale down during conversion. Scaling down is sharp; scaling up is not.

---

## Converting to GIF

A naive `ffmpeg -i demo.mp4 demo.gif` quantises each frame against the default 256-colour palette and produces something both large and banded. Generate a palette from the actual footage first:

```bash
ffmpeg -i demo.mp4 \
  -vf "fps=15,scale=1280:-1:flags=lanczos,palettegen=stats_mode=diff" \
  -y palette.png

ffmpeg -i demo.mp4 -i palette.png \
  -lavfi "fps=15,scale=1280:-1:flags=lanczos,paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle" \
  -y docs/demo.gif

rm palette.png
```

- `fps=15` is the main size lever. A screen recording of deliberate mouse movement looks fine at 15; below 12 it starts to stutter visibly.
- `stats_mode=diff` weights the palette toward the pixels that change between frames, which is what a screen recording mostly is - a static chrome around a small area of motion.
- `diff_mode=rectangle` lets the encoder write only the changed region of each frame. On a mostly-static UI this is a large saving.
- `bayer_scale=3` keeps dithering subtle. Higher values produce visible cross-hatching on flat UI backgrounds.

Then squeeze it:

```bash
gifsicle -O3 --lossy=60 --colors 128 docs/demo.gif -o docs/demo.gif
```

**Aim for under 8 MB.** GitHub will happily serve more, but the README stalls on a slow connection and the demo goes unwatched, which is the one failure mode that makes the whole exercise pointless. If you are over budget, in this order: cut a shot, drop to 12 fps, scale to 1100 px wide, then reduce colours to 96.

Check the result at the size it will actually be seen:

```bash
ls -lh docs/demo.gif
```

Open it in a browser at 100% and watch it loop twice. Anything that annoys you on the second loop will annoy a reviewer on the first.

---

## Wiring it into the README

The README currently mentions `docs/demo.gif` as plain text, because the file does not exist and a broken image in the first screen of a README is worse than no image at all. Once it does exist, replace that mention near the top of the README with:

```markdown
![Cadence: uploading a GPX file, the imported route and elevation profile, and kilometre splits with grade-adjusted pace](docs/demo.gif)
```

Write real alt text. It is what someone reading with images disabled gets, and it is what appears if the file ever moves.

`.gitattributes` already marks `*.gif` as binary, so Git will not try to normalise line endings inside it or produce a useless diff.

---

## Re-recording later

The shot list above is the contract, not the exact pixels. When the UI changes, re-record against the same list rather than inventing a new one - it keeps the demo comparable, and it keeps you from quietly dropping the shot that has become inconvenient to demonstrate.
