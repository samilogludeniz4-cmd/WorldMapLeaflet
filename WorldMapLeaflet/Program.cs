using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("WorldMapLeaflet/1.0");
http.DefaultRequestHeaders.From = "senin.gercek.mailin@ornek.com"; // <-- GERÇEK mailini yaz

app.MapGet("/api/geocode", async (string q) =>
{
    if (string.IsNullOrWhiteSpace(q)) return Results.Json(Array.Empty<object>());
    var url =
        "https://nominatim.openstreetmap.org/search" +
        $"?format=json&addressdetails=1&limit=8&countrycodes=tr&accept-language=tr&q={Uri.EscapeDataString(q)}";

    try
    {
        var resp = await http.GetAsync(url);
        if (!resp.IsSuccessStatusCode)
            return Results.Problem($"Nominatim: {(int)resp.StatusCode}");
        var data = await resp.Content.ReadFromJsonAsync<object>();
        return Results.Json(data);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.Run();