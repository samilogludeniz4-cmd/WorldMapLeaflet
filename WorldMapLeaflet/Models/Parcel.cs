using NetTopologySuite.Geometries;

namespace WorldMapLeaflet.Models;

public class Parcel
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public User User { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Polygon Geometry { get; set; } = null!; // PostGIS polygon - 4+ koordinat noktası
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

