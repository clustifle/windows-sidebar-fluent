#if THEME_REGRESSION
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
namespace Sidebar;
public partial class MainWindow
{
    internal async void RunPuzzleRegression()
    {
        var original = state;
        int[] originalTiles = picturePuzzle.Tiles.ToArray(); int originalMoves = picturePuzzle.Moves;
        var report = System.IO.Path.Combine(AppContext.BaseDirectory, "puzzle-test-result.txt");
        System.IO.File.WriteAllText(report, "");
        try
        {
            VerifyFeedThumbnails();
            await VerifyThumbnailDownloads();
            System.IO.File.AppendAllText(report, "PASS: RSS media thumbnail/content, image enclosure, Atom, relative HTML images, xml:base, tracker filtering, invalid URLs, text fallback and DTD rejection.\n");
            state = System.Text.Json.JsonSerializer.Deserialize<SidebarState>(System.Text.Json.JsonSerializer.Serialize(original))!;
            state.PicturePuzzle = true; state.AutoHide = false; state.PageIndex = 0;
            state.GadgetOrder.Remove("Picture puzzle"); state.GadgetOrder.Insert(0, "Picture puzzle");
            state.LonghornGadgetOrder.Remove("Picture puzzle"); state.LonghornGadgetOrder.Insert(0, "Picture puzzle");
            foreach (var scenario in new[] { (Rail: false, Width: 240, Theme: "Light"), (Rail: false, Width: 320, Theme: "Dark"), (Rail: true, Width: 168, Theme: "Light"), (Rail: true, Width: 228, Theme: "Dark") })
            {
                state.LonghornMode = scenario.Rail; state.Width = scenario.Rail ? 240 : scenario.Width; state.LonghornWidth = scenario.Rail ? scenario.Width : 168;
                state.Theme = scenario.Theme; state.PuzzleImageMode = "Sample"; state.PuzzleNumberLabels = false;
                state.CardStyles["Picture puzzle"] = new();
                picturePuzzle.Restore([1, 2, 3, 4, 5, 6, 0, 7, 8], 0);
                await UpdateLonghornAsync(); GadgetScroller.ChangeView(null, 0, null); await Task.Delay(500); RenderPuzzle();
                for (int attempt = 0; attempt < 30 && !puzzleSampleReady; attempt++) await Task.Delay(100);
                if (!puzzleSampleReady || puzzlePicture is not SvgImageSource || puzzleImageError is not null) throw new Exception("Built-in picture did not decode: " + puzzleImageError);
                await Task.Delay(200);
                if (puzzleButtons.Count != 8 || PuzzleBoard.Children.Count != 9 || Math.Abs(PuzzleBoard.ActualWidth - PuzzleBoard.ActualHeight) > 0.5 || PuzzleBoard.ActualWidth > PuzzleFrame.ActualWidth - 5) throw new Exception("Board exceeds its card or is not square");
                foreach (var pair in puzzleButtons)
                {
                    var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(pair.Value);
                    var pixels = await bitmap.GetPixelsAsync(); using var reader = Windows.Storage.Streams.DataReader.FromBuffer(pixels);
                    var bytes = new byte[pixels.Length]; reader.ReadBytes(bytes); int colored = 0;
                    for (int offset = 0; offset < bytes.Length; offset += 4)
                    { int low = Math.Min(bytes[offset], Math.Min(bytes[offset + 1], bytes[offset + 2])); int high = Math.Max(bytes[offset], Math.Max(bytes[offset + 1], bytes[offset + 2])); if (bytes[offset + 3] > 128 && high - low > 25) colored++; }
                    if (colored < bitmap.PixelWidth * bitmap.PixelHeight * 0.1) throw new Exception($"Tile {pair.Key} has no visible picture crop");
                }
                double height = PuzzleGadget.Height;
                var buttons = puzzleButtons.Values.ToArray();
                if (!picturePuzzle.Move(7)) throw new Exception("Valid move was rejected"); RenderPuzzle();
                if (!puzzleButtons.Values.SequenceEqual(buttons) || PuzzleGadget.Height != height || picturePuzzle.Moves != 1) throw new Exception("Moving a tile rebuilt controls or resized its card");
                await CaptureRailPreviewAsync($"puzzle-{(scenario.Rail ? "rail" : "normal")}-{scenario.Theme.ToLowerInvariant()}.png", PuzzleGadget);
                state.PuzzleImageMode = "Numbers"; RenderPuzzle();
                if (puzzlePicture is not null || PuzzlePreview.IsEnabled) throw new Exception("Numbered tiles retained a picture preview");
                state.PuzzleImageMode = "Sample"; state.PuzzleNumberLabels = true; RenderPuzzle();
                if (picturePuzzle.Moves != 1 || !picturePuzzle.Tiles.SequenceEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 0, 8 })) throw new Exception("Changing picture mode reset game progress");
                picturePuzzle.Move(8); state.PuzzleShowMoves = false; RenderPuzzle();
                await Task.Delay(650);
                if (!picturePuzzle.Solved || PuzzleBoard.Visibility != Visibility.Collapsed || PuzzleCompletedImage.Visibility != Visibility.Visible || !ReferenceEquals(PuzzleCompletedImage.Source, puzzlePicture) || PuzzleShuffle.Content as string != "Play again" || PuzzleStatus.Visibility != Visibility.Visible) throw new Exception("Single completed picture or completion feedback is missing");
                await Task.Delay(250);
                await CaptureRailPreviewAsync($"puzzle-completed-{scenario.Width}.png", PuzzleGadget);
                picturePuzzle.Shuffle(); RenderPuzzle();
                if (PuzzleBoard.Visibility != Visibility.Visible || PuzzleCompletedImage.Visibility != Visibility.Collapsed || PuzzleCompletedImage.Source is not null) throw new Exception("Play again did not restore the tile board");
                state.PuzzleImageMode = "Custom"; state.PuzzlePicture = System.IO.Path.Combine(AppContext.BaseDirectory, "missing-picture.png"); RenderPuzzle();
                if (puzzlePicture is not SvgImageSource || puzzleImageError is null) throw new Exception("Unavailable custom picture did not fall back to the sample");
                state.PuzzleImageMode = "Sample"; state.PuzzleShowMoves = true; state.PuzzleNumberLabels = false;
                state.CardStyles["Picture puzzle"].Height = 240; state.CardStyles["Picture puzzle"].Padding = 24;
                ApplyState(); await Task.Delay(250); RenderPuzzle(); await Task.Delay(100);
                if (PuzzleGadget.Height != 240 || PuzzleBoard.Width > PuzzleFrame.ActualWidth - 5) throw new Exception("Locked height or custom padding overflowed");
                System.IO.File.AppendAllText(report, $"PASS: rail={scenario.Rail}, width={scenario.Width}, theme={scenario.Theme}: eight colored image crops, square bounds, tile reuse, modes preserve progress, one continuous completed image, restart, missing-file fallback, fixed height and padding.\n");
            }
            foreach (var sample in PuzzleSamples.All)
            {
                SelectPuzzleSample(sample.Id);
                for (int attempt = 0; attempt < 30 && !puzzleSampleReady; attempt++) await Task.Delay(100);
                if (!puzzleSampleReady || puzzleImageError is not null) throw new Exception("Nature sample failed: " + sample.Name);
                if (PuzzleSamples.Different(sample.Id).Id == sample.Id) throw new Exception("Shuffle repeated the same sample");
            }
            picturePuzzle.Restore([1,2,3,4,5,6,7,0,8], 0); RenderPuzzle(); MovePuzzleTile(8);
            await Task.Delay(210);
            if (windowsAppearance.AnimationsEnabled && PuzzleMergeLayer.Visibility != Visibility.Visible) throw new Exception("Merge animation did not start");
            PuzzleShuffle_Click(this, new RoutedEventArgs()); await Task.Delay(650);
            if (PuzzleBoard.Visibility != Visibility.Visible || PuzzleCompletedImage.Visibility != Visibility.Collapsed || PuzzleMergeLayer.Children.Count != 0) throw new Exception("Shuffle failed to cancel completion animation");
            System.IO.File.AppendAllText(report, "PASS: all six nature samples decode, shuffle avoids repeat, merge animation starts and cancels safely on new game.\n");
            await VerifyFeedCardAsync();
            System.IO.File.AppendAllText(report, "PASS: compact thumbnail row fits the card, mixed text-only headlines and image toggle preserve all articles.\n");
        }
        catch (Exception ex) { System.IO.File.AppendAllText(report, "FAIL: " + ex); }
        finally
        {
            puzzlePopup?.Hide(); state = original; picturePuzzle.Restore(originalTiles, originalMoves); loadedPuzzleKey = null;
            await UpdateLonghornAsync(); RenderPuzzle(); Save(); Close();
        }
    }
}
#endif
