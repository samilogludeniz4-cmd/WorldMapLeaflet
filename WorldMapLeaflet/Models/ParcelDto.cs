namespace WorldMapLeaflet.Models;

public class CreateParcelRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<CoordinateDto> Coordinates { get; set; } = new(); // En az 4 nokta
}

public class CoordinateDto
{
    public double Lat { get; set; }
    public double Lon { get; set; }
}

public class ParcelResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<CoordinateDto> Coordinates { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

