using Linteum.BlazorApp.Api;
using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.BlazorApp;

/// <summary>
/// Thin facade over the API gateway. The real logic now lives in focused collaborators under
/// <see cref="Linteum.BlazorApp.Api"/>: <see cref="SessionStore"/> (session / local-storage),
/// <see cref="PixelCacheManager"/> (the three client caches), <see cref="ApiHttp"/> (the HTTP
/// client), and one repository per resource. This class preserves the public surface the 12
/// consuming components depend on and forwards each call unchanged (P‑MAIN‑03). It holds no state
/// and no logic of its own.
/// </summary>
internal sealed class MyApiClient
{
    private readonly SessionStore _session;
    private readonly PixelCacheManager _cache;
    private readonly ColorsRepository _colors;
    private readonly CanvasesRepository _canvases;
    private readonly SubscriptionsRepository _subscriptions;
    private readonly CanvasChatRepository _canvasChat;
    private readonly PixelsRepository _pixels;
    private readonly HistoryRepository _history;
    private readonly BalanceRepository _balance;
    private readonly AccountRepository _account;

    public MyApiClient(
        SessionStore session,
        PixelCacheManager cache,
        ColorsRepository colors,
        CanvasesRepository canvases,
        SubscriptionsRepository subscriptions,
        CanvasChatRepository canvasChat,
        PixelsRepository pixels,
        HistoryRepository history,
        BalanceRepository balance,
        AccountRepository account)
    {
        _session = session;
        _cache = cache;
        _colors = colors;
        _canvases = canvases;
        _subscriptions = subscriptions;
        _canvasChat = canvasChat;
        _pixels = pixels;
        _history = history;
        _balance = balance;
        _account = account;
    }

    // ---- session / local storage ----

    public Task SetSessionAsync(Guid? sessionId) => _session.SetSessionAsync(sessionId);
    public void ClearSession() => _session.ClearSession();
    public Task<Guid?> GetCurrentUserIdAsync() => _session.GetCurrentUserIdAsync();
    public Task<LoginMethod> GetCurrentLoginMethodAsync() => _session.GetCurrentLoginMethodAsync();
    public Task<bool> IsGuestUserAsync() => _session.IsGuestUserAsync();

    // ---- cache (used directly by CanvasPage) ----

    public void InvalidateHistoryCache(Guid pixelId) => _cache.InvalidateHistoryCache(pixelId);
    public void InvalidatePixelCache(string canvasName, int x, int y) => _cache.InvalidatePixelCache(canvasName, x, y);
    public void ClearCanvasCache(string canvasName) => _cache.ClearCanvasCache(canvasName);
    public void HandlePixelColorChanged(string canvasName, int x, int y, int colorId, Guid? pixelId = null, Guid? ownerId = null)
        => _cache.HandlePixelColorChanged(canvasName, x, y, colorId, pixelId, ownerId);
    public void HandlePixelDeleted(string canvasName, int x, int y, Guid canvasId)
        => _cache.HandlePixelDeleted(canvasName, x, y, canvasId);
    public int? GetWhiteColorId() => _cache.GetWhiteColorId();
    public bool IsPixelKnownWhite(string canvasName, int x, int y) => _cache.IsPixelKnownWhite(canvasName, x, y);

    // ---- colors ----

    public Task<List<ColorDto>?> GetColorsAsync() => _colors.GetColorsAsync();

    // ---- canvases ----

    public Task<CanvasDto?> AddCanvasAsync(CanvasDto canvasDto, string? password) => _canvases.AddCanvasAsync(canvasDto, password);
    public Task<CanvasDto?> AddCanvasFromImageAsync(CanvasDto canvasDto, string? password, byte[] imageBytes, string fileName)
        => _canvases.AddCanvasFromImageAsync(canvasDto, password, imageBytes, fileName);
    public Task<CanvasDto> GetCanvas(string canvasName) => _canvases.GetCanvas(canvasName);
    public Task<List<CanvasDto>> SearchCanvasesAsync(string name) => _canvases.SearchCanvasesAsync(name);
    public Task<List<CanvasDto>> GetSubscribedCanvasesAsync() => _canvases.GetSubscribedCanvasesAsync();
    public Task<CanvasOperationResponseDto> EraseCanvasAsync(string canvasName) => _canvases.EraseCanvasAsync(canvasName);
    public Task<CanvasOperationResponseDto> DeleteCanvasAsync(string canvasName) => _canvases.DeleteCanvasAsync(canvasName);
    public Task<byte[]> GetCanvasImage(CanvasDto canvasDto) => _canvases.GetCanvasImage(canvasDto);

    // ---- subscriptions ----

    public Task<bool> SubscribeAsync(string canvasName, string? password) => _subscriptions.SubscribeAsync(canvasName, password);
    public Task<bool> UnsubscribeAsync(string canvasName) => _subscriptions.UnsubscribeAsync(canvasName);

    // ---- chat ----

    public Task SendCanvasChatMessageAsync(string canvasName, string message) => _canvasChat.SendCanvasChatMessageAsync(canvasName, message);

    // ---- pixels ----

    public Task<NormalModeQuotaDto?> GetNormalModeQuotaAsync(string canvasName) => _pixels.GetNormalModeQuotaAsync(canvasName);
    public Task<PixelDto> GetPixelData(string canvasName, int x, int y, bool useCache = false) => _pixels.GetPixelData(canvasName, x, y, useCache);
    public Task<PixelDto> Paint((int X, int Y) clickedPixel, CanvasDto canvasDto, int colorId) => _pixels.Paint(clickedPixel, canvasDto, colorId);
    public Task<PixelDto> Paint((int X, int Y) clickedPixel, CanvasDto canvasDto, int colorId, long price) => _pixels.Paint(clickedPixel, canvasDto, colorId, price);
    public Task<PixelBatchChangeResultDto> PaintBatch(CanvasDto canvasDto, IReadOnlyCollection<PixelDto> pixels, string? masterPassword = null)
        => _pixels.PaintBatch(canvasDto, pixels, masterPassword);
    public Task<PixelBatchChangeResultDto> PaintBatch(CanvasDto canvasDto, IReadOnlyCollection<CoordinateDto> coordinates, int colorId, long price = 0, string? masterPassword = null, StrokePlaybackMetadataDto? playback = null)
        => _pixels.PaintBatch(canvasDto, coordinates, colorId, price, masterPassword, playback);
    public Task<PixelBatchDeleteResultDto> DeleteBatchAsync(CanvasDto canvasDto, IReadOnlyCollection<CoordinateDto> coordinates, string? masterPassword = null, StrokePlaybackMetadataDto? playback = null)
        => _pixels.DeleteBatchAsync(canvasDto, coordinates, masterPassword, playback);
    public Task PaintTextAsync((int X, int Y) clickedPixel, CanvasDto canvasDto, string text, int textColorId, int? backgroundColorId, int fontSize)
        => _pixels.PaintTextAsync(clickedPixel, canvasDto, text, textColorId, backgroundColorId, fontSize);

    // ---- history ----

    public Task<List<HistoryResponseItem>> GetHistoryAsync(Guid pixelId, bool useCache = false) => _history.GetHistoryAsync(pixelId, useCache);

    // ---- balance ----

    public Task<long> GetCurrentGoldAsync(Guid canvasId) => _balance.GetCurrentGoldAsync(canvasId);

    // ---- account ----

    public Task<(UserDto? User, Guid? SessionId)> LoginAsync(string email, string password) => _account.LoginAsync(email, password);
    public Task<(UserDto? User, Guid? SessionId, string? Error)> LoginWithGoogleCodeAsync(string code) => _account.LoginWithGoogleCodeAsync(code);
    public Task<(UserDto? User, Guid? SessionId)> LoginAsync(Guid sessionId) => _account.LoginAsync(sessionId);
    public Task<(UserDto? User, Guid? SessionId)> LoginAsGuestAsync() => _account.LoginAsGuestAsync();
    public Task<(UserDto? User, Guid? SessionId)> SignupAsync(string email, string password, string userName) => _account.SignupAsync(email, password, userName);
    public Task ChangeUsernameAsync(string userName) => _account.ChangeUsernameAsync(userName);
    public Task ChangePasswordAsync(string password) => _account.ChangePasswordAsync(password);
}
