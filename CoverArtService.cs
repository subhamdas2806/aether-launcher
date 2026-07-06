using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace GameShelf;

public class GridImage
{
    public string Url { get; set; } = "";
    public string Thumb { get; set; } = "";
}

public class CoverArtService
{
    private readonly HttpClient _httpClient;

    public CoverArtService(string apiKey)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AetherLauncher/1.0");
    }

    public async Task<List<(int Id, string Name, int? ReleaseYear)>> SearchGameAsync(string title)
    {
        var list = new List<(int Id, string Name, int? ReleaseYear)>();
        if (string.IsNullOrWhiteSpace(title)) return list;

        try
        {
            var url = $"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(title)}";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return list;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("success", out var successProp) && successProp.GetBoolean() &&
                doc.RootElement.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataProp.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number &&
                        item.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                    {
                        int id = idProp.GetInt32();
                        string name = nameProp.GetString() ?? "";
                        int? releaseYear = null;

                        if (item.TryGetProperty("release_date", out var releaseDateProp) && releaseDateProp.ValueKind == JsonValueKind.Number)
                        {
                            try
                            {
                                var releaseTimestamp = releaseDateProp.GetInt64();
                                releaseYear = DateTimeOffset.FromUnixTimeSeconds(releaseTimestamp).Year;
                            }
                            catch { }
                        }

                        list.Add((id, name, releaseYear));
                    }
                }
            }
        }
        catch { }
        return list;
    }

    public async Task<List<GridImage>> GetGridUrlsAsync(int gameId)
    {
        var list = new List<GridImage>();
        try
        {
            var url = $"https://www.steamgriddb.com/api/v2/grids/game/{gameId}?dimensions=600x900,342x482";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return list;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("success", out var successProp) && successProp.GetBoolean() &&
                doc.RootElement.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataProp.EnumerateArray())
                {
                    if (item.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
                    {
                        var highResUrl = urlProp.GetString() ?? "";
                        var thumbUrl = "";
                        if (item.TryGetProperty("thumb", out var thumbProp) && thumbProp.ValueKind == JsonValueKind.String)
                        {
                            thumbUrl = thumbProp.GetString() ?? "";
                        }
                        else
                        {
                            thumbUrl = highResUrl;
                        }

                        if (!string.IsNullOrEmpty(highResUrl))
                        {
                            list.Add(new GridImage { Url = highResUrl, Thumb = thumbUrl });
                        }
                    }
                }
            }
        }
        catch { }
        return list;
    }
}
