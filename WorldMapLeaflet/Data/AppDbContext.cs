using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using WorldMapLeaflet.Models;

namespace WorldMapLeaflet.Data;

public class AppDbContext : IdentityDbContext<User>
{
    public DbSet<Parcel> Parcels { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Parcel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.Geometry)
                .HasColumnType("geometry(POLYGON,4326)") // PostGIS polygon with WGS84 (lat/lon)
                .IsRequired();

            entity.HasOne(e => e.User)
                .WithMany(u => u.Parcels)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

