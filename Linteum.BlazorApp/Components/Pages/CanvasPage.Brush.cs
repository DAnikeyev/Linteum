using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.JSInterop;

namespace Linteum.BlazorApp.Components.Pages;

public partial class CanvasPage
{
    [JSInvokable]
    public async Task OnPixelClicked(int x, int y)
    {
        if (_canvas == null || IsDisposed)
        {
            return;
        }

        _clickedPixel = (x, y);
        _clickedPixelData = await ApiClient.GetPixelData(_canvas.Name, x, y, useCache: true);
        if (IsDisposed)
        {
            return;
        }

        if (_clickedPixelData != null && _colors != null)
        {
            var color = _colors.FirstOrDefault(c => c.Id == _clickedPixelData.ColorId);
            if (color != null && _renderer != null)
            {
                _renderer.EnqueuePixel(x, y, color.HexValue, suppressRipple: true);
            }
        }

        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task OnPixelSelectionCleared()
    {
        if (IsDisposed)
        {
            return Task.CompletedTask;
        }

        if (_isTextSelectionPersistenceEnabled && _clickedPixel.HasValue)
        {
            return Task.CompletedTask;
        }

        _clickedPixel = null;
        _clickedPixelData = null;
        return InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task OnBrushStrokeStarted()
    {
        if (IsDisposed)
        {
            return Task.CompletedTask;
        }

        lock (_brushStrokeLock)
        {
            _brushStrokeActive = true;
            _brushStrokePixels.Clear();
            _activeStrokeId = Guid.NewGuid();
            _strokeChunkSequence = 0;
            _strokeChunkStartedAtUtc = DateTime.UtcNow;
        }

        return Task.CompletedTask;
    }

    [JSInvokable]
    public async Task OnBrushStrokeEnded()
    {
        if (IsDisposed)
        {
            return;
        }

        bool hasPendingBrushPixels;
        bool hasPendingErasePixels;
        lock (_brushStrokeLock)
        {
            _brushStrokeActive = false;
            _brushStrokePixels.Clear();
            hasPendingBrushPixels = _pendingBrushPixels.Count > 0;
            hasPendingErasePixels = _pendingEraseCoordinates.Count > 0;
        }

        if (hasPendingBrushPixels)
        {
            await FlushPendingBrushPixelsAsync(drainAllRemaining: true);
        }

        if (hasPendingErasePixels)
        {
            await FlushPendingErasePixelsAsync(drainAllRemaining: true);
        }

        ResetStrokePlaybackTracking();
    }

    [JSInvokable]
    public async Task OnBrushPixelPaintRequested(int x, int y)
    {
        if (_canvas == null || _renderer == null || _pixelManager == null || !IsDragToolEnabled || _canvas.CanvasMode != CanvasMode.FreeDraw || IsDisposed)
        {
            return;
        }

        var isErasing = _isEraserBrushEnabled;
        var brushPaintSettings = isErasing ? null : _pixelManager.GetBrushPaintSettings();
        if (!isErasing && brushPaintSettings is null)
        {
            return;
        }

        bool shouldPaint;
        bool shouldFlushBrushBatch = false;
        lock (_brushStrokeLock)
        {
            if (!_brushStrokeActive)
            {
                _brushStrokeActive = true;
                _brushStrokePixels.Clear();
            }

            shouldPaint = _brushStrokePixels.Add((x, y));
        }

        if (!shouldPaint)
        {
            return;
        }

        if (isErasing)
        {
            var erasedPixels = GetEraserCoordinates(x, y).ToList();
            var eraseCandidates = new List<CoordinateDto>();
            var shouldFlushEraseBatch = false;

            lock (_brushStrokeLock)
            {
                foreach (var pixel in erasedPixels)
                {
                    var coordinate = (pixel.X, pixel.Y);
                    if (_clearedErasePixels.Contains(coordinate) || _pendingErasePixels.Contains(coordinate))
                    {
                        continue;
                    }

                    if (ApiClient.IsPixelKnownWhite(_canvas.Name, pixel.X, pixel.Y))
                    {
                        _clearedErasePixels.Add(coordinate);
                        continue;
                    }

                    eraseCandidates.Add(pixel);
                }
            }

            if (eraseCandidates.Count > 0)
            {
                var nonWhiteEraseCandidates = await _renderer.FilterNonWhiteCoordinatesAsync(eraseCandidates);
                var nonWhiteCandidateKeys = nonWhiteEraseCandidates
                    .Select(filtered => (filtered.X, filtered.Y))
                    .ToHashSet();
                var skippedAsWhite = eraseCandidates
                    .Where(candidate => !nonWhiteCandidateKeys.Contains((candidate.X, candidate.Y)))
                    .ToList();

                RememberClearedPixels(skippedAsWhite);

                lock (_brushStrokeLock)
                {
                    foreach (var pixel in nonWhiteEraseCandidates)
                    {
                        var coordinate = (pixel.X, pixel.Y);
                        if (_clearedErasePixels.Contains(coordinate) || !_pendingErasePixels.Add(coordinate))
                        {
                            continue;
                        }

                        _pendingEraseCoordinates.Add(pixel);
                    }

                    shouldFlushEraseBatch = _pendingEraseCoordinates.Count >= BrushBatchSize;
                }
            }

            if (shouldFlushEraseBatch)
            {
                await FlushPendingErasePixelsAsync();
            }
        }
        else
        {
            var (colorId, colorHex) = brushPaintSettings!.Value;

            lock (_brushStrokeLock)
            {
                _pendingErasePixels.Remove((x, y));
                _pendingEraseCoordinates.RemoveAll(coordinate => coordinate.X == x && coordinate.Y == y);
                _clearedErasePixels.Remove((x, y));
                _pendingBrushPixels.Add((x, y, colorId, colorHex));
                shouldFlushBrushBatch = _pendingBrushPixels.Count >= BrushBatchSize;
            }
        }

        if (shouldFlushBrushBatch)
        {
            await FlushPendingBrushPixelsAsync();
        }
    }

    private async Task UpdateBrushSelectionAsync((int X, int Y) pixel, PixelDto? pixelData, int requestVersion)
    {
        if (requestVersion != Volatile.Read(ref _brushSelectionVersion))
        {
            return;
        }

        _clickedPixel = pixel;
        _clickedPixelData = pixelData;
        await InvokeAsync(StateHasChanged);
    }

    private Task EnqueueConfirmedPixelPlaybackAsync(ConfirmedPixelPlaybackBatchDto? playbackBatch)
    {
        if (playbackBatch == null || playbackBatch.Pixels.Count == 0)
        {
            return Task.CompletedTask;
        }

        if (!string.IsNullOrWhiteSpace(playbackBatch.ClientOperationId)
            && _pendingLocalPlaybackOperationIds.TryRemove(playbackBatch.ClientOperationId, out _))
        {
            return Task.CompletedTask;
        }

        Interlocked.Increment(ref _pendingConfirmedPlaybackCount);
        return _confirmedPlaybackChannel.Writer.WriteAsync(ConfirmedPlaybackWorkItem.FromPixels(playbackBatch)).AsTask();
    }

    private Task EnqueueConfirmedPixelDeletionPlaybackAsync(ConfirmedPixelDeletionPlaybackBatchDto? playbackBatch)
    {
        if (playbackBatch == null || playbackBatch.Coordinates.Count == 0)
        {
            return Task.CompletedTask;
        }

        if (!string.IsNullOrWhiteSpace(playbackBatch.ClientOperationId)
            && _pendingLocalPlaybackOperationIds.TryRemove(playbackBatch.ClientOperationId, out _))
        {
            return Task.CompletedTask;
        }

        Interlocked.Increment(ref _pendingConfirmedPlaybackCount);
        return _confirmedPlaybackChannel.Writer.WriteAsync(ConfirmedPlaybackWorkItem.FromDeletes(playbackBatch)).AsTask();
    }

    private async Task RunBrushFlushLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(BrushFlushInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                bool hasPendingBrushPixels;
                lock (_brushStrokeLock)
                {
                    hasPendingBrushPixels = _pendingBrushPixels.Count > 0;
                }

                if (!hasPendingBrushPixels)
                {
                    continue;
                }

                await InvokeAsync(() => FlushPendingBrushPixelsAsync());
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunConfirmedPlaybackLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _confirmedPlaybackChannel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_confirmedPlaybackChannel.Reader.TryRead(out var workItem))
                {
                    Interlocked.Decrement(ref _pendingConfirmedPlaybackCount);
                    await PlayConfirmedPlaybackAsync(workItem, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PlayConfirmedPlaybackAsync(ConfirmedPlaybackWorkItem workItem, CancellationToken cancellationToken)
    {
        var totalItemCount = workItem.IsDeletion ? workItem.Coordinates.Count : workItem.Pixels.Count;
        if (totalItemCount == 0)
        {
            return;
        }

        var durationMs = NormalizeConfirmedPlaybackDuration(workItem.DurationMs, totalItemCount, Volatile.Read(ref _pendingConfirmedPlaybackCount));
        var stepCount = Math.Max(1, Math.Min(totalItemCount, (int)Math.Ceiling(durationMs / 16d)));
        var delayPerStepMs = stepCount <= 1 ? 0 : Math.Max(1, durationMs / stepCount);
        var processedCount = 0;

        for (var step = 0; step < stepCount && processedCount < totalItemCount; step++)
        {
            var remainingCount = totalItemCount - processedCount;
            var remainingSteps = stepCount - step;
            var sliceCount = Math.Max(1, (int)Math.Ceiling(remainingCount / (double)remainingSteps));

            if (workItem.IsDeletion)
            {
                var slice = workItem.Coordinates.Skip(processedCount).Take(sliceCount).ToList();
                await HandlePixelsDeletedAsync(slice);
            }
            else
            {
                var slice = workItem.Pixels.Skip(processedCount).Take(sliceCount).ToList();
                foreach (var pixel in slice)
                {
                    await HandlePixelUpdatedAsync(pixel, suppressRipple: true);
                }
            }

            processedCount += sliceCount;

            if (delayPerStepMs > 0 && processedCount < totalItemCount)
            {
                await Task.Delay(delayPerStepMs, cancellationToken);
            }
        }
    }

    private static int NormalizeConfirmedPlaybackDuration(int durationMs, int itemCount, int pendingQueueDepth)
    {
        var fallbackDurationMs = Math.Max(16, (int)Math.Ceiling(itemCount * 1000d / 600d));
        var effectiveDurationMs = durationMs > 0 ? durationMs : fallbackDurationMs;
        var maxDurationMs = pendingQueueDepth > 2 ? 32 : pendingQueueDepth > 0 ? 64 : 120;
        return Math.Max(16, Math.Min(effectiveDurationMs, maxDurationMs));
    }

    private void TrackLocalPlaybackOperation(StrokePlaybackMetadataDto? playback)
    {
        if (!string.IsNullOrWhiteSpace(playback?.ClientOperationId))
        {
            _pendingLocalPlaybackOperationIds[playback.ClientOperationId] = 0;
        }
    }

    private void ForgetLocalPlaybackOperation(StrokePlaybackMetadataDto? playback)
    {
        if (!string.IsNullOrWhiteSpace(playback?.ClientOperationId))
        {
            _pendingLocalPlaybackOperationIds.TryRemove(playback.ClientOperationId, out _);
        }
    }

    private StrokePlaybackMetadataDto? CreateStrokePlaybackMetadataLocked()
    {
        if (!_activeStrokeId.HasValue)
        {
            return null;
        }

        var nowUtc = DateTime.UtcNow;
        var startedAtUtc = _strokeChunkStartedAtUtc == default ? nowUtc : _strokeChunkStartedAtUtc;
        _strokeChunkStartedAtUtc = nowUtc;

        return new StrokePlaybackMetadataDto
        {
            ClientOperationId = Guid.NewGuid().ToString("N"),
            StrokeId = _activeStrokeId.Value,
            ChunkSequence = _strokeChunkSequence++,
            ChunkDurationMs = Math.Max(16, (int)Math.Round((nowUtc - startedAtUtc).TotalMilliseconds)),
        };
    }

    private void ResetStrokePlaybackTracking()
    {
        lock (_brushStrokeLock)
        {
            if (_pendingBrushPixels.Count > 0 || _pendingEraseCoordinates.Count > 0)
            {
                return;
            }

            _activeStrokeId = null;
            _strokeChunkSequence = 0;
            _strokeChunkStartedAtUtc = default;
        }
    }

    private async Task HandlePixelsDeletedAsync(IReadOnlyCollection<CoordinateDto>? coordinates)
    {
        if (_canvas == null || coordinates == null || coordinates.Count == 0)
        {
            return;
        }

        var currentCanvasName = _canvas.Name;
        var selectedPixelId = _clickedPixelData?.Id;
        var selectedPixelDeleted = false;
        var uniquePixels = new HashSet<(int X, int Y)>();

        foreach (var coordinate in coordinates)
        {
            if (!uniquePixels.Add((coordinate.X, coordinate.Y)))
            {
                continue;
            }

            lock (_brushStrokeLock)
            {
                _pendingErasePixels.Remove((coordinate.X, coordinate.Y));
                _pendingEraseCoordinates.RemoveAll(item => item.X == coordinate.X && item.Y == coordinate.Y);
                _clearedErasePixels.Add((coordinate.X, coordinate.Y));
            }

            ApiClient.HandlePixelDeleted(currentCanvasName, coordinate.X, coordinate.Y, _canvas.Id);
            _renderer?.EnqueuePixelClear(coordinate.X, coordinate.Y);

            if (_clickedPixel.HasValue && _clickedPixel.Value.X == coordinate.X && _clickedPixel.Value.Y == coordinate.Y)
            {
                selectedPixelDeleted = true;
            }
        }

        if (selectedPixelDeleted)
        {
            if (selectedPixelId.HasValue)
            {
                ApiClient.InvalidateHistoryCache(selectedPixelId.Value);
            }

            _clickedPixelData = null;
        }

        await InvokeAsync(StateHasChanged);
    }

    private Task HandlePixelUpdatedAsync(PixelDto pixel)
    {
        return HandlePixelUpdatedAsync(pixel, suppressRipple: !ShouldAnimateRemoteRipple(pixel));
    }

    private async Task HandlePixelUpdatedAsync(PixelDto pixel, bool suppressRipple)
    {
        if (_renderer == null || _colors == null)
        {
            return;
        }

        var currentCanvasName = _canvas?.Name ?? canvasName;
        var selectedPixelId = _clickedPixel.HasValue && _clickedPixel.Value.X == pixel.X && _clickedPixel.Value.Y == pixel.Y
            ? _clickedPixelData?.Id
            : null;

        lock (_brushStrokeLock)
        {
            _pendingErasePixels.Remove((pixel.X, pixel.Y));
            _pendingEraseCoordinates.RemoveAll(coordinate => coordinate.X == pixel.X && coordinate.Y == pixel.Y);
            _clearedErasePixels.Remove((pixel.X, pixel.Y));
        }

        if (_canvas?.CanvasMode == CanvasMode.Economy)
        {
            ApiClient.InvalidatePixelCache(currentCanvasName, pixel.X, pixel.Y);
            if (selectedPixelId.HasValue)
            {
                ApiClient.InvalidateHistoryCache(selectedPixelId.Value);
            }
        }
        else
        {
            ApiClient.HandlePixelColorChanged(currentCanvasName, pixel.X, pixel.Y, pixel.ColorId, pixel.Id ?? selectedPixelId, pixel.OwnerId);
        }

        var color = _colors.FirstOrDefault(cDto => cDto.Id == pixel.ColorId);
        if (color != null)
        {
            _renderer.EnqueuePixel(pixel.X, pixel.Y, color.HexValue, suppressRipple);
        }

        if (_clickedPixel.HasValue && _clickedPixel.Value.X == pixel.X && _clickedPixel.Value.Y == pixel.Y && _pixelManager != null)
        {
            if (_canvas?.CanvasMode == CanvasMode.Economy)
            {
                _clickedPixelData = await ApiClient.GetPixelData(currentCanvasName, pixel.X, pixel.Y, useCache: false);
            }
            else if (_clickedPixelData != null)
            {
                _clickedPixelData.ColorId = pixel.ColorId;
                _clickedPixelData.Id ??= pixel.Id;
                _clickedPixelData.OwnerId = pixel.OwnerId;
                if (_clickedPixelData.Id.HasValue)
                {
                    ApiClient.InvalidateHistoryCache(_clickedPixelData.Id.Value);
                }
            }

            await _pixelManager.UpdatePixelHistory();
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task RunEraseFlushLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(EraseFlushInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                bool hasPendingErasePixels;
                lock (_brushStrokeLock)
                {
                    hasPendingErasePixels = _pendingEraseCoordinates.Count > 0;
                }

                if (!hasPendingErasePixels)
                {
                    continue;
                }

                await InvokeAsync(() => FlushPendingErasePixelsAsync());
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task FlushPendingBrushPixelsAsync(bool drainAllRemaining = false)
    {
        if (_canvas == null)
        {
            return;
        }

        await _brushFlushSemaphore.WaitAsync();
        try
        {
            while (true)
            {
                List<(int X, int Y, int ColorId, string ColorHex)> batch;
                StrokePlaybackMetadataDto? playback;
                lock (_brushStrokeLock)
                {
                    if (_pendingBrushPixels.Count == 0)
                    {
                        return;
                    }

                    var firstColorId = _pendingBrushPixels[0].ColorId;
                    batch = _pendingBrushPixels
                        .TakeWhile(item => item.ColorId == firstColorId)
                        .Take(BrushBatchSize)
                        .ToList();
                    _pendingBrushPixels.RemoveRange(0, batch.Count);
                    playback = CreateStrokePlaybackMetadataLocked();
                }

                if (batch.Count == 0)
                {
                    return;
                }

                TrackLocalPlaybackOperation(playback);

                var requestVersion = Interlocked.Increment(ref _brushSelectionVersion);

                try
                {
                    var result = _pixelManager != null
                        ? await _pixelManager.PaintBatchAsync(
                            batch.Select(item => new CoordinateDto(item.X, item.Y)).ToList(),
                            batch[0].ColorId,
                            playback: playback)
                        : await ApiClient.PaintBatch(
                            _canvas,
                            batch.Select(item => new CoordinateDto(item.X, item.Y)).ToList(),
                            batch[0].ColorId,
                            playback: playback);

                    if (result.ChangedPixels.Count == 0)
                    {
                        ForgetLocalPlaybackOperation(playback);
                    }

                    var changedCoordinates = result.ChangedPixels
                        .Select(pixel => (pixel.X, pixel.Y))
                        .ToHashSet();
                    var rejectedPixels = batch
                        .Where(item => !changedCoordinates.Contains((item.X, item.Y)))
                        .Select(item => (item.X, item.Y))
                        .ToList();

                    if (rejectedPixels.Count > 0)
                    {
                        await RestoreRejectedBrushPixelsAsync(rejectedPixels);
                    }

                    var lastChangedPixel = result.ChangedPixels.LastOrDefault();
                    if (lastChangedPixel != null)
                    {
                        await UpdateBrushSelectionAsync((lastChangedPixel.X, lastChangedPixel.Y), lastChangedPixel, requestVersion);
                    }
                }
                catch (Exception ex)
                {
                    ForgetLocalPlaybackOperation(playback);
                    _nlog.Warn(ex, "Brush batch paint rejected on canvas {CanvasName}", _canvas.Name);
                    await RestoreRejectedBrushPixelsAsync(batch.Select(item => (item.X, item.Y)));
                    return;
                }

                if (!drainAllRemaining)
                {
                    return;
                }
            }
        }
        finally
        {
            _brushFlushSemaphore.Release();
            ResetStrokePlaybackTracking();
        }
    }

    private async Task FlushPendingErasePixelsAsync(bool drainAllRemaining = false)
    {
        var renderer = _renderer;
        if (_canvas == null || renderer == null)
        {
            return;
        }

        await _eraseFlushSemaphore.WaitAsync();

        try
        {
            while (true)
            {
                List<CoordinateDto> eraseBatch;
                StrokePlaybackMetadataDto? playback;
                lock (_brushStrokeLock)
                {
                    if (_pendingEraseCoordinates.Count == 0)
                    {
                        return;
                    }

                    var queuedBatch = _pendingEraseCoordinates.Take(BrushBatchSize).ToList();
                    _pendingEraseCoordinates.RemoveRange(0, queuedBatch.Count);
                    foreach (var coordinate in queuedBatch)
                    {
                        _pendingErasePixels.Remove((coordinate.X, coordinate.Y));
                    }

                    eraseBatch = queuedBatch
                        .Where(coordinate => !_clearedErasePixels.Contains((coordinate.X, coordinate.Y)))
                        .ToList();

                    playback = eraseBatch.Count > 0 ? CreateStrokePlaybackMetadataLocked() : null;
                }

                if (eraseBatch.Count == 0)
                {
                    if (!drainAllRemaining)
                    {
                        return;
                    }

                    continue;
                }

                var filteredEraseBatch = await renderer.FilterNonWhiteCoordinatesAsync(eraseBatch);
                var filteredEraseKeys = filteredEraseBatch
                    .Select(filtered => (filtered.X, filtered.Y))
                    .ToHashSet();
                var skippedAsWhite = eraseBatch
                    .Where(coordinate => !filteredEraseKeys.Contains((coordinate.X, coordinate.Y)))
                    .ToList();

                RememberClearedPixels(skippedAsWhite);

                if (filteredEraseBatch.Count == 0)
                {
                    if (!drainAllRemaining)
                    {
                        return;
                    }

                    continue;
                }

                var requestVersion = Interlocked.Increment(ref _brushSelectionVersion);

                try
                {
                    TrackLocalPlaybackOperation(playback);
                    var result = await ApiClient.DeleteBatchAsync(_canvas, filteredEraseBatch, playback: playback);
                    if (result.DeletedCoordinates.Count == 0)
                    {
                        ForgetLocalPlaybackOperation(playback);
                    }

                    var deletedCoordinateKeys = result.DeletedCoordinates
                        .Select(coordinate => (coordinate.X, coordinate.Y))
                        .ToHashSet();
                    var rejectedPixels = filteredEraseBatch
                        .Where(item => !deletedCoordinateKeys.Contains((item.X, item.Y)))
                        .Select(item => (item.X, item.Y))
                        .ToList();

                    if (rejectedPixels.Count > 0)
                    {
                        await RestoreRejectedBrushPixelsAsync(rejectedPixels);
                    }

                    if (result.DeletedCoordinates.Count > 0)
                    {
                        await HandlePixelsDeletedAsync(result.DeletedCoordinates);

                        var lastDeletedPixel = result.DeletedCoordinates.LastOrDefault();
                        if (lastDeletedPixel != null)
                        {
                            await UpdateBrushSelectionAsync((lastDeletedPixel.X, lastDeletedPixel.Y), null, requestVersion);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ForgetLocalPlaybackOperation(playback);
                    _nlog.Warn(ex, "Brush batch erase rejected on canvas {CanvasName}", _canvas.Name);
                    await RestoreRejectedBrushPixelsAsync(filteredEraseBatch.Select(item => (item.X, item.Y)));
                    return;
                }

                if (!drainAllRemaining)
                {
                    return;
                }
            }
        }
        finally
        {
            _eraseFlushSemaphore.Release();
            ResetStrokePlaybackTracking();
        }
    }

    private IEnumerable<CoordinateDto> GetEraserCoordinates(int x, int y)
    {
        if (_canvas == null)
        {
            yield break;
        }

        var half = _selectedEraserSize / 2;
        for (var dx = -half; dx <= half; dx++)
        {
            var targetX = x + dx;
            if (targetX < 0 || targetX >= _canvas.Width)
            {
                continue;
            }

            for (var dy = -half; dy <= half; dy++)
            {
                var targetY = y + dy;
                if (targetY < 0 || targetY >= _canvas.Height)
                {
                    continue;
                }

                yield return new CoordinateDto(targetX, targetY);
            }
        }
    }

    private void RememberClearedPixels(IReadOnlyCollection<CoordinateDto> coordinates)
    {
        if (_canvas == null || coordinates.Count == 0)
        {
            return;
        }

        foreach (var coordinate in coordinates)
        {
            lock (_brushStrokeLock)
            {
                _pendingErasePixels.Remove((coordinate.X, coordinate.Y));
                _pendingEraseCoordinates.RemoveAll(item => item.X == coordinate.X && item.Y == coordinate.Y);
                _clearedErasePixels.Add((coordinate.X, coordinate.Y));
            }

            ApiClient.HandlePixelDeleted(_canvas.Name, coordinate.X, coordinate.Y, _canvas.Id);
        }
    }

    private async Task RestoreRejectedBrushPixelsAsync(IEnumerable<(int X, int Y)> coordinates)
    {
        foreach (var (x, y) in coordinates.Distinct())
        {
            await RestoreRejectedBrushPixelAsync(x, y);
        }
    }

    private bool HasPendingLocalPixelMutation(int x, int y)
    {
        lock (_brushStrokeLock)
        {
            return _pendingErasePixels.Contains((x, y))
                || _pendingBrushPixels.Any(pixel => pixel.X == x && pixel.Y == y);
        }
    }

    private async Task RestoreRejectedBrushPixelAsync(int x, int y)
    {
        if (_canvas == null || HasPendingLocalPixelMutation(x, y))
        {
            return;
        }

        PixelDto? actualPixel = null;

        try
        {
            actualPixel = await ApiClient.GetPixelData(_canvas.Name, x, y, useCache: false);
        }
        catch (Exception reloadEx)
        {
            _nlog.Warn(reloadEx, "Failed to reload pixel data after rejected brush paint at ({X}, {Y}) on canvas {CanvasName}", x, y, _canvas.Name);
        }

        if (HasPendingLocalPixelMutation(x, y))
        {
            return;
        }

        if (actualPixel != null)
        {
            var restoredColor = _colors?.FirstOrDefault(color => color.Id == actualPixel.ColorId);
            if (restoredColor != null && _renderer != null)
            {
                _renderer.EnqueuePixel(x, y, restoredColor.HexValue, suppressRipple: true);
            }

            if (actualPixel.Id.HasValue)
            {
                ApiClient.InvalidateHistoryCache(actualPixel.Id.Value);
            }

            if (_clickedPixel.HasValue && _clickedPixel.Value.X == x && _clickedPixel.Value.Y == y)
            {
                _clickedPixelData = actualPixel;
                await InvokeAsync(StateHasChanged);
            }

            return;
        }

        await LoadCanvasImage();

        if (_clickedPixel.HasValue && _clickedPixel.Value.X == x && _clickedPixel.Value.Y == y)
        {
            _clickedPixelData = null;
            await InvokeAsync(StateHasChanged);
        }
    }

    private bool ShouldAnimateRemoteRipple(PixelDto pixel)
    {
        return pixel.OwnerId.HasValue
            && _currentUserId.HasValue
            && pixel.OwnerId.Value != _currentUserId.Value;
    }
}

