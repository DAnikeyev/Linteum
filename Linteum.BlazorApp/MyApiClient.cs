using Linteum.Shared.DTO;

namespace Linteum.BlazorApp;

public class MyApiClient
{
    private readonly HttpClient _httpClient;

    public MyApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<ColorDto>?> GetColorsAsync()
    {
        return await _httpClient.GetFromJsonAsync<List<ColorDto>>("/colors");
    }
}