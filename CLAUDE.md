# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and run

```bash
# Build
dotnet build src/TextureUnshuffle/TextureUnshuffle.csproj

# Run
dotnet run --project src/TextureUnshuffle
```

No test project exists yet.

## What this app does

Desktop tool (Avalonia, .NET 8) for manually unshuffling tile-based textures. A shuffled texture (e.g. game asset) is split into a grid of tiles; the user can auto-restore the original order via a seed-based permutation or swap tiles manually.

Reference: `Sample/texture_unshuffle.html` — the original web version this app replaces.

## Architecture

**Pattern:** MVVM with CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`).

**SkiaSharp** (bundled with Avalonia, no separate package) handles all pixel-level image work. Avalonia `Bitmap` is only used for display; conversion goes through `BitmapConverter.ToAvaloniaBitmap(SKBitmap)` which encodes to PNG in-memory.

**Dialog service pattern:** `IDialogService` (Services/) is implemented directly by `MainWindow` (which has access to `StorageProvider`). The ViewModel receives it via constructor. The parameterless ViewModel constructor (`this(null!)`) exists only for the Avalonia XAML design-time previewer.

**Core data flow:**
1. `TileGrid.Load(path, tileSize, cols, rows)` — decodes image, slices into `SKBitmap[] Tiles`
2. `TileGrid.Arrangement` — `int[]` where `arrangement[displayPos] = tileIndex`; start state is identity `[0,1,2,…]`
3. `TileGrid.BuildResultBitmap()` — reassembles tiles according to current `Arrangement`
4. `MainWindowViewModel.RefreshTileItems()` — rebuilds `ObservableCollection<TileItemViewModel>` from current arrangement; each `TileItemViewModel` holds the tile image, its position, and a reference to `SelectTileCommand`

**Tile selection/swap flow:** `SelectTileCommand` receives the clicked position. First click sets `_selectedPos` and marks `TileItemViewModel.IsSelected = true`. Second click on a different tile swaps `Arrangement` entries, updates only the two affected `TileItemViewModel.Image` values (not a full refresh), then rebuilds `ResultImage`.

**Undo/Redo:** `_undoStack`/`_redoStack` (`Stack<int[]>`) hold clones of `Arrangement`. `PushUndo()` is called before every mutating operation (auto-restore, reset, swap). Redo stack is cleared on any new mutation.

**Drag-and-drop:** `MainWindow` registers `DragDrop` handlers; dropped file path is forwarded to `vm.LoadFile(path)`. A `DropOverlay` XAML element is shown/hidden during drag.

**Shuffle algorithm:** `ShufflePermutation` uses Fisher-Yates with the **mulberry32** PRNG — this is a best-guess replacement for the original JS tool's RNG. The original HTML hardcodes the permutation for seed 149 without exposing its algorithm, so results for other seeds may differ from the original tool.

## Key XAML notes

- `AvaloniaUseCompiledBindingsByDefault=true` is set — all bindings are compiled. Each AXAML file needs `x:DataType` on the root or data template element.
- Pixel-perfect tile rendering: use `RenderOptions.BitmapInterpolationMode="None"` (Avalonia enum, not WPF's `NearestNeighbor`).
- `NumericUpDown.Value` is `decimal?` — ViewModel toolbar properties (`TileSize`, `Cols`, `Rows`, `Seed`) are `decimal`; cast to `int` when calling model methods.

## Implementation plan status

See `PLAN.md` for the full plan. Completed:
- **Этап 1** — project scaffold
- **Этап 2** — texture loading, tile slicing, display (Original / Result / Tile grid panels)
- **Этап 3** — seed permutation (`ShufflePermutation`, AutoRestore command)
- **Этап 4** — manual tile swap (click-to-select, click-to-swap in tile grid)
- **Этап 5** — save / save-as
- **Этап 6** — undo/redo (Ctrl+Z/Y via keyboard bindings in AXAML)

Remaining: **Этап 7** (UI polish) — dark theme, zoom, additional error handling. Drag-and-drop file onto window is already done.
