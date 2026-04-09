using SkiaSharp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TextureUnshuffle.Models.Solvers;

public interface IArrangementSolver
{
    Task<SolverResult> SolveAsync(
        SKBitmap[] tiles, int cols, int rows,
        IProgress<int>? progress = null,
        CancellationToken ct = default);
}
