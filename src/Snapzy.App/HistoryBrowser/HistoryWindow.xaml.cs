using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Snapzy.Core.History;
using Snapzy.Core.Localization;
using Snapzy.Core.Settings;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
using TextBox = System.Windows.Controls.TextBox;
using Brushes = System.Windows.Media.Brushes;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MessageBox = System.Windows.MessageBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Color = System.Windows.Media.Color;

namespace Snapzy.App.HistoryBrowser;

public partial class HistoryWindow : Window
{
    private static HistoryWindow? _current;

    private readonly HistoryStore _store;
    private readonly AppSettings _settings;
    private bool _initialized;

    private HistoryWindow(HistoryStore store, AppSettings settings)
    {
        InitializeComponent();
        _store = store;
        _settings = settings;
        Title = Strings.Get("Hist_Title");
        SearchBox.ToolTip = Strings.Get("Hist_Search");
        EmptyText.Text = Strings.Get("Hist_Empty");
        foreach (var key in new[] { "Hist_FilterAll", "Hist_FilterImages", "Hist_FilterVideos", "Hist_FilterGifs" })
            TypeFilter.Items.Add(Strings.Get(key));
        TypeFilter.SelectedIndex = 0;
        _initialized = true;
        Activated += (_, _) => Refresh();
        Closed += (_, _) => { if (_current == this) _current = null; };
        Refresh();
    }

    public static void Open(HistoryStore store, AppSettings settings)
    {
        if (_current is not null)
        {
            _current.Activate();
            return;
        }
        _current = new HistoryWindow(store, settings);
        _current.Show();
        _current.Activate();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_initialized) Refresh();
    }

    private void Refresh()
    {
        var search = SearchBox.Text.Trim();
        var typeIndex = TypeFilter.SelectedIndex; // 0 all, 1 images, 2 videos, 3 gifs
        var entries = _store.List()
            .Where(en => typeIndex switch
            {
                1 => en.Type == "image",
                2 => en.Type == "video",
                3 => en.Type == "gif",
                _ => true,
            })
            .Where(en => search.Length == 0 || en.FileName.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Cards.Items.Clear();
        foreach (var entry in entries)
            Cards.Items.Add(MakeCard(entry));
        EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement MakeCard(HistoryEntry entry)
    {
        var thumb = new Image
        {
            Source = ThumbnailLoader.GetThumb(entry, _store, 200),
            Stretch = Stretch.Uniform,
            Height = 100,
        };
        var name = new TextBlock
        {
            Text = entry.FileName,
            Foreground = Brushes.White,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 4, 2, 0),
        };
        var time = new TextBlock
        {
            Text = entry.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            Foreground = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)),
            FontSize = 10,
            Margin = new Thickness(2, 1, 2, 2),
        };
        var stack = new StackPanel();
        stack.Children.Add(thumb);
        stack.Children.Add(name);
        stack.Children.Add(time);

        var card = new Border
        {
            Width = 160,
            Margin = new Thickness(4),
            Padding = new Thickness(6),
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            CornerRadius = new CornerRadius(6),
            Child = stack,
            Tag = entry,
        };

        var menu = new ContextMenu();
        if (entry.Type == "image")
        {
            menu.Items.Add(MakeMenuItem(Strings.Get("QA_Copy"), () =>
                CaptureFlow.CopyImageToClipboard(_store.GetFullPath(entry))));
            menu.Items.Add(MakeMenuItem(Strings.Get("QA_Annotate"), () =>
                AppActions.OpenAnnotate(_store.GetFullPath(entry))));
            menu.Items.Add(MakeMenuItem(Strings.Get("Hist_OcrCopy"), () => OcrToClipboard(entry, tableMode: false)));
            menu.Items.Add(MakeMenuItem(Strings.Get("Hist_OcrTableCopy"), () => OcrToClipboard(entry, tableMode: true)));
        }
        menu.Items.Add(MakeMenuItem(Strings.Get("QA_Open"), () => OpenFile(entry)));
        menu.Items.Add(MakeMenuItem(Strings.Get("QA_Folder"), () =>
            Process.Start("explorer.exe", $"/select,\"{_store.GetFullPath(entry)}\"")));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem(Strings.Get("QA_Delete"), () => DeleteWithConfirm(entry)));
        card.ContextMenu = menu;

        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2) return;
            if (entry.Type == "image") AppActions.OpenAnnotate(_store.GetFullPath(entry));
            else OpenFile(entry);
        };
        return card;
    }

    private static MenuItem MakeMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { Snapzy.Core.Log.Error("History action failed", ex); }
        };
        return item;
    }

    private async void OcrToClipboard(HistoryEntry entry, bool tableMode)
    {
        try
        {
            if (!Snapzy.Core.Ocr.OcrService.IsAvailable)
            {
                AppActions.Tray?.Balloon("Snapzy", Strings.Get("Toast_OcrUnavailable"));
                return;
            }
            using var bmp = new System.Drawing.Bitmap(_store.GetFullPath(entry));
            var ocr = tableMode
                ? await Snapzy.Core.Ocr.OcrService.RecognizeTableAsync(bmp)
                : new Snapzy.Core.Ocr.OcrClipboardResult(
                    await Snapzy.Core.Ocr.OcrService.RecognizeBitmapAsync(bmp), false, 0, 0);
            if (string.IsNullOrWhiteSpace(ocr.Text))
            {
                AppActions.Tray?.Balloon("Snapzy", Strings.Get("Toast_OcrEmpty"));
                return;
            }
            System.Windows.Clipboard.SetText(ocr.Text);
            AppActions.Tray?.Balloon("Snapzy", ocr.IsTable
                ? string.Format(Strings.Get("Toast_OcrTableCopied"), ocr.Rows, ocr.Columns)
                : Strings.Get("Toast_OcrCopied"));
        }
        catch (Exception ex)
        {
            Snapzy.Core.Log.Error("History OCR failed", ex);
            AppActions.Tray?.Balloon("Snapzy", Strings.Get("Toast_CaptureFailed"));
        }
    }

    private void OpenFile(HistoryEntry entry) =>
        Process.Start(new ProcessStartInfo(_store.GetFullPath(entry)) { UseShellExecute = true });

    private void DeleteWithConfirm(HistoryEntry entry)
    {
        var pick = MessageBox.Show(this,
            string.Format(Strings.Get("Hist_DeleteConfirm"), entry.FileName),
            Strings.Get("Hist_Title"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (pick != MessageBoxResult.Yes) return;
        _store.Delete(entry.Id);
        Refresh();
    }
}
