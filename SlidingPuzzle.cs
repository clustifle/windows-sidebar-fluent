namespace Sidebar;

internal sealed class SlidingPuzzle
{
    public int[] Tiles { get; private set; } = [1, 2, 3, 4, 5, 6, 7, 8, 0];
    public int Moves { get; private set; }
    public bool Solved => Tiles.SequenceEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 0 });
    public void Restore(int[]? tiles, int moves)
    {
        if (tiles is { Length: 9 } && tiles.Order().SequenceEqual(Enumerable.Range(0, 9)) && Inversions(tiles) % 2 == 0)
        { Tiles = tiles.ToArray(); Moves = Math.Max(0, moves); }
        else Shuffle();
    }
    public bool Move(int index)
    {
        if (index is < 0 or > 8) return false;
        int blank = Array.IndexOf(Tiles, 0);
        if (Math.Abs(index / 3 - blank / 3) + Math.Abs(index % 3 - blank % 3) != 1) return false;
        (Tiles[index], Tiles[blank]) = (Tiles[blank], Tiles[index]); Moves++; return true;
    }
    public void Shuffle()
    {
        Tiles = [1, 2, 3, 4, 5, 6, 7, 8, 0];
        int previous = -1;
        for (int step = 0; step < 150; step++)
        {
            int blank = Array.IndexOf(Tiles, 0);
            int[] options = Enumerable.Range(0, 9).Where(i => i != previous && Math.Abs(i / 3 - blank / 3) + Math.Abs(i % 3 - blank % 3) == 1).ToArray();
            Move(options[Random.Shared.Next(options.Length)]); previous = blank;
        }
        if (Solved) Move(7);
        Moves = 0;
    }
    private static int Inversions(int[] tiles)
    {
        int count = 0;
        for (int i = 0; i < tiles.Length; i++) for (int j = i + 1; j < tiles.Length; j++) if (tiles[i] != 0 && tiles[j] != 0 && tiles[i] > tiles[j]) count++;
        return count;
    }
}
