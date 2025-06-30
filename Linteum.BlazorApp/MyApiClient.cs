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

    public async Task<bool> LoginAsync(string email, string password)
    {
        var userDto = new UserDto { Email = email };
        var response = await _httpClient.PostAsJsonAsync($"/users/login?passwordHashOrKey={Uri.EscapeDataString(password)}&loginMethod=0", userDto);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> SignupAsync(string email, string password, string userName)
    {
        var userDto = new UserDto { Email = email, UserName = userName };
        var response = await _httpClient.PostAsJsonAsync($"/users/add-or-update?passwordHashOrKey={Uri.EscapeDataString(password)}&loginMethod=0", userDto);
        return response.IsSuccessStatusCode;
    }
}

