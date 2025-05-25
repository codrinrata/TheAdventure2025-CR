using Silk.NET.Maths;

namespace TheAdventure;

public class Camera
{
    private int _x;
    private int _y;
    private Rectangle<int> _worldBounds = new();
    private float _zoom = 1.0f; // Default zoom level
    
    public int X => _x;
    public int Y => _y;
    public readonly int Width;
    public readonly int Height;
    
    public float Zoom
    {
        get => _zoom;
        set => _zoom = Math.Max(0.8f, Math.Min(5.0f, value)); // Zoom limits 0.1x to 5x
    }
    
    public Camera(int width, int height)
    {
        Width = width;
        Height = height;
    }
    
    public void SetWorldBounds(Rectangle<int> bounds)
    {
        // Account for zoom when calculating margins
        var marginLeft = (int)(Width / (2 * _zoom));
        var marginTop = (int)(Height / (2 * _zoom));
        
        if (marginLeft * 2 > bounds.Size.X)
        {
            marginLeft = 48;
        }
        
        if (marginTop * 2 > bounds.Size.Y)
        {
            marginTop = 48;
        }
        
        _worldBounds = new Rectangle<int>(marginLeft, marginTop, bounds.Size.X - marginLeft * 2,
            bounds.Size.Y - marginTop * 2);
        _x = marginLeft;
        _y = marginTop;
    }
    
    public void LookAt(int x, int y)
    {
        if (_worldBounds.Contains(new Vector2D<int>(_x, y)))
        {
            _y = y;
        }
        if (_worldBounds.Contains(new Vector2D<int>(x, _y)))
        {
            _x = x;
        }
    }

    public void ForceLookAt(int x, int y)
    {
        // Force camera to look at specified coordinates without bounds checking
        _x = x;
        _y = y;
    }
    
    public Rectangle<int> ToScreenCoordinates(Rectangle<int> rect)
    {
        // Apply zoom to the rectangle size and position
        var scaledRect = new Rectangle<int>(
            (int)(rect.Origin.X * _zoom),
            (int)(rect.Origin.Y * _zoom),
            (int)(rect.Size.X * _zoom),
            (int)(rect.Size.Y * _zoom)
        );
        
        // Then translate to screen coordinates
        return scaledRect.GetTranslated(new Vector2D<int>(
            (int)(Width / 2 - X * _zoom), 
            (int)(Height / 2 - Y * _zoom)
        ));
    }
    
    public Vector2D<int> ToWorldCoordinates(Vector2D<int> point)
    {
        // Convert screen coordinates back to world coordinates considering zoom
        return new Vector2D<int>(
            (int)((point.X - Width / 2) / _zoom + X),
            (int)((point.Y - Height / 2) / _zoom + Y)
        );
    }
}