using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using TextureUnshuffle.Models;
using TextureUnshuffle.Services;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace TextureUnshuffle.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private TileGrid? _grid;
    private int _selectedPos = -1;

    private readonly Stack<int[]> _undoStack = new();
    private readonly Stack<int[]> _redoStack = new();

    // Design-time constructor
    public MainWindowViewModel() : this(null!) { }

    public MainWindowViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGridLoaded))]
    private AvaloniaBitmap? _originalImage;
    [ObservableProperty] private AvaloniaBitmap? _resultImage;

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
    [ObservableProperty] private ObservableCollection<TileItemViewModel> _tileItems = new();

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
    private async Task Save()
    {
        if (_grid is null) return;
        try
        {
            _grid.SaveAs(_grid.FilePath);
            Status = $"Сохранено: {System.IO.Path.GetFileName(_grid.FilePath)}";
        }
        catch (Exception ex)
        {
            Status = $"Ошибка сохранения: {ex.Message}";
        }
        await Task.CompletedTask;
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
        ResultImage = BitmapConverter.ToAvaloniaBitmap(_grid.BuildResultBitmap());
        RefreshTileItems();
        Status = "Сброс к исходному состоянию";
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_grid is null || _undoStack.Count == 0) return;
        ClearSelection();
        _redoStack.Push((int[])_grid.Arrangement.Clone());
        _grid.Arrangement = _undoStack.Pop();
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
        _undoStack.Push((int[])_grid.Arrangement.Clone());
        _grid.Arrangement = _redoStack.Pop();
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

            // Update only the two swapped tile images
            TileItems[a].IsSelected = false;
            TileItems[a].Image = BitmapConverter.ToAvaloniaBitmap(_grid.Tiles[_grid.Arrangement[a]]);
            TileItems[b].Image = BitmapConverter.ToAvaloniaBitmap(_grid.Tiles[_grid.Arrangement[b]]);

            ResultImage = BitmapConverter.ToAvaloniaBitmap(_grid.BuildResultBitmap());
            _selectedPos = -1;
            Status = $"Поменяны позиции {a} и {b}";
        }
    }

    public void LoadFile(string path) => LoadFromPath(path);

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void PushUndo()
    {
        if (_grid is null) return;
        _undoStack.Push((int[])_grid.Arrangement.Clone());
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
    }

    private void LoadFromPath(string path)
    {
        try
        {
            ClearSelection();
            _undoStack.Clear();
            _redoStack.Clear();
            NotifyUndoRedoChanged();
            _grid = TileGrid.Load(path, (int)TileSize, (int)Cols, (int)Rows);
            OriginalImage = BitmapConverter.ToAvaloniaBitmap(_grid.OriginalBitmap);
            ResultImage = BitmapConverter.ToAvaloniaBitmap(_grid.BuildResultBitmap());
            RefreshTileItems();
            Status = $"Загружено: {System.IO.Path.GetFileName(path)}  ({_grid.Cols}×{_grid.Rows}, {_grid.TileSize}px)";
        }
        catch (Exception ex)
        {
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
            int tileIdx = _grid.Arrangement[pos];
            TileItems.Add(new TileItemViewModel(
                BitmapConverter.ToAvaloniaBitmap(_grid.Tiles[tileIdx]),
                pos,
                SelectTileCommand));
        }
    }
}
