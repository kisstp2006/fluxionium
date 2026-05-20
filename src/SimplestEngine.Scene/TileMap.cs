namespace SimplestEngine;

[GDClass("TileSet", "Resource")]
public sealed class TileSet : Resource
{
    private readonly Dictionary<int, TileInfo> _tiles = new();

    public void SetTile(int id, Texture2D? texture, Rect2 region)
    {
        _tiles[id] = new TileInfo(texture, region);
    }

    public bool TryGetTile(int id, out TileInfo tile) => _tiles.TryGetValue(id, out tile);
}

public readonly record struct TileInfo(Texture2D? Texture, Rect2 Region);

[GDClass("TileMap", "Node2D")]
public sealed class TileMap : Node2D
{
    private readonly Dictionary<Vector2i, Cell> _cells = new();
    private TileSet? _tileSet;
    private Vector2 _cellSize = new(64, 64);
    private int[] _tileData = Array.Empty<int>();

    public TileSet? TileSet
    {
        get => _tileSet;
        set { _tileSet = value; QueueRedraw(); }
    }

    public Vector2 CellSize
    {
        get => _cellSize;
        set { _cellSize = value; QueueRedraw(); }
    }

    public int[] TileData
    {
        get => _tileData;
        set
        {
            _tileData = value ?? Array.Empty<int>();
            DecodeTileData();
            QueueRedraw();
        }
    }

    public void SetCell(int x, int y, int tile, bool flipX = false, bool flipY = false, bool transpose = false)
    {
        var pos = new Vector2i(x, y);
        if (tile < 0) _cells.Remove(pos);
        else _cells[pos] = new Cell(tile, flipX, flipY, transpose);
        QueueRedraw();
    }

    public int GetCell(int x, int y) =>
        _cells.TryGetValue(new Vector2i(x, y), out var cell) ? cell.Tile : -1;

    public Vector2 MapToWorld(Vector2 mapPosition, bool ignoreHalfOfs = false) =>
        new(mapPosition.X * _cellSize.X, mapPosition.Y * _cellSize.Y);

    public Vector2 WorldToMap(Vector2 worldPosition) =>
        new(MathF.Floor(worldPosition.X / _cellSize.X), MathF.Floor(worldPosition.Y / _cellSize.Y));

    public override void _Draw()
    {
        if (RenderingServer is null || _tileSet is null) return;

        foreach (var (pos, cell) in _cells)
        {
            if (!_tileSet.TryGetTile(cell.Tile, out var tile) || tile.Texture is null) continue;
            var src = tile.Region;
            if (src.Size == Vector2.Zero)
                src = new Rect2(Vector2.Zero, new Vector2(tile.Texture.Width, tile.Texture.Height));

            var dstPos = new Vector2(pos.X * _cellSize.X, pos.Y * _cellSize.Y);
            var dst = new Rect2(dstPos, _cellSize);
            RenderingServer.CanvasItemAddTextureRectRegion(CanvasItemRid, dst, tile.Texture.Rid, src, Modulate);
        }
    }

    public override void _EnterTree()
    {
        base._EnterTree();
        QueueRedraw();
    }

    private void DecodeTileData()
    {
        _cells.Clear();
        for (int i = 0; i + 2 < _tileData.Length; i += 3)
        {
            var key = _tileData[i];
            var tile = _tileData[i + 1];
            var flags = _tileData[i + 2];
            var x = (short)(key & 0xffff);
            var y = (short)((key >> 16) & 0xffff);
            _cells[new Vector2i(x, y)] = new Cell(tile, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0);
        }
    }

    private readonly record struct Cell(int Tile, bool FlipX, bool FlipY, bool Transpose);
}
