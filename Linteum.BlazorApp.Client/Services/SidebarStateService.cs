namespace Linteum.BlazorApp.Client;

/// <summary>
/// Scoped service for cross-component communication between CanvasSidebar (interactive
/// island inside SSR layout) and other interactive components (pages, CanvasPage).
/// Replaces the old EventCallback and @ref-based patterns that cannot cross SSR/interactive boundaries.
/// </summary>
public class SidebarStateService
{
    public string CurrentMargin { get; private set; } = "300px";

    public event Action<string>? MarginChanged;
    public event Func<Task>? CanvasListChanged;

    public void NotifyMarginChanged(string margin)
    {
        CurrentMargin = margin;
        MarginChanged?.Invoke(margin);
    }

    public async Task NotifyCanvasListChangedAsync()
    {
        if (CanvasListChanged is not null)
            await CanvasListChanged.Invoke();
    }
}

