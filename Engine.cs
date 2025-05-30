using System.Reflection;
using System.Text.Json;
using Silk.NET.Maths;
using TheAdventure.Models;
using TheAdventure.Models.Data;
using TheAdventure.Scripting;

namespace TheAdventure;

public class Engine
{
    private readonly GameRenderer _renderer;
    private readonly Input _input;
    private readonly ScriptEngine _scriptEngine = new();

    private readonly Dictionary<int, GameObject> _gameObjects = new();
    private readonly Dictionary<string, TileSet> _loadedTileSets = new();
    private readonly Dictionary<int, Tile> _tileIdMap = new();

    private Level _currentLevel = new();
    private PlayerObject? _player;

    private bool _isPaused = false;
    private bool _escPressedLastFrame = false;
    private bool _rPressedLastFrame = false;

    private DateTimeOffset _lastUpdate = DateTimeOffset.Now;

    public Engine(GameRenderer renderer, Input input)
    {
        _renderer = renderer;
        _input = input;

        _input.OnMouseClick += (_, coords) =>
        {
            if (!_isPaused)
            {
                AddBomb(coords.x, coords.y);
            }
        };
    }

    public void SetupWorld()
    {
        _player = new(SpriteSheet.Load(_renderer, "Player.json", "Assets"), 100, 100);

        var levelContent = File.ReadAllText(Path.Combine("Assets", "terrain.tmj"));
        var level = JsonSerializer.Deserialize<Level>(levelContent);
        if (level == null)
        {
            throw new Exception("Failed to load level");
        }

        foreach (var tileSetRef in level.TileSets)
        {
            var tileSetContent = File.ReadAllText(Path.Combine("Assets", tileSetRef.Source));
            var tileSet = JsonSerializer.Deserialize<TileSet>(tileSetContent);
            if (tileSet == null)
            {
                throw new Exception("Failed to load tile set");
            }

            foreach (var tile in tileSet.Tiles)
            {
                tile.TextureId = _renderer.LoadTexture(Path.Combine("Assets", tile.Image), out _);
                _tileIdMap.Add(tile.Id!.Value, tile);
            }

            _loadedTileSets.Add(tileSet.Name, tileSet);
        }

        if (level.Width == null || level.Height == null)
        {
            throw new Exception("Invalid level dimensions");
        }

        if (level.TileWidth == null || level.TileHeight == null)
        {
            throw new Exception("Invalid tile dimensions");
        }

        _renderer.SetWorldBounds(new Rectangle<int>(0, 0, level.Width.Value * level.TileWidth.Value,
            level.Height.Value * level.TileHeight.Value));

        _currentLevel = level;

        _scriptEngine.LoadAll(Path.Combine("Assets", "Scripts"));
    }

    public void ProcessFrame()
    {
        var currentTime = DateTimeOffset.Now;
        var msSinceLastFrame = (currentTime - _lastUpdate).TotalMilliseconds;
        _lastUpdate = currentTime;

        if (_player == null)
        {
            return;
        }

        bool escPressed = _input.IsEscapePressed();
        if (escPressed && !_escPressedLastFrame)
        {
            _isPaused = !_isPaused;
            Console.WriteLine(_isPaused ? "Game paused" : "Game resumed");
        }
        _escPressedLastFrame = escPressed;

        bool rPressed = _input.IsKeyRPressed();
        if (rPressed && !_rPressedLastFrame && _player.CurrentHP <= 0)
        {
            RespawnPlayer();
        }
        _rPressedLastFrame = rPressed;

        if (_isPaused || _player.CurrentHP <= 0)
        {
            return;
        }

        double up = _input.IsUpPressed() ? 1.0 : 0.0;
        double down = _input.IsDownPressed() ? 1.0 : 0.0;
        double left = _input.IsLeftPressed() ? 1.0 : 0.0;
        double right = _input.IsRightPressed() ? 1.0 : 0.0;
        bool isAttacking = _input.IsKeyAPressed() && (up + down + left + right <= 1);
        bool addBomb = _input.IsKeyBPressed();

        _player.UpdatePosition(up, down, left, right, 48, 48, msSinceLastFrame);
        if (isAttacking)
        {
            _player.Attack();
        }

        _scriptEngine.ExecuteAll(this);

        if (addBomb)
        {
            AddBomb(_player.Position.X, _player.Position.Y, false);
        }
    }

    public void RenderFrame()
    {
        _renderer.SetDrawColor(0, 0, 0, 255);
        _renderer.ClearScreen();

        var playerPosition = _player!.Position;
        _renderer.CameraLookAt(playerPosition.X, playerPosition.Y);

        RenderTerrain();
        RenderAllObjects();

        RenderUI();

        if (_isPaused)
        {
            RenderPauseScreen();
        }

        if (_player.CurrentHP <= 0)
        {
            RenderDeathScreen();
        }

        _renderer.PresentFrame();
    }

    private void RenderPauseScreen()
    {
        var windowSize = _renderer.GetWindowSize();

        _renderer.SetDrawColor(0, 0, 0, 128);
        var overlayRect = new Rectangle<int>(0, 0, windowSize.Width, windowSize.Height);
        _renderer.RenderUIRectangle(overlayRect);
        
        int centerX = windowSize.Width / 2;
        int centerY = windowSize.Height / 2;

        _renderer.SetDrawColor(255, 255, 255, 255);

        var leftBar = new Rectangle<int>(centerX - 30, centerY - 40, 20, 80);
        var rightBar = new Rectangle<int>(centerX + 10, centerY - 40, 20, 80);
        
        _renderer.RenderUIRectangle(leftBar);
        _renderer.RenderUIRectangle(rightBar);

        _renderer.SetDrawColor(200, 200, 200, 255);
        var borderRect = new Rectangle<int>(centerX - 40, centerY - 50, 80, 100);
        _renderer.RenderUIRectangleBorder(borderRect);
    }

    private void RenderUI()
    {
        if (_player == null) return;

        var windowSize = _renderer.GetWindowSize();

        int healthBarX = 20;
        int healthBarY = 20;
        int healthBarWidth = 200;
        int healthBarHeight = 20;

        _renderer.SetDrawColor(100, 20, 20, 255);
        var healthBarBg = new Rectangle<int>(healthBarX, healthBarY, healthBarWidth, healthBarHeight);
        _renderer.RenderUIRectangle(healthBarBg);

        if (_player.CurrentHP > 0)
        {
            _renderer.SetDrawColor(50, 200, 50, 255);
            var healthPercentage = (float)_player.CurrentHP / _player.MaxHP;
            var currentHealthWidth = (int)(healthBarWidth * healthPercentage);
            var healthBarFg = new Rectangle<int>(healthBarX, healthBarY, currentHealthWidth, healthBarHeight);
            _renderer.RenderUIRectangle(healthBarFg);
        }

        _renderer.SetDrawColor(255, 255, 255, 255);
        _renderer.RenderUIRectangleBorder(new Rectangle<int>(healthBarX, healthBarY, healthBarWidth, healthBarHeight));
        
        RenderHPText(healthBarX + healthBarWidth + 10, healthBarY);

        RenderHealthNumbers(healthBarX, healthBarY + healthBarHeight + 5);

        RenderHealthStatusIndicator(healthBarX, healthBarY - 25);
    }

    private void RenderHPText(int x, int y)
    {
        _renderer.SetDrawColor(255, 255, 255, 255);
        
        // Letter "H" made of rectangles
        var h1 = new Rectangle<int>(x, y, 3, 20);        // Left vertical line
        var h2 = new Rectangle<int>(x + 12, y, 3, 20);   // Right vertical line  
        var h3 = new Rectangle<int>(x, y + 8, 15, 3);    // Middle horizontal line
        _renderer.RenderUIRectangle(h1);
        _renderer.RenderUIRectangle(h2);
        _renderer.RenderUIRectangle(h3);
        
        // Letter "P" made of rectangles
        int pX = x + 20;
        var p1 = new Rectangle<int>(pX, y, 3, 20);       // Left vertical line
        var p2 = new Rectangle<int>(pX, y, 12, 3);       // Top horizontal line
        var p3 = new Rectangle<int>(pX + 9, y, 3, 9);    // Top right vertical line
        var p4 = new Rectangle<int>(pX, y + 8, 12, 3);   // Middle horizontal line
        _renderer.RenderUIRectangle(p1);
        _renderer.RenderUIRectangle(p2);
        _renderer.RenderUIRectangle(p3);
        _renderer.RenderUIRectangle(p4);
    }

    private void RenderHealthNumbers(int x, int y)
    {
        if (_player == null) return;
        
        _renderer.SetDrawColor(255, 255, 255, 255);
        
        string healthText = $"{_player.CurrentHP}/{_player.MaxHP}";
        
        int digitX = x;
        foreach (char digit in healthText)
        {
            if (char.IsDigit(digit))
            {
                RenderDigit(digit, digitX, y);
                digitX += 8;
            }
            else if (digit == '/')
            {
                _renderer.SetDrawColor(255, 255, 255, 255);
                var slash = new Rectangle<int>(digitX + 2, y + 2, 2, 8);
                _renderer.RenderUIRectangle(slash);
                digitX += 6;
            }
        }
    }

    private void RenderDigit(char digit, int x, int y)
    {
        _renderer.SetDrawColor(255, 255, 255, 255);
        
        switch (digit)
        {
            case '0':
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y, 6, 2));     // Top
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y, 2, 10));    // Left
                _renderer.RenderUIRectangle(new Rectangle<int>(x + 4, y, 2, 10)); // Right
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y + 8, 6, 2)); // Bottom
                break;
            case '1':
                _renderer.RenderUIRectangle(new Rectangle<int>(x + 4, y, 2, 10)); // Right only
                break;
            case '2':
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y, 6, 2));     // Top
                _renderer.RenderUIRectangle(new Rectangle<int>(x + 4, y, 2, 5)); // Top right
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y + 4, 6, 2)); // Middle
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y + 5, 2, 5)); // Bottom left
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y + 8, 6, 2)); // Bottom
                break;
            case '3':
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y, 6, 2));     // Top
                _renderer.RenderUIRectangle(new Rectangle<int>(x + 4, y, 2, 10)); // Right
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y + 4, 6, 2)); // Middle
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y + 8, 6, 2)); // Bottom
                break;
            case '4':
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y, 2, 5));     // Top left
                _renderer.RenderUIRectangle(new Rectangle<int>(x + 4, y, 2, 10)); // Right
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y + 4, 6, 2)); // Middle
                break;
            case '5':
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y, 6, 2));     // Top
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y, 2, 5));     // Top left
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y + 4, 6, 2)); // Middle
                _renderer.RenderUIRectangle(new Rectangle<int>(x + 4, y + 5, 2, 5)); // Bottom right
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y + 8, 6, 2)); // Bottom
                break;
            case '6':
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y, 6, 2));     // Top
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y, 2, 10));    // Left
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y + 4, 6, 2)); // Middle
                _renderer.RenderUIRectangle(new Rectangle<int>(x + 4, y + 5, 2, 5)); // Bottom right
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y + 8, 6, 2)); // Bottom
                break;
            case '7':
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y, 6, 2));     // Top
                _renderer.RenderUIRectangle(new Rectangle<int>(x + 4, y, 2, 10)); // Right
                break;
            case '8':
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y, 6, 2));     // Top
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y, 2, 10));    // Left
                _renderer.RenderUIRectangle(new Rectangle<int>(x + 4, y, 2, 10)); // Right
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y + 4, 6, 2)); // Middle
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y + 8, 6, 2)); // Bottom
                break;
            case '9':
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y, 6, 2));     // Top
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y, 2, 5));     // Top left
                _renderer.RenderUIRectangle(new Rectangle<int>(x + 4, y, 2, 10)); // Right
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y + 4, 6, 2)); // Middle
                _renderer.RenderUIRectangle(new Rectangle<int>(x, y + 8, 6, 2)); // Bottom
                break;
        }
    }

    private void RenderHealthStatusIndicator(int x, int y)
    {
        if (_player == null) return;
        
        float healthPercentage = (float)_player.CurrentHP / _player.MaxHP;
        
        // Color-coded health status indicator
        if (healthPercentage > 0.7f)
        {
            // Healthy - Green plus sign
            _renderer.SetDrawColor(0, 255, 0, 255);
            _renderer.RenderUIRectangle(new Rectangle<int>(x + 2, y, 6, 2));  // Horizontal
            _renderer.RenderUIRectangle(new Rectangle<int>(x + 4, y - 2, 2, 6)); // Vertical
        }
        else if (healthPercentage > 0.3f)
        {
            // Injured - Yellow warning triangle
            _renderer.SetDrawColor(255, 255, 0, 255);
            _renderer.RenderUIRectangle(new Rectangle<int>(x + 5, y, 2, 8));    // Center line
            _renderer.RenderUIRectangle(new Rectangle<int>(x + 3, y + 2, 6, 2)); // Top part
            _renderer.RenderUIRectangle(new Rectangle<int>(x + 1, y + 4, 10, 2)); // Bottom part
            _renderer.RenderUIRectangle(new Rectangle<int>(x + 5, y + 7, 2, 2)); // Dot
        }
        else if (healthPercentage > 0)
        {
            // Critical - Red skull-like indicator
            _renderer.SetDrawColor(255, 0, 0, 255);
            _renderer.RenderUIRectangle(new Rectangle<int>(x + 1, y + 1, 8, 6)); // Head
            _renderer.RenderUIRectangle(new Rectangle<int>(x + 2, y + 2, 2, 2)); // Left eye
            _renderer.RenderUIRectangle(new Rectangle<int>(x + 6, y + 2, 2, 2)); // Right eye
            _renderer.RenderUIRectangle(new Rectangle<int>(x + 4, y + 4, 2, 2)); // Nose
        }
    }

    private void RenderDeathScreen()
    {
        var windowSize = _renderer.GetWindowSize();
        
        _renderer.SetDrawColor(150, 0, 0, 100);
        var overlayRect = new Rectangle<int>(0, 0, windowSize.Width, windowSize.Height);
        _renderer.RenderUIRectangle(overlayRect);
        
        int centerX = windowSize.Width / 2;
        int centerY = windowSize.Height / 2;
        
        // Create "DEAD" text using rectangles
        _renderer.SetDrawColor(255, 255, 255, 255);
        
        // Letter "D"
        var d1 = new Rectangle<int>(centerX - 80, centerY - 30, 4, 60); // Left line
        var d2 = new Rectangle<int>(centerX - 80, centerY - 30, 40, 4); // Top line
        var d3 = new Rectangle<int>(centerX - 80, centerY + 26, 40, 4); // Bottom line
        var d4 = new Rectangle<int>(centerX - 44, centerY - 26, 4, 20); // Top right
        var d5 = new Rectangle<int>(centerX - 44, centerY + 6, 4, 20);  // Bottom right
        
        // Letter "E"
        var e1 = new Rectangle<int>(centerX - 30, centerY - 30, 4, 60); // Left line
        var e2 = new Rectangle<int>(centerX - 30, centerY - 30, 30, 4); // Top line
        var e3 = new Rectangle<int>(centerX - 30, centerY - 2, 25, 4);  // Middle line
        var e4 = new Rectangle<int>(centerX - 30, centerY + 26, 30, 4); // Bottom line
        
        // Letter "A"
        var a1 = new Rectangle<int>(centerX + 10, centerY - 30, 4, 60); // Left line
        var a2 = new Rectangle<int>(centerX + 26, centerY - 30, 4, 60); // Right line
        var a3 = new Rectangle<int>(centerX + 10, centerY - 30, 20, 4); // Top line
        var a4 = new Rectangle<int>(centerX + 10, centerY - 2, 20, 4);  // Middle line
        
        // Letter "D" (second one)
        var d6 = new Rectangle<int>(centerX + 40, centerY - 30, 4, 60); // Left line
        var d7 = new Rectangle<int>(centerX + 40, centerY - 30, 35, 4); // Top line
        var d8 = new Rectangle<int>(centerX + 40, centerY + 26, 35, 4); // Bottom line
        var d9 = new Rectangle<int>(centerX + 71, centerY - 26, 4, 20); // Top right
        var d10 = new Rectangle<int>(centerX + 71, centerY + 6, 4, 20); // Bottom right
        
        // Render "DEAD"
        _renderer.RenderUIRectangle(d1);
        _renderer.RenderUIRectangle(d2);
        _renderer.RenderUIRectangle(d3);
        _renderer.RenderUIRectangle(d4);
        _renderer.RenderUIRectangle(d5);
        
        _renderer.RenderUIRectangle(e1);
        _renderer.RenderUIRectangle(e2);
        _renderer.RenderUIRectangle(e3);
        _renderer.RenderUIRectangle(e4);
        
        _renderer.RenderUIRectangle(a1);
        _renderer.RenderUIRectangle(a2);
        _renderer.RenderUIRectangle(a3);
        _renderer.RenderUIRectangle(a4);
        
        _renderer.RenderUIRectangle(d6);
        _renderer.RenderUIRectangle(d7);
        _renderer.RenderUIRectangle(d8);
        _renderer.RenderUIRectangle(d9);
        _renderer.RenderUIRectangle(d10);
        
        _renderer.SetDrawColor(200, 200, 200, 255);
        
        var arrow1 = new Rectangle<int>(centerX - 20, centerY + 50, 40, 4);
        var arrow2 = new Rectangle<int>(centerX + 16, centerY + 46, 4, 12);
        _renderer.RenderUIRectangle(arrow1);
        _renderer.RenderUIRectangle(arrow2);
        
        // "R" letter
        var r1 = new Rectangle<int>(centerX - 10, centerY + 70, 4, 30); // Left line
        var r2 = new Rectangle<int>(centerX - 10, centerY + 70, 20, 4); // Top line
        var r3 = new Rectangle<int>(centerX + 6, centerY + 70, 4, 15);  // Top right vertical
        var r4 = new Rectangle<int>(centerX - 10, centerY + 85, 15, 4); // Middle line
        var r5 = new Rectangle<int>(centerX + 6, centerY + 89, 4, 11);  // Bottom right diagonal
        
        _renderer.RenderUIRectangle(r1);
        _renderer.RenderUIRectangle(r2);
        _renderer.RenderUIRectangle(r3);
        _renderer.RenderUIRectangle(r4);
        _renderer.RenderUIRectangle(r5);
    }

    public void RenderAllObjects()
    {
        var toRemove = new List<int>();
        foreach (var gameObject in GetRenderables())
        {
            gameObject.Render(_renderer);
            if (gameObject is TemporaryGameObject { IsExpired: true } tempGameObject)
            {
                toRemove.Add(tempGameObject.Id);
            }
        }

        foreach (var id in toRemove)
        {
            _gameObjects.Remove(id, out var gameObject);

            if (_player == null)
            {
                continue;
            }

            var tempGameObject = (TemporaryGameObject)gameObject!;
            var deltaX = Math.Abs(_player.Position.X - tempGameObject.Position.X);
            var deltaY = Math.Abs(_player.Position.Y - tempGameObject.Position.Y);
            if (deltaX < 32 && deltaY < 32)
            {
                _player.TakeDamage(1, (double)DateTimeOffset.Now.ToUnixTimeMilliseconds());
            }
        }

        _player?.Render(_renderer);
    }

    public void RenderTerrain()
    {
        foreach (var currentLayer in _currentLevel.Layers)
        {
            for (int i = 0; i < _currentLevel.Width; ++i)
            {
                for (int j = 0; j < _currentLevel.Height; ++j)
                {
                    int? dataIndex = j * currentLayer.Width + i;
                    if (dataIndex == null)
                    {
                        continue;
                    }

                    var currentTileId = currentLayer.Data[dataIndex.Value] - 1;
                    if (currentTileId == null)
                    {
                        continue;
                    }

                    var currentTile = _tileIdMap[currentTileId.Value];

                    var tileWidth = currentTile.ImageWidth ?? 0;
                    var tileHeight = currentTile.ImageHeight ?? 0;

                    var sourceRect = new Rectangle<int>(0, 0, tileWidth, tileHeight);
                    var destRect = new Rectangle<int>(i * tileWidth, j * tileHeight, tileWidth, tileHeight);
                    _renderer.RenderTexture(currentTile.TextureId, sourceRect, destRect);
                }
            }
        }
    }

    public IEnumerable<RenderableGameObject> GetRenderables()
    {
        foreach (var gameObject in _gameObjects.Values)
        {
            if (gameObject is RenderableGameObject renderableGameObject)
            {
                yield return renderableGameObject;
            }
        }
    }

    public (int X, int Y) GetPlayerPosition()
    {
        return _player!.Position;
    }

    public void AddBomb(int X, int Y, bool translateCoordinates = true)
    {
        var worldCoords = translateCoordinates ? _renderer.ToWorldCoordinates(X, Y) : new Vector2D<int>(X, Y);

        SpriteSheet spriteSheet = SpriteSheet.Load(_renderer, "BombExploding.json", "Assets");
        spriteSheet.ActivateAnimation("Explode");

        TemporaryGameObject bomb = new(spriteSheet, 2.1, (worldCoords.X, worldCoords.Y));
        _gameObjects.Add(bomb.Id, bomb);
    }

    private void RespawnPlayer()
    {
        _gameObjects.Clear();

        _player = new PlayerObject(
            SpriteSheet.Load(_renderer, "Player.json", "Assets"),
            400, 400 
        );

        _renderer.CameraLookAt(_player.Position.X, _player.Position.Y);
    }
}