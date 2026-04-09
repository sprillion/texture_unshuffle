using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TextureUnshuffle.Models;
using TextureUnshuffle.Models.Solvers;
using TextureUnshuffle.Models.UvAnalysis;
using TextureUnshuffle.Services;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace TextureUnshuffle.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private TileGrid? _grid;
    private int _selectedPos = -1;

    private readonly record struct GridState(int[] Arrangement, int[] Rotations, int[] Flips);
    private readonly Stack<GridState> _undoStack = new();
    private readonly Stack<GridState> _redoStack = new();

    // Design-time constructor
    public MainWindowViewModel() : this(null!) { }

    public MainWindowViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGridLoaded))]
    [NotifyPropertyChangedFor(nameof(LeftPanelImage))]
    private AvaloniaBitmap? _originalImage;
    [ObservableProperty] private AvaloniaBitmap? _resultImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftPanelImage))]
    [NotifyPropertyChangedFor(nameof(LeftPanelLabel))]
    private AvaloniaBitmap? _referenceImage;

    public AvaloniaBitmap? LeftPanelImage => ReferenceImage ?? OriginalImage;
    public string LeftPanelLabel => ReferenceImage is not null ? "Эталон" : "Оригинал";

    public bool IsGridLoaded => _grid is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TileGridWidth))]
    private decimal _tileSize = 16;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TileGridWidth))]
    private decimal _cols = 8;

    [ObservableProperty] private decimal _rows = 8;
    [ObservableProperty] private decimal _seed = 149;
    [ObservableProperty] private string _status = "Откройте файл текстуры";
    [ObservableProperty] private bool _isTileSelected = false;
    [ObservableProperty] private ObservableCollection<TileItemViewModel> _tileItems = new();

    // Edge Matching
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EdgeMatchingConfidenceText))]
    private float _edgeMatchingConfidence = 0f;

    [ObservableProperty] private int _edgeMatchingProgress = 0;
    [ObservableProperty] private bool _isEdgeMatchingRunning = false;

    public string EdgeMatchingConfidenceText =>
        EdgeMatchingConfidence > 0f ? $"{EdgeMatchingConfidence:P0}" : "";

    private CancellationTokenSource? _edgeMatchingCts;

    // UV Solver
    private TileAdjacencyGraph? _adjacencyGraph;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UvModelFileName))]
    [NotifyPropertyChangedFor(nameof(UvConstraintCount))]
    [NotifyPropertyChangedFor(nameof(IsModelLoaded))]
    private string? _uvModelPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UvSolverConfidenceText))]
    private float _uvSolverConfidence = 0f;

    [ObservableProperty] private int _uvSolverProgress = 0;
    [ObservableProperty] private bool _isUvSolverRunning = false;

    public bool IsModelLoaded => _uvModelPath is not null;
    public string UvModelFileName => _uvModelPath is null ? "" : System.IO.Path.GetFileName(_uvModelPath);
    public string UvConstraintCount => _adjacencyGraph is null ? "" : $"{_adjacencyGraph.ConstraintCount} ограничений";
    public string UvSolverConfidenceText =>
        UvSolverConfidence > 0f ? $"{UvSolverConfidence:P0}" : "";

    private CancellationTokenSource? _uvSolverCts;

    // Reference Texture Matching
    private TileGrid? _refGrid;
    private CancellationTokenSource? _refMatchingCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefFileName))]
    [NotifyPropertyChangedFor(nameof(IsRefLoaded))]
    private string? _refFilePath;

    [ObservableProperty] private int   _refMatchingProgress  = 0;
    [ObservableProperty] private bool  _isRefMatchingRunning = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefMatchingConfidenceText))]
    private float _refMatchingConfidence = 0f;

    public bool   IsRefLoaded               => _refFilePath is not null;
    public string RefFileName               => _refFilePath is null ? "" : System.IO.Path.GetFileName(_refFilePath);
    public string RefMatchingConfidenceText => _refMatchingConfidence > 0f ? $"{_refMatchingConfidence:P0}" : "";

    private const int TileDisplaySize = 32;
    private const int TileBorderThickness = 2;

    public int TileGridWidth => (int)Cols * (TileDisplaySize + TileBorderThickness * 2) + 4;

    // ── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenFile()
    {
        var path = await _dialogService.OpenTextureFileAsync();
        if (path is null) return;
        LoadFromPath(path);
    }

    [RelayCommand]
    private void ApplyGrid()
    {
        if (_grid is null) return;
        LoadFromPath(_grid.FilePath);
    }

    [RelayCommand]
    private void AutoRestore()
    {
        if (_grid is null) return;
        try
        {
            ClearSelection();
            PushUndo();
            int seed = (int)Seed;
            int n = _grid.Cols * _grid.Rows;
            int[] perm = ShufflePermutation.GeneratePermutation(seed, n);
            int[] inv = ShufflePermutation.GetInverse(perm);
            _grid.Arrangement = inv;
            ResultImage = BitmapConverter.ToAvaloniaBitmap(_grid.BuildResultBitmap());
            RefreshTileItems();
            Status = $"Авто-восстановление применено (seed {seed})";
        }
        catch (Exception ex)
        {
            Status = $"Ошибка: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RunEdgeMatching()
    {
        if (_grid is null) return;
        _edgeMatchingCts = new CancellationTokenSource();
        IsEdgeMatchingRunning = true;
        EdgeMatchingProgress = 0;
        EdgeMatchingConfidence = 0f;
        Status = "Edge Matching: запуск...";
        try
        {
            ClearSelection();
            PushUndo();
            var solver = new EdgeMatchingSolver();
            var progress = new Progress<int>(v =>
            {
                EdgeMatchingProgress = v;
                if (v < 40)
                    Status = $"Edge Matching: матрица совместимости {v * 100 / 40}%";
                else
                    Status = $"Edge Matching: перебор вариантов {(v - 40) * 100 / 60}%";
            });
            var result = await solver.SolveAsync(
                _grid.Tiles, _grid.Cols, _grid.Rows,
                progress, _edgeMatchingCts.Token);

            _grid.Arrangement     = result.Arrangement;
            _grid.Rotations       = result.Rotations ?? new int[_grid.Cols * _grid.Rows];
            _grid.Flips           = new int[_grid.Cols * _grid.Rows];
            EdgeMatchingConfidence = result.Confidence;
            ResultImage = BitmapConverter.ToAvaloniaBitmap(_grid.BuildResultBitmap());
            RefreshTileItems();
            Status = $"Edge Matching завершён. Confidence: {result.Confidence:P0}";
        }
        catch (OperationCanceledException)
        {
            Status = "Edge Matching отменён";
        }
        catch (Exception ex)
        {
            Status = $"Ошибка Edge Matching: {ex.Message}";
        }
        finally
        {
            IsEdgeMatchingRunning = false;
            EdgeMatchingProgress = 0;
            _edgeMatchingCts?.Dispose();
            _edgeMatchingCts = null;
        }
    }

    [RelayCommand]
    private void CancelEdgeMatching()
    {
        _edgeMatchingCts?.Cancel();
    }

    [RelayCommand]
    private async Task LoadModel()
    {
        var path = await _dialogService.OpenModelFileAsync();
        if (path is null) return;
        try
        {
            Status = $"Загрузка модели: {System.IO.Path.GetFileName(path)}...";
            var triangles = await Task.Run(() => ModelLoader.LoadUvTriangles(path));
            if (_grid is null)
            {
                Status = "Сначала загрузите текстуру";
                return;
            }
            _adjacencyGraph = UvTileMapper.BuildAdjacencyGraph(triangles, _grid.Cols, _grid.Rows);
            UvModelPath = path;
            Status = $"Модель загружена: {System.IO.Path.GetFileName(path)}  ({_adjacencyGraph.ConstraintCount} ограничений из {triangles.Count} треугольников)";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Ошибка загрузки модели", ex.Message);
            Status = $"Ошибка: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RunUvSolver()
    {
        if (_grid is null || _adjacencyGraph is null) return;
        _uvSolverCts = new CancellationTokenSource();
        IsUvSolverRunning = true;
        UvSolverProgress = 0;
        UvSolverConfidence = 0f;
        Status = "UV-солвер: запуск...";
        try
        {
            ClearSelection();
            PushUndo();
            var solver = new UvConstrainedSolver(_adjacencyGraph);
            var progress = new Progress<int>(v =>
            {
                UvSolverProgress = v;
                if (v < 60)
                    Status = $"UV-солвер: матрица {v * 100 / 60}%";
                else
                    Status = $"UV-солвер: расстановка {(v - 60) * 100 / 40}%";
            });
            var result = await solver.SolveAsync(
                _grid.Tiles, _grid.Cols, _grid.Rows,
                progress, _uvSolverCts.Token);

            _grid.Arrangement  = result.Arrangement;
            _grid.Rotations    = result.Rotations ?? new int[_grid.Cols * _grid.Rows];
            _grid.Flips        = new int[_grid.Cols * _grid.Rows];
            UvSolverConfidence = result.Confidence;
            ResultImage = BitmapConverter.ToAvaloniaBitmap(_grid.BuildResultBitmap());
            RefreshTileItems();
            Status = $"UV-солвер завершён. Confidence: {result.Confidence:P0}  (ограничений: {_adjacencyGraph.ConstraintCount})";
        }
        catch (OperationCanceledException)
        {
            Status = "UV-солвер отменён";
        }
        catch (Exception ex)
        {
            Status = $"Ошибка UV-солвера: {ex.Message}";
        }
        finally
        {
            IsUvSolverRunning = false;
            UvSolverProgress = 0;
            _uvSolverCts?.Dispose();
            _uvSolverCts = null;
        }
    }

    [RelayCommand]
    private void CancelUvSolver()
    {
        _uvSolverCts?.Cancel();
    }

    [RelayCommand]
    private async Task Save()
    {
        if (_grid is null) return;
        var fileName = System.IO.Path.GetFileName(_grid.FilePath);
        bool confirmed = await _dialogService.ConfirmAsync(
            "Перезапись файла",
            $"Перезаписать исходный файл?\n\n{fileName}");
        if (!confirmed) return;
        try
        {
            _grid.SaveAs(_grid.FilePath);
            Status = $"Сохранено: {fileName}";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Ошибка сохранения", ex.Message);
            Status = $"Ошибка сохранения: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveAs()
    {
        if (_grid is null) return;
        var path = await _dialogService.SaveFileAsync(System.IO.Path.GetFileName(_grid.FilePath));
        if (path is null) return;
        try
        {
            _grid.SaveAs(path);
            Status = $"Сохранено как: {System.IO.Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Ошибка сохранения", ex.Message);
            Status = $"Ошибка сохранения: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Reset()
    {
        if (_grid is null) return;
        ClearSelection();
        PushUndo();
        int n = _grid.Cols * _grid.Rows;
        _grid.Arrangement = Enumerable.Range(0, n).ToArray();
        _grid.Rotations   = new int[n];
        _grid.Flips       = new int[n];
        ResultImage = BitmapConverter.ToAvaloniaBitmap(_grid.BuildResultBitmap());
        RefreshTileItems();
        Status = "Сброс к исходному состоянию";
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_grid is null || _undoStack.Count == 0) return;
        ClearSelection();
        _redoStack.Push(new GridState(
            (int[])_grid.Arrangement.Clone(),
            (int[])_grid.Rotations.Clone(),
            (int[])_grid.Flips.Clone()));
        var state = _undoStack.Pop();
        _grid.Arrangement = state.Arrangement;
        _grid.Rotations   = state.Rotations;
        _grid.Flips       = state.Flips;
        ResultImage = BitmapConverter.ToAvaloniaBitmap(_grid.BuildResultBitmap());
        RefreshTileItems();
        NotifyUndoRedoChanged();
        Status = $"Отменено. Undo: {_undoStack.Count}, Redo: {_redoStack.Count}";
    }
    private bool CanUndo() => _undoStack.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_grid is null || _redoStack.Count == 0) return;
        ClearSelection();
        _undoStack.Push(new GridState(
            (int[])_grid.Arrangement.Clone(),
            (int[])_grid.Rotations.Clone(),
            (int[])_grid.Flips.Clone()));
        var state = _redoStack.Pop();
        _grid.Arrangement = state.Arrangement;
        _grid.Rotations   = state.Rotations;
        _grid.Flips       = state.Flips;
        ResultImage = BitmapConverter.ToAvaloniaBitmap(_grid.BuildResultBitmap());
        RefreshTileItems();
        NotifyUndoRedoChanged();
        Status = $"Повторено. Undo: {_undoStack.Count}, Redo: {_redoStack.Count}";
    }
    private bool CanRedo() => _redoStack.Count > 0;

    [RelayCommand]
    private void SelectTile(int pos)
    {
        if (_grid is null) return;

        if (_selectedPos == -1)
        {
            // First click — select
            _selectedPos = pos;
            TileItems[pos].IsSelected = true;
            IsTileSelected = true;
            Status = $"Выбран тайл на позиции {pos}. Кликните другой для замены.";
        }
        else if (_selectedPos == pos)
        {
            // Click same tile — deselect
            ClearSelection();
            Status = "Выделение снято";
        }
        else
        {
            // Second click on different tile — swap
            int a = _selectedPos;
            int b = pos;
            PushUndo();
            (_grid.Arrangement[a], _grid.Arrangement[b]) = (_grid.Arrangement[b], _grid.Arrangement[a]);
            (_grid.Rotations[a],   _grid.Rotations[b])   = (_grid.Rotations[b],   _grid.Rotations[a]);
            (_grid.Flips[a],       _grid.Flips[b])       = (_grid.Flips[b],       _grid.Flips[a]);

            // Update only the two swapped tile images
            TileItems[a].IsSelected = false;
            TileItems[a].Image = GetTilePreview(a);
            TileItems[b].Image = GetTilePreview(b);

            ResultImage = BitmapConverter.ToAvaloniaBitmap(_grid.BuildResultBitmap());
            _selectedPos = -1;
            IsTileSelected = false;
            Status = $"Поменяны позиции {a} и {b}";
        }
    }

    [RelayCommand]
    private void RotateTileCw()
    {
        if (_grid is null || _selectedPos < 0) return;
        PushUndo();
        _grid.Rotations[_selectedPos] = (_grid.Rotations[_selectedPos] + 1) % 4;
        TileItems[_selectedPos].Image = GetTilePreview(_selectedPos);
        ResultImage = BitmapConverter.ToAvaloniaBitmap(_grid.BuildResultBitmap());
        Status = $"Тайл {_selectedPos} повёрнут по часовой";
    }

    [RelayCommand]
    private void RotateTileCcw()
    {
        if (_grid is null || _selectedPos < 0) return;
        PushUndo();
        _grid.Rotations[_selectedPos] = (_grid.Rotations[_selectedPos] + 3) % 4;
        TileItems[_selectedPos].Image = GetTilePreview(_selectedPos);
        ResultImage = BitmapConverter.ToAvaloniaBitmap(_grid.BuildResultBitmap());
        Status = $"Тайл {_selectedPos} повёрнут против часовой";
    }

    [RelayCommand]
    private void FlipTileH()
    {
        if (_grid is null || _selectedPos < 0) return;
        PushUndo();
        _grid.Flips[_selectedPos] ^= 1; // toggle bit 0
        TileItems[_selectedPos].Image = GetTilePreview(_selectedPos);
        ResultImage = BitmapConverter.ToAvaloniaBitmap(_grid.BuildResultBitmap());
        Status = $"Тайл {_selectedPos}: отражение по горизонтали";
    }

    [RelayCommand]
    private void FlipTileV()
    {
        if (_grid is null || _selectedPos < 0) return;
        PushUndo();
        _grid.Flips[_selectedPos] ^= 2; // toggle bit 1
        TileItems[_selectedPos].Image = GetTilePreview(_selectedPos);
        ResultImage = BitmapConverter.ToAvaloniaBitmap(_grid.BuildResultBitmap());
        Status = $"Тайл {_selectedPos}: отражение по вертикали";
    }

    [RelayCommand]
    private async Task LoadRefTexture()
    {
        var path = await _dialogService.OpenTextureFileAsync();
        if (path is null) return;
        if (_grid is null)
        {
            Status = "Сначала загрузите исходную текстуру";
            return;
        }
        try
        {
            using var codec = SkiaSharp.SKCodec.Create(path)
                ?? throw new Exception("Не удалось декодировать эталонную текстуру");
            int refTileSize = codec.Info.Width / _grid.Cols;
            if (refTileSize < 1)
                throw new Exception($"Слишком маленькое разрешение эталона ({codec.Info.Width}px) для {_grid.Cols} колонок");
            _refGrid    = TileGrid.Load(path, refTileSize, _grid.Cols, _grid.Rows);
            RefFilePath = path;
            ReferenceImage = BitmapConverter.ToAvaloniaBitmap(_refGrid.OriginalBitmap);
            Status = $"Эталон загружен: {System.IO.Path.GetFileName(path)}  (тайл {refTileSize}px)";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Ошибка загрузки эталона", ex.Message);
            Status = $"Ошибка: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RunRefMatching()
    {
        if (_grid is null || _refGrid is null) return;
        _refMatchingCts      = new CancellationTokenSource();
        IsRefMatchingRunning = true;
        RefMatchingProgress  = 0;
        RefMatchingConfidence = 0f;
        Status = "Reference Matching: запуск...";
        try
        {
            ClearSelection();
            PushUndo();
            var solver   = new ReferenceTextureSolver();
            var progress = new Progress<int>(v =>
            {
                RefMatchingProgress = v;
                Status = v < 80
                    ? $"Reference Matching: матрица затрат {v * 100 / 80}%"
                    : $"Reference Matching: назначение {(v - 80) * 100 / 20}%";
            });
            var result = await solver.SolveAsync(
                _grid.Tiles, _refGrid.Tiles, _grid.Cols, _grid.Rows,
                progress, _refMatchingCts.Token);

            _grid.Arrangement     = result.Arrangement;
            _grid.Rotations       = result.Rotations ?? new int[_grid.Cols * _grid.Rows];
            _grid.Flips           = new int[_grid.Cols * _grid.Rows];
            RefMatchingConfidence = result.Confidence;
            ResultImage = BitmapConverter.ToAvaloniaBitmap(_grid.BuildResultBitmap());
            RefreshTileItems();
            Status = "Reference Matching завершён";
        }
        catch (OperationCanceledException)
        {
            Status = "Reference Matching отменён";
        }
        catch (Exception ex)
        {
            Status = $"Ошибка Reference Matching: {ex.Message}";
        }
        finally
        {
            IsRefMatchingRunning = false;
            RefMatchingProgress  = 0;
            _refMatchingCts?.Dispose();
            _refMatchingCts = null;
        }
    }

    [RelayCommand]
    private void CancelRefMatching()
    {
        _refMatchingCts?.Cancel();
    }

    public void LoadFile(string path) => LoadFromPath(path);

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void PushUndo()
    {
        if (_grid is null) return;
        _undoStack.Push(new GridState(
            (int[])_grid.Arrangement.Clone(),
            (int[])_grid.Rotations.Clone(),
            (int[])_grid.Flips.Clone()));
        _redoStack.Clear();
        NotifyUndoRedoChanged();
    }

    private void NotifyUndoRedoChanged()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void ClearSelection()
    {
        if (_selectedPos >= 0 && _selectedPos < TileItems.Count)
            TileItems[_selectedPos].IsSelected = false;
        _selectedPos = -1;
        IsTileSelected = false;
    }

    private async void LoadFromPath(string path)
    {
        try
        {
            ClearSelection();
            _undoStack.Clear();
            _redoStack.Clear();
            NotifyUndoRedoChanged();
            // Clear ref grid — cols/rows may have changed
            _refGrid       = null;
            RefFilePath    = null;
            ReferenceImage = null;
            _grid = TileGrid.Load(path, (int)TileSize, (int)Cols, (int)Rows);
            OriginalImage = BitmapConverter.ToAvaloniaBitmap(_grid.OriginalBitmap);
            ResultImage = BitmapConverter.ToAvaloniaBitmap(_grid.BuildResultBitmap());
            RefreshTileItems();
            Status = $"Загружено: {System.IO.Path.GetFileName(path)}  ({_grid.Cols}×{_grid.Rows}, {_grid.TileSize}px)";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Ошибка загрузки", ex.Message);
            Status = $"Ошибка: {ex.Message}";
        }
    }

    private void RefreshTileItems()
    {
        if (_grid is null) return;
        _selectedPos = -1;
        TileItems.Clear();
        for (int pos = 0; pos < _grid.Arrangement.Length; pos++)
        {
            TileItems.Add(new TileItemViewModel(
                GetTilePreview(pos),
                pos,
                SelectTileCommand));
        }
    }

    private AvaloniaBitmap GetTilePreview(int pos)
    {
        int tileIdx = _grid!.Arrangement[pos];
        int rot     = _grid.Rotations[pos];
        int flip    = _grid.Flips[pos];

        SkiaSharp.SKBitmap? rotated = null;
        SkiaSharp.SKBitmap? flipped = null;
        try
        {
            var current = _grid.Tiles[tileIdx];
            if (rot  != 0) { rotated = TileGrid.RotateBitmap(current, rot); current = rotated; }
            if (flip != 0) { flipped = TileGrid.FlipBitmap(current, flip);  current = flipped; }
            return BitmapConverter.ToAvaloniaBitmap(current);
        }
        finally
        {
            rotated?.Dispose();
            flipped?.Dispose();
        }
    }
}
