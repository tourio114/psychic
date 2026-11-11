using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Forms = System.Windows.Forms;

namespace PsychicIsland;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Forms.NotifyIcon tray;
    private readonly Preferences preferences = Preferences.Load();
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private bool busy, expanded, closed, commandBusy, transitioning, pointerDown;
    private Point pressPoint;
    private int transitionVersion;
    private double anchor;
    private string artworkKey = "";

    public MainWindow()
    {
        InitializeComponent();
        Island.LostMouseCapture += (_, _) => pointerDown = false;
        ContextMenu = BuildMenu();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示灵动岛", null, (_, _) => Dispatcher.Invoke(() => { Show(); Activate(); }));
        menu.Items.Add("重置到顶部中央", null, (_, _) => Dispatcher.Invoke(ResetPosition));
        menu.Items.Add("12 / 24 小时制", null, (_, _) => Dispatcher.Invoke(ToggleClock));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close));
        tray = new Forms.NotifyIcon { Text = "Psychic Island · 灵动岛", Icon = System.Drawing.SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); Activate(); });
        Loaded += (_, _) =>
        {
            if (preferences.Center is double center && preferences.Top is double top)
            {
                anchor = center; Left = center - Width / 2; Top = top; ClampPosition();
            }
            else ResetPosition();
            UpdateClock();
            tick.Start();
            _ = RefreshMedia();
        };
        tick.Tick += (_, _) => { UpdateClock(); _ = RefreshMedia(); };
        Deactivated += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (!IsActive && ContextMenu?.IsOpen != true) SetExpanded(false);
        });
        KeyDown += (_, e) => { if (e.Key == Key.Escape) SetExpanded(false); };
        Closed += (_, _) => { closed = true; tick.Stop(); SystemParameters.StaticPropertyChanged -= OnDisplaySettingsChanged; tray.Dispose(); SavePosition(); };
        SystemParameters.StaticPropertyChanged += OnDisplaySettingsChanged;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        void Add(string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("重置到顶部中央", ResetPosition);
        Add("切换 12 / 24 小时制", ToggleClock);
        Add("隐藏（双击托盘图标恢复）", Hide);
        menu.Items.Add(new Separator());
        Add("退出灵动岛", Close);
        return menu;
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        Clock.Text = now.ToString(preferences.Use24Hour ? "HH:mm:ss" : "hh:mm:ss tt");
        DateText.Text = now.ToString("yyyy 年 M 月 d 日 · dddd");
    }
    private void ToggleClock() { preferences.Use24Hour = !preferences.Use24Hour; preferences.Save(); UpdateClock(); }
    private void ResetPosition()
    {
        anchor = SystemParameters.WorkArea.Left + SystemParameters.WorkArea.Width / 2;
        Left = anchor - Width / 2; Top = SystemParameters.WorkArea.Top + 8;
        SavePosition();
    }
    private void ClampPosition()
    {
        // Virtual desktop coordinates are WPF device-independent units.
        Left = Math.Clamp(Left, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width));
        Top = Math.Clamp(Top, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height));
        anchor = Left + Width / 2;
    }
    private void SavePosition() { preferences.Center = anchor; preferences.Top = Top; preferences.Save(); }
    private void OnDisplaySettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (closed) { SystemParameters.StaticPropertyChanged -= OnDisplaySettingsChanged; return; }
        if (e.PropertyName is nameof(SystemParameters.WorkArea) or nameof(SystemParameters.VirtualScreenWidth) or nameof(SystemParameters.VirtualScreenHeight))
            Dispatcher.BeginInvoke(() => { ClampPosition(); SavePosition(); });
    }
    private void SetExpanded(bool value)
    {
        if (expanded == value) return;
        expanded = value;
        transitioning = true;
        int version = ++transitionVersion;
        // The native host resizes only at transition boundaries. The fixed content
        // layout stays unchanged while WPF animates a lightweight rounded clip.
        if (value) { Height = 300; Details.Visibility = Visibility.Visible; }
        Details.IsHitTestVisible = value;
        var duration = TimeSpan.FromMilliseconds(value ? 380 : 280);
        var easing = new QuinticEase { EasingMode = EasingMode.EaseOut };
        var shape = new RectAnimation
        {
            From = PillClip.Rect,
            To = value ? new Rect(0, 0, 352, 280) : new Rect(60, 0, 232, 52),
            Duration = duration, EasingFunction = easing
        };
        shape.Completed += (_, _) =>
        {
            if (closed || version != transitionVersion) return;
            transitioning = false;
            if (!expanded) { Details.Visibility = Visibility.Hidden; Height = 72; }
            _ = RefreshMedia();
        };
        PillClip.BeginAnimation(RectangleGeometry.RectProperty, shape);
        Animate(ClockOffset, TranslateTransform.XProperty, value ? 0 : 60, duration, easing);
        Animate(StatusOffset, TranslateTransform.XProperty, value ? 0 : -60, duration, easing);
        Animate(DetailsOffset, TranslateTransform.YProperty, value ? 0 : 12, duration, easing);
        var fade = new DoubleAnimation(value ? 1 : 0, TimeSpan.FromMilliseconds(value ? 220 : 100))
        {
            From = Details.Opacity,
            BeginTime = TimeSpan.FromMilliseconds(value ? 80 : 0),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Details.BeginAnimation(OpacityProperty, fade);
    }
    private static void Animate(Animatable target, DependencyProperty property, double value, TimeSpan duration, IEasingFunction easing)
        => target.BeginAnimation(property, new DoubleAnimation((double)target.GetValue(property), value, duration) { EasingFunction = easing });

    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (e.GetPosition(Island).Y > 52) return;
        Activate();
        pressPoint = e.GetPosition(this);
        pointerDown = true;
        Island.CaptureMouse();
        e.Handled = true;
    }
    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (!pointerDown || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        pointerDown = false;
        Island.ReleaseMouseCapture();
        try { DragMove(); } catch (InvalidOperationException) { }
        ClampPosition(); SavePosition();
    }
    private void OnPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (!pointerDown) return;
        pointerDown = false;
        Island.ReleaseMouseCapture();
        if (e.GetPosition(Island).Y is >= 0 and <= 52 && PillClip.FillContains(e.GetPosition(Island)))
            SetExpanded(!expanded);
        e.Handled = true;
    }

    private async Task RefreshMedia()
    {
        if (busy || closed || transitioning) return;
        busy = true;
        try
        {
            manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var current = manager.GetCurrentSession() ?? manager.GetSessions().FirstOrDefault();
            if (closed) return;
            session = current;
            if (current is null) { ClearMedia("播放音乐后，即可在这里控制"); return; }
            var info = current.GetPlaybackInfo();
            bool playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            Play.Content = playing ? "Ⅱ" : "▶";
            Play.IsEnabled = !commandBusy && (playing ? info.Controls.IsPauseEnabled : info.Controls.IsPlayEnabled);
            Previous.IsEnabled = !commandBusy && info.Controls.IsPreviousEnabled;
            Next.IsEnabled = !commandBusy && info.Controls.IsNextEnabled;
            Dot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(playing ? "#A998FF" : "#A6ADC0"));
            CompactStatus.Text = playing ? "♫" : "◷";
            var properties = await current.TryGetMediaPropertiesAsync();
            if (closed || transitioning) return;
            Track.Text = string.IsNullOrWhiteSpace(properties.Title) ? "系统媒体播放器" : properties.Title;
            Artist.Text = string.IsNullOrWhiteSpace(properties.Artist) ? current.SourceAppUserModelId : properties.Artist;
            Track.ToolTip = Track.Text; Artist.ToolTip = Artist.Text;
            var timeline = current.GetTimelineProperties();
            var total = timeline.EndTime - timeline.StartTime;
            var position = timeline.Position - timeline.StartTime;
            if (playing && timeline.LastUpdatedTime != default)
                position += TimeSpan.FromSeconds(Math.Max(0, (DateTimeOffset.Now - timeline.LastUpdatedTime).TotalSeconds) * (info.PlaybackRate ?? 1));
            position = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, Math.Max(0, total.TotalSeconds)));
            Progress.Value = total.TotalSeconds > 0 ? position.TotalSeconds / total.TotalSeconds : 0;
            Elapsed.Text = FormatTime(position); DurationText.Text = FormatTime(total);
            Hint.Text = "点击顶部收起 · 拖动移动 · 右键菜单";
            string key = current.SourceAppUserModelId + "|" + properties.Title + "|" + properties.Artist + "|" + properties.AlbumTitle;
            if (key != artworkKey)
            {
                Artwork.Source = null;
                if (properties.Thumbnail is not null)
                {
                    using var stream = await properties.Thumbnail.OpenReadAsync();
                    using var input = stream.AsStreamForRead();
                    var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 96; bitmap.StreamSource = input; bitmap.EndInit(); bitmap.Freeze();
                    if (!closed) Artwork.Source = bitmap;
                }
                artworkKey = key;
            }
        }
        catch (Exception ex)
        {
            session = null;
            ClearMedia("媒体连接暂不可用，将自动重试");
            Hint.ToolTip = ex.Message;
            manager = null;
        }
        finally { busy = false; }
    }
    private void ClearMedia(string message)
    {
        Track.Text = "现在，享受片刻宁静"; Artist.Text = message;
        Play.IsEnabled = Previous.IsEnabled = Next.IsEnabled = false;
        Play.Content = "▶"; CompactStatus.Text = "◷";
        Dot.Fill = new SolidColorBrush(Color.FromRgb(166, 173, 192));
        Artwork.Source = null; artworkKey = "";
        Progress.Value = 0; Elapsed.Text = DurationText.Text = "0:00";
    }
    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
    private async Task Command(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action)
    {
        if (session is not { } target || commandBusy) return;
        commandBusy = true;
        Play.IsEnabled = Previous.IsEnabled = Next.IsEnabled = false;
        try { if (!await action(target)) Hint.Text = "播放器未接受操作，请稍后重试"; }
        catch (Exception) { Hint.Text = "播放器已断开，将自动重新连接"; }
        finally { commandBusy = false; }
        await RefreshMedia();
    }
    private async void OnPlay(object sender, RoutedEventArgs e) => await Command(async s => await s.TryTogglePlayPauseAsync());
    private async void OnPrevious(object sender, RoutedEventArgs e) => await Command(async s => await s.TrySkipPreviousAsync());
    private async void OnNext(object sender, RoutedEventArgs e) => await Command(async s => await s.TrySkipNextAsync());
}

public sealed class Preferences
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PsychicIsland", "settings.json");
    public bool Use24Hour { get; set; } = true;
    public double? Center { get; set; }
    public double? Top { get; set; }
    public static Preferences Load()
    {
        try
        {
            var result = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new();
            if (result.Center is double c && !double.IsFinite(c)) result.Center = null;
            if (result.Top is double t && !double.IsFinite(t)) result.Top = null;
            return result;
        }
        catch { return new(); }
    }
    public void Save()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
