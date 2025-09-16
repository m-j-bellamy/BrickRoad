using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", async context =>
{
    context.Response.ContentType = "text/html";
    await context.Response.WriteAsync(@"
        <html>
        <body>
            <form method='get' action='/directions'>
                From: <input name='from' /><br/>
                To: <input name='to' /><br/>
                <input type='submit' value='Get Directions'/>
            </form>
        </body>
        </html>
    ");
});

app.MapGet("/directions", async context =>
{
    var from = context.Request.Query["from"].ToString();
    var to = context.Request.Query["to"].ToString();

    if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
    {
        await context.Response.WriteAsync("Missing 'from' or 'to' parameters.");
        return;
    }

    try
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BrickRoad/1.0 (miriambellamy0@gmail.com)");

        async Task<(double lat, double lon)?> Geocode(string location)
        {
            var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(location)}&format=json&limit=1";
            var json = await httpClient.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<List<NominatimResult>>(json);

            if (data == null || data.Count == 0)
                return null;

            return (double.Parse(data[0].lat), double.Parse(data[0].lon));
        }

        var fromCoord = await Geocode(from);
        var toCoord = await Geocode(to);

        if (fromCoord == null || toCoord == null)
        {
            await context.Response.WriteAsync("Could not geocode one or both locations.");
            return;
        }

        var osrmUrl = $"http://router.project-osrm.org/route/v1/driving/{fromCoord.Value.lon},{fromCoord.Value.lat};{toCoord.Value.lon},{toCoord.Value.lat}?overview=false&steps=true";
        var osrmJson = await httpClient.GetStringAsync(osrmUrl);
        var routeDoc = JsonDocument.Parse(osrmJson);

        var steps = routeDoc.RootElement
            .GetProperty("routes")[0]
            .GetProperty("legs")[0]
            .GetProperty("steps");

        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync($"Directions from {from} to {to}:\n\n");

        foreach (var step in steps.EnumerateArray())
        {
            var maneuver = step.GetProperty("maneuver");
            var type = maneuver.GetProperty("type").GetString();
            var modifier = maneuver.TryGetProperty("modifier", out var modProp) ? modProp.GetString() : null;
            var roadName = step.GetProperty("name").GetString();

            var instruction = $"Go {modifier ?? type} onto {roadName}";
            await context.Response.WriteAsync($"- {instruction}\n");
        }

    }
    catch (Exception ex)
    {
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync("An error occurred:\n" + ex.Message);
    }
});

app.Run();

public class NominatimResult
{
    public string lat { get; set; }
    public string lon { get; set; }
}
