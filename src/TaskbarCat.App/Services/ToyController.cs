using System.Windows;
using TaskbarCat.App.Interop;
using TaskbarCat.App.Views;
using TaskbarCat.Models;
using TaskbarCat.Services;

// UseWPF and UseWindowsForms are both on; pin the one this file means.
using Application = System.Windows.Application;

namespace TaskbarCat.App.Services;

/// <summary>
/// Toy mode: the pointer becomes a toy and the cat chases it.
///
/// Owns the three pieces of global state this feature needs — the hidden system cursor, the
/// overlay window and the low-level mouse hook — so there is exactly one place that turns them
/// all on and exactly one that turns them all off. Anything that can end toy mode calls
/// <see cref="Stop"/>; nothing reaches past it.
/// </summary>
internal sealed class ToyController : IDisposable
{
    private readonly IReadOnlyDictionary<string, ToySprite> _sprites;
    private readonly ToyChase _chase = new();
    private readonly MouseHookService _hook;
    private ToyOverlayWindow? _overlay;

    public ToyController(string assetsRoot)
    {
        _sprites = ToySprites.Load(assetsRoot);
        _hook = new MouseHookService(() => Application.Current?.Dispatcher.BeginInvoke(Stop));
    }

    public ToyKind Active { get; private set; } = ToyKind.None;

    public bool IsActive => Active != ToyKind.None;

    /// <summary>Raised when the mode ends, however it ended.</summary>
    public event Action? Stopped;

    /// <summary>True if there is art for this toy; false means the assets are missing.</summary>
    public bool CanStart(ToyKind kind) => _sprites.ContainsKey(NameOf(kind));

    public void Start(ToyKind kind)
    {
        if (kind == ToyKind.None) { Stop(); return; }
        if (!_sprites.TryGetValue(NameOf(kind), out var sprite)) return;

        Active = kind;
        _chase.Reset();

        _overlay ??= new ToyOverlayWindow();
        _overlay.SetToy(sprite);
        _overlay.Show();

        ToyCursorService.Hide();
        _hook.Install();
    }

    public void Stop()
    {
        if (!IsActive) return;

        Active = ToyKind.None;
        _chase.Reset();

        // Order matters on the way out: unhook first so no click can arrive mid-teardown, and
        // put the cursor back before hiding the overlay so there is never a frame with neither
        // a pointer nor a toy on screen.
        _hook.Uninstall();
        ToyCursorService.Restore();
        _overlay?.Hide();

        Stopped?.Invoke();
    }

    /// <summary>
    /// One frame of play. Returns the cat's reaction, or null when toy mode is off.
    /// </summary>
    public ToyDecision? Tick(TimeSpan dt, double catCentreX, double catTop, double catBottom,
        double railLeft, double railRight)
    {
        if (!IsActive || _overlay is null) return null;
        if (!Win32.GetCursorPos(out var p)) return null;

        _overlay.Follow(p.X, p.Y, dt);

        var frame = new ToyFrame(p.X, p.Y, catCentreX, catTop, catBottom, railLeft, railRight);
        return _chase.Update(dt, Active, frame);
    }

    public bool InReaction => _chase.InReaction;

    private static string NameOf(ToyKind kind) => kind switch
    {
        ToyKind.Yarn => "yarn",
        ToyKind.Laser => "laser",
        _ => "",
    };

    public void Dispose()
    {
        Stop();
        _hook.Dispose();
        _overlay?.Close();
        _overlay = null;
    }
}
