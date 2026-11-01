using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System.Numerics;

namespace Sidebar;

public partial class MainWindow
{
    private int puzzleCompletionGeneration;
    private bool puzzleWasSolved;
    private ImageSource? puzzleCompletionSource;
    private double puzzleCompletionSize;

    private void UpdatePuzzleCompletion(double size, double cell)
    {
        bool newlySolved = picturePuzzle.Solved && !puzzleWasSolved;
        puzzleWasSolved = picturePuzzle.Solved;
        if (!picturePuzzle.Solved || puzzlePicture is null)
        {
            puzzleCompletionGeneration++; puzzleCompletionSource = null;
            PuzzleMergeLayer.Children.Clear(); PuzzleMergeLayer.Visibility = Visibility.Collapsed;
            PuzzleCompletedImage.Visibility = Visibility.Collapsed; PuzzleCompletedImage.Source = null;
            PuzzleBoard.Visibility = Visibility.Visible; return;
        }
        if (ReferenceEquals(puzzleCompletionSource, puzzlePicture) && Math.Abs(puzzleCompletionSize - size) < 0.01) return;
        int generation = ++puzzleCompletionGeneration;
        puzzleCompletionSource = puzzlePicture; puzzleCompletionSize = size;
        PuzzleCompletedImage.Source = puzzlePicture;
        PuzzleMergeLayer.Children.Clear(); PuzzleMergeLayer.Visibility = Visibility.Collapsed;
        if (newlySolved && Root.IsLoaded && windowsAppearance.AnimationsEnabled)
        { PuzzleCompletedImage.Visibility = Visibility.Collapsed; PuzzleBoard.Visibility = Visibility.Visible; _ = MergePuzzleAsync(generation, size, cell); }
        else ShowCompletedPuzzle();
    }

    private bool CurrentPuzzleMerge(int generation)
        => generation == puzzleCompletionGeneration && !sidebarClosed && !closing && picturePuzzle.Solved;

    private void ShowCompletedPuzzle()
    {
        PuzzleCompletedImage.Visibility = Visibility.Visible; PuzzleBoard.Visibility = Visibility.Collapsed;
        PuzzleMergeLayer.Visibility = Visibility.Collapsed; PuzzleMergeLayer.Children.Clear();
    }

    private async Task MergePuzzleAsync(int generation, double size, double cell)
    {
        try
        {
            await Task.Delay(160); // Finish the last tile's slide before closing the gutters.
            if (!CurrentPuzzleMerge(generation)) return;
            double inner = Math.Max(6, cell - 2), target = size / 3;
            for (int tile = 1; tile <= 9; tile++)
            {
                int col = (tile - 1) % 3, row = (tile - 1) / 3;
                double x = col * (cell + 2) + 1, y = row * (cell + 2) + 1;
                var piece = PuzzleImageTile(tile, inner, false);
                Canvas.SetLeft(piece, x); Canvas.SetTop(piece, y); PuzzleMergeLayer.Children.Add(piece);
                ElementCompositionPreview.SetIsTranslationEnabled(piece, true);
                var visual = ElementCompositionPreview.GetElementVisual(piece); var compositor = visual.Compositor;
                var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(.2f, 0), new Vector2(0, 1));
                var scale = compositor.CreateVector3KeyFrameAnimation();
                scale.InsertKeyFrame(0, Vector3.One); scale.InsertKeyFrame(1, new Vector3((float)(target / inner), (float)(target / inner), 1), ease);
                scale.Duration = TimeSpan.FromMilliseconds(280); visual.StartAnimation("Scale", scale);
                var move = compositor.CreateVector3KeyFrameAnimation();
                move.InsertKeyFrame(0, Vector3.Zero); move.InsertKeyFrame(1, new Vector3((float)(col * target - x), (float)(row * target - y), 0), ease);
                move.Duration = scale.Duration; visual.StartAnimation("Translation", move);
                if (tile == 9)
                {
                    var fade = compositor.CreateScalarKeyFrameAnimation(); fade.InsertKeyFrame(0, 0); fade.InsertKeyFrame(1, 1);
                    fade.Duration = scale.Duration; visual.StartAnimation("Opacity", fade);
                }
            }
            PuzzleMergeLayer.Visibility = Visibility.Visible; PuzzleBoard.Visibility = Visibility.Collapsed;
            await Task.Delay(340);
            if (CurrentPuzzleMerge(generation)) ShowCompletedPuzzle();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        { if (CurrentPuzzleMerge(generation)) ShowCompletedPuzzle(); }
    }
}
