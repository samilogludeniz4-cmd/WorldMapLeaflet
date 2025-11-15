using Microsoft.AspNetCore.Identity;

namespace WorldMapLeaflet.Models;

public class User : IdentityUser
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Parcel> Parcels { get; set; } = new List<Parcel>();
}

