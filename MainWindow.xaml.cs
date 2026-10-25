using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Apathy.Models;
using Apathy.Services;

namespace Apathy;

public partial class MainWindow : Window
{
    private const double MinMenuWidth = 640;
    private const double MinMenuHeight = 480;
    private const string DiscordInvite = "https://discord.com/invite/apathy";

    private readonly List<AppItem> _items = new();
    private readonly List<CheckBox> _groupToggles = new();
    private readonly List<Grid> _categoryPages = new();
    private bool _fitted;
    private readonly Dictionary<AppItem, CheckBox> _boxes = new();
    private Hardware? _hardware;
    private TextBlock? _cpuText, _gpuText, _boardText;
    private const int ParallelDownloads = 3;
    private bool _fastDownloadsSet;
    // Search tab
    private readonly Dictionary<AppItem, string> _where = new();
    private readonly List<AppItem> _wingetItems = new();
    private TabItem? _searchTab;
    private TextBox? _searchBox;
    private StackPanel? _searchResults, _wingetResults;
    private TextBlock? _wingetStatus;
    private Button? _wingetButton;
    // winget versions per package id, fetched the first time the menu opens
    private readonly Dictionary<string, List<string>> _versions = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cancel;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();

        // Drag from any spot that isn't a control (controls mark the click as handled).
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                FocusSearch();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // Esc in a filled search box clears it; anywhere else it closes the window.
                if (_searchBox is { IsKeyboardFocused: true, Text.Length: > 0 }) _searchBox.Clear();
                else Close();
                e.Handled = true;
            }
        };
        SourceInitialized += (_, _) => Native.DisableRoundedCorners(new WindowInteropHelper(this).Handle);
        Closing += (_, e) =>
        {
            if (_busy && MessageBox.Show(this, "Installation is still running. Close anyway?", "apathy",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
            }
        };

        var categories = Catalog.Load(out string source, out string? error);
        int index = 0;
        foreach (var category in categories)
        {
            MainTabs.Items.Insert(index++, BuildCategoryTab(category));
        }
        MainTabs.Items.Insert(0, BuildSearchTab());
        MainTabs.SelectedIndex = 1;

        SelectedList.ItemsSource = _items.Where(i => i.IsChecked).ToList();
        WriteLog($"Loaded {_items.Count} programs ({source}).");
        if (error != null)
        {
            WriteLog($"Could not read {Catalog.FileName}: {error}. Using the built-in list.");
        }
        RefreshSelection();
        Loaded += async (_, _) =>
        {
            FitToContent();
            await DetectHardwareAsync();
            // Detected part names and "(your PC)" can make the Drivers tab wider.
            FitToContent();
        };
    }

    // ---------- Menu ----------

    private TabItem BuildCategoryTab(Category category)
    {
        // Two columns of group boxes, like the apathy pages; taller groups get more room.
        var page = new Grid();
        page.ColumnDefinitions.Add(new ColumnDefinition());
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        page.ColumnDefinitions.Add(new ColumnDefinition());
        _categoryPages.Add(page);
        var columns = new[] { new Grid(), new Grid() };
        Grid.SetColumn(columns[1], 2);
        page.Children.Add(columns[0]);
        page.Children.Add(columns[1]);

        var groups = new List<(GroupBox Box, int Rows)>();
        if (category.DetectHardware) groups.Add((BuildHardwareGroup(), 9));
        groups.AddRange(category.Groups.Select(g => (BuildGroup(g, category.Name), g.Programs.Count + 3)));

        for (int i = 0; i < groups.Count; i++)
        {
            var column = columns[i % 2];
            if (column.Children.Count > 0)
            {
                column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            }
            column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(groups[i].Rows, GridUnitType.Star) });
            Grid.SetRow(groups[i].Box, column.RowDefinitions.Count - 1);
            column.Children.Add(groups[i].Box);
        }

        return new TabItem
        {
            Header = GlyphFromHex(category.Icon),
            ToolTip = category.Name,
            Content = page,
        };
    }

    private GroupBox BuildGroup(Subcategory subcategory, string categoryName)
    {
        var panel = new StackPanel();
        var all = new CheckBox
        {
            Content = "Select all",
            Foreground = (Brush)FindResource("Sk.TextMuted"),
        };
        panel.Children.Add(all);
        panel.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 0, 30, 6),
            Background = (Brush)FindResource("Sk.Line"),
        });
        _groupToggles.Add(all);

        var boxes = new List<CheckBox>();
        foreach (var program in subcategory.Programs)
        {
            var item = new AppItem(program);
            _items.Add(item);
            _where[item] = $"{categoryName} / {subcategory.Name}";
            var box = new CheckBox
            {
                Content = program.Name,
                ToolTip = program.Id,
                DataContext = item,
            };
            box.SetBinding(ToggleButton.IsCheckedProperty, nameof(AppItem.IsChecked));
            box.Click += (_, _) => RefreshSelection();
            boxes.Add(box);
            _boxes[item] = box;
            panel.Children.Add(box);
        }

        all.Click += (_, _) =>
        {
            foreach (var box in boxes) ((AppItem)box.DataContext).IsChecked = all.IsChecked == true;
            RefreshSelection();
        };

        return new GroupBox { Header = subcategory.Name, Content = panel };
    }

    /// <summary>"This PC": detected processor, graphics and motherboard, plus a button that ticks their drivers.</summary>
    private GroupBox BuildHardwareGroup()
    {
        var panel = new StackPanel();
        TextBlock AddRow(string label)
        {
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Style = (Style)FindResource("Sk.Label"),
                Margin = new Thickness(20, 0, 30, 1),
            });
            var value = new TextBlock
            {
                Text = "detecting...",
                Style = (Style)FindResource("Sk.Label"),
                Margin = new Thickness(20, 0, 30, 6),
                Foreground = (Brush)FindResource("Sk.TextMuted"),
                TextWrapping = TextWrapping.Wrap,
            };
            panel.Children.Add(value);
            return value;
        }

        _cpuText = AddRow("Processor");
        _gpuText = AddRow("Graphics card");
        _boardText = AddRow("Motherboard");
        var select = new Button { Content = "Select drivers for this PC" };
        select.Click += SelectDrivers_Click;
        panel.Children.Add(select);
        return new GroupBox { Header = "This PC", Content = panel };
    }

    private async Task DetectHardwareAsync()
    {
        try
        {
            _hardware = await Hardware.DetectAsync();
        }
        catch (Exception ex)
        {
            Log.Write("Hardware detection failed: " + ex);
            _hardware = new Hardware();
        }

        string Show(IEnumerable<string> names) => names.Any() ? string.Join("\n", names) : "unknown";
        if (_cpuText != null) _cpuText.Text = Show(_hardware.Processors);
        if (_gpuText != null) _gpuText.Text = Show(_hardware.GraphicsCards);
        if (_boardText != null) _boardText.Text = _hardware.Motherboard.Length > 0 ? _hardware.Motherboard : "unknown";

        foreach (var pair in _boxes.Where(p => _hardware.Matches(p.Key.Program.Detect)))
        {
            pair.Value.Content = pair.Key.Name + "  (your PC)";
        }
        WriteLog("Hardware: " + string.Join(", ", _hardware.Processors.Concat(_hardware.GraphicsCards)
            .Concat(new[] { _hardware.Motherboard }).Where(n => n.Length > 0)));
    }

    private void SelectDrivers_Click(object sender, RoutedEventArgs e)
    {
        if (_hardware == null) return;
        var matches = _items.Where(i => _hardware.Matches(i.Program.Detect)).ToList();
        if (matches.Count == 0)
        {
            MessageBox.Show(this, "No matching drivers found for this PC.\nPick them by hand, or use Snappy Driver Installer.",
                "apathy", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        foreach (var item in matches) item.IsChecked = true;
        RefreshSelection();
        WriteLog($"Selected {matches.Select(i => i.Program.Id).Distinct().Count()} drivers for this PC.");
    }

    private void Discord_Click(object sender, RoutedEventArgs e) => OpenUrl(DiscordInvite);

    /// <summary>Opens a link through Explorer, so the browser doesn't run as administrator like this app does.</summary>
    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start("explorer.exe", "\"" + url + "\"");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private static string GlyphFromHex(string hex) =>
        int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code)
            ? char.ConvertFromUtf32(code)
            : "";

    // The same program can sit in two groups (Intel DSA): install it once.
    // Silent winget installs go first, then installers that open their own window, then ISO downloads,
    // so nothing waits behind a window the user hasn't clicked through yet.
    private List<AppItem> Selected() =>
        _items.Where(i => i.IsChecked).GroupBy(i => i.Program.Id).Select(g => g.First())
            .OrderBy(i => !i.Program.IsDirect ? 0 : i.Program.Save ? 2 : 1)
            .ToList();

    private void RefreshSelection()
    {
        var selected = Selected();
        foreach (var item in _items.Where(i => !i.IsChecked)) item.State = InstallState.None;
        SelectedList.ItemsSource = selected;
        NothingSelected.Visibility = selected.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectedGroup.Header = selected.Count == 0 ? "Selected programs" : $"Selected programs ({selected.Count})";
        InstallButton.Content = selected.Count == 0 ? "Install selected" : $"Install selected ({selected.Count})";
    }

    private void Recommended_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items.Where(i => i.Program.Recommended)) item.IsChecked = true;
        RefreshSelection();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items) item.IsChecked = false;
        foreach (var toggle in _groupToggles) toggle.IsChecked = false;
        RefreshSelection();
    }

    // ---------- Search ----------

    private TabItem BuildSearchTab()
    {
        var muted = (Brush)FindResource("Sk.TextMuted");

        // Left: the built-in list, filtered while typing.
        _searchBox = new TextBox();
        var placeholder = new TextBlock
        {
            Text = "Type a program name...",
            Foreground = muted,
            IsHitTestVisible = false,
            Margin = new Thickness(25, 0, 0, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _searchBox.TextChanged += (_, _) =>
        {
            placeholder.Visibility = _searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            RunLocalSearch();
        };
        _searchBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) await SearchWingetAsync();
        };
        var boxRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        boxRow.Children.Add(_searchBox);
        boxRow.Children.Add(placeholder);

        _searchResults = new StackPanel();
        var left = new StackPanel();
        left.Children.Add(boxRow);
        left.Children.Add(_searchResults);

        // Right: anything else from the winget catalog.
        _wingetButton = new Button { Content = "Search winget" };
        _wingetButton.Click += async (_, _) => await SearchWingetAsync();
        _wingetStatus = new TextBlock
        {
            Style = (Style)FindResource("Sk.Label"),
            Margin = new Thickness(20, 0, 30, 6),
            Foreground = muted,
            TextWrapping = TextWrapping.Wrap,
            Text = "Not in the list? Search the whole winget catalog, thousands of programs. Enter in the search box does the same.",
        };
        _wingetResults = new StackPanel();
        var right = new StackPanel();
        right.Children.Add(_wingetStatus);
        right.Children.Add(_wingetButton);
        right.Children.Add(_wingetResults);

        var page = new Grid();
        page.ColumnDefinitions.Add(new ColumnDefinition());
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        page.ColumnDefinitions.Add(new ColumnDefinition());
        var leftGroup = new GroupBox { Header = "Search", Content = left };
        var rightGroup = new GroupBox { Header = "More from winget", Content = right };
        Grid.SetColumn(rightGroup, 2);
        page.Children.Add(leftGroup);
        page.Children.Add(rightGroup);

        RunLocalSearch();
        _searchTab = new TabItem { Header = "\uE721", ToolTip = "Search (Ctrl+F)", Content = page };
        _searchTab.GotFocus += (_, e) =>
        {
            if (e.OriginalSource == _searchTab) Dispatcher.InvokeAsync(() => _searchBox.Focus());
        };
        return _searchTab;
    }

    private void FocusSearch()
    {
        if (_searchTab == null || _searchBox == null) return;
        _searchTab.IsSelected = true;
        Dispatcher.InvokeAsync(() =>
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private static bool Has(string text, string query) =>
        text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    private void RunLocalSearch()
    {
        if (_searchBox == null || _searchResults == null || _wingetButton == null) return;
        string query = _searchBox.Text.Trim();
        _searchResults.Children.Clear();
        _wingetButton.Content = query.Length == 0 ? "Search winget" : $"Search winget for \"{query}\"";

        if (query.Length == 0)
        {
            _searchResults.Children.Add(MutedLabel("Searches names, ids and groups, e.g. discord, nvidia, browsers."));
            return;
        }

        var matches = _items
            .Where(i => _where.ContainsKey(i))
            .Where(i => Has(i.Name, query) || Has(i.Program.Id, query) || Has(_where[i], query))
            .GroupBy(i => i.Program.Id).Select(g => g.First())
            .ToList();
        if (matches.Count == 0)
        {
            _searchResults.Children.Add(MutedLabel("Nothing in the list. Try \"More from winget\" on the right."));
            return;
        }
        foreach (var item in matches) _searchResults.Children.Add(ResultBox(item, _where[item]));
    }

    private async Task SearchWingetAsync()
    {
        if (_searchBox == null || _wingetResults == null || _wingetStatus == null || _wingetButton == null) return;
        string query = _searchBox.Text.Trim();
        if (query.Length == 0)
        {
            _wingetStatus.Text = "Type something in the search box first.";
            return;
        }

        // Forget the last results that nobody ticked.
        foreach (var old in _wingetItems.Where(i => !i.IsChecked).ToList())
        {
            _wingetItems.Remove(old);
            _items.Remove(old);
        }
        _wingetResults.Children.Clear();
        _wingetButton.IsEnabled = false;
        _wingetStatus.Text = $"Searching winget for \"{query}\"...";
        try
        {
            if (!await Winget.EnsureAsync(WriteLog))
            {
                _wingetStatus.Text = "winget is not available on this PC.";
                return;
            }
            var found = await Winget.SearchAsync(query, CancellationToken.None);
            _wingetStatus.Text = found.Count == 0
                ? $"No results for \"{query}\"."
                : $"{found.Count} results for \"{query}\". Tick the ones to install:";
            foreach (var (name, id) in found)
            {
                var item = _items.FirstOrDefault(i => string.Equals(i.Program.Id, id, StringComparison.OrdinalIgnoreCase));
                if (item == null)
                {
                    item = new AppItem(new Program { Name = name, Id = id });
                    _items.Add(item);
                    _wingetItems.Add(item);
                }
                _wingetResults.Children.Add(ResultBox(item, id));
            }
        }
        catch (Exception ex)
        {
            _wingetStatus.Text = "Search failed: " + ex.Message;
            Log.Write("winget search failed: " + ex);
        }
        finally
        {
            _wingetButton.IsEnabled = true;
        }
    }

    /// <summary>A result row: same checkbox as on the category tabs (shared state), with where it comes from.</summary>
    private CheckBox ResultBox(AppItem item, string note)
    {
        var text = new TextBlock();
        text.Inlines.Add(new System.Windows.Documents.Run(item.Name));
        text.Inlines.Add(new System.Windows.Documents.Run("  " + note) { Foreground = (Brush)FindResource("Sk.TextMuted") });
        var box = new CheckBox { Content = text, ToolTip = item.Program.Id, DataContext = item };
        box.SetBinding(ToggleButton.IsCheckedProperty, nameof(AppItem.IsChecked));
        box.Click += (_, _) => RefreshSelection();
        return box;
    }

    private TextBlock MutedLabel(string text) => new()
    {
        Text = text,
        Style = (Style)FindResource("Sk.Label"),
        Margin = new Thickness(20, 0, 30, 6),
        Foreground = (Brush)FindResource("Sk.TextMuted"),
        TextWrapping = TextWrapping.Wrap,
    };

    // ---------- Actions ----------

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0)
        {
            ShowNothingSelected();
            return;
        }

        await RunBusy(async token =>
        {
            foreach (var item in selected) item.State = InstallState.Waiting;
            int ok = 0, already = 0, opened = 0;
            var failed = new List<string>();
            var openedUrls = new HashSet<string>();
            var saved = new List<string>();

            for (int i = 0; i < selected.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var item = selected[i];
                item.State = InstallState.Working;
                StatusText.Text = $"Installing {item.Name} ({i + 1} of {selected.Count})...";
                WriteLog($"-> {item.Name}");

                if (item.Program.IsDirect)
                {
                    // Not in winget: downloaded from the vendor, then run (installers) or kept (ISO images).
                    string folder = item.Program.Save ? IsoFolder : InstallerCache;
                    string? file = await FetchDirectAsync(item, folder, ProgressFor(i, selected.Count), openedUrls, token);
                    if (file == null)
                    {
                        if (item.State == InstallState.Opened) opened++;
                        else failed.Add(item.Name);
                    }
                    else if (item.Program.Save)
                    {
                        item.State = InstallState.Saved;
                        WriteLog("   saved to " + file);
                        saved.Add(file);
                    }
                    else
                    {
                        StatusText.Text = $"{item.Name}: finish the setup in its own window ({i + 1} of {selected.Count})...";
                        DetailText.Text = "Waiting for " + Path.GetFileName(file) + " to close";
                        WriteLog("   running " + Path.GetFileName(file));
                        int code = await DirectDownload.RunAsync(file, item.Program.Args, token);
                        // 3010 / 1641: installed, Windows needs a restart.
                        if (code is 0 or 3010 or 1641)
                        {
                            item.State = InstallState.Done;
                            WriteLog(code == 0 ? "   OK" : "   OK, restart the PC to finish");
                            ok++;
                        }
                        else
                        {
                            item.State = InstallState.Failed;
                            WriteLog($"   the setup closed with code {code}");
                            failed.Add(item.Name);
                        }
                    }
                }
                else if (item.Program.Url is { Length: > 0 } url)
                {
                    // Vendor drivers that can't be downloaded directly: their official download page does the install.
                    if (openedUrls.Add(url)) OpenUrl(url);
                    WriteLog("   opened the official download page");
                    item.State = InstallState.Opened;
                    opened++;
                }
                else if (item.Program.NoAdmin)
                {
                    Winget.InstallAsNormalUser(item.Program.Id, item.Version);
                    WriteLog("   started in the background as a normal user");
                    item.State = InstallState.Done;
                    ok++;
                }
                else
                {
                    if (item.Version != null) WriteLog("   version " + item.Version);
                    int code = await Winget.InstallAsync(item.Program.Id, item.Version, ShowDetail,
                        ProgressFor(i, selected.Count), token);
                    if (code == 0)
                    {
                        item.State = InstallState.Done;
                        WriteLog("   OK");
                        ok++;
                    }
                    else if (Winget.IsAlreadyInstalled(code))
                    {
                        item.State = InstallState.AlreadyInstalled;
                        WriteLog("   already installed");
                        already++;
                    }
                    else
                    {
                        item.State = InstallState.Failed;
                        WriteLog($"   ERROR (code 0x{code:X8})");
                        failed.Add(item.Name);
                    }
                }
                SetProgress(i + 1, selected.Count);
            }

            string summary = $"Installed: {ok}, already there: {already}, failed: {failed.Count}";
            if (saved.Count > 0) summary += $", saved: {saved.Count}";
            if (opened > 0) summary += $", opened in browser: {opened}";
            StatusText.Text = summary;
            WriteLog(summary);
            string browserNote = opened > 0
                ? "\n\nSome downloads opened in your browser instead: download and run them from there."
                : "";
            if (saved.Count > 0)
            {
                browserNote += "\n\nSystem images were saved to " + IsoFolder +
                               ". Put one on a USB stick with Rufus, Ventoy or balenaEtcher.";
                Process.Start("explorer.exe", "\"" + IsoFolder + "\"");
            }
            if (failed.Count > 0)
            {
                MessageBox.Show(this, "Could not install:\n- " + string.Join("\n- ", failed) +
                                      "\n\nDetails are in " + Log.FilePath + browserNote,
                    "apathy", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(this, "Done!\n" + summary + browserNote, "apathy",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        });
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0)
        {
            ShowNothingSelected();
            return;
        }

        string target;
        using (var dialog = new System.Windows.Forms.FolderBrowserDialog
               {
                   Description = "Where should the installers be saved? (e.g. a USB drive)",
                   ShowNewFolderButton = true,
               })
        {
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            target = Path.Combine(dialog.SelectedPath, "Installers");
        }

        await RunBusy(async token =>
        {
            var failed = new List<string>();
            var openedUrls = new HashSet<string>();
            var fractions = new double[selected.Count];
            int finished = 0;
            using var gate = new SemaphoreSlim(ParallelDownloads);
            StatusText.Text = $"Downloading {selected.Count} programs, {ParallelDownloads} at a time...";

            // Downloads don't block each other like installers do, so several run at once.
            // Continuations come back to the UI thread, so the counters need no locking.
            var downloads = selected.Select(async (item, index) =>
            {
                if (item.Program.Url is { Length: > 0 } url && !item.Program.IsDirect)
                {
                    // Not in winget: the vendor's download page is the only official source.
                    if (openedUrls.Add(url)) OpenUrl(url);
                    WriteLog($"-> {item.Name}: opened the official download page");
                    item.State = InstallState.Opened;
                    fractions[index] = 1;
                    return;
                }

                await gate.WaitAsync(token);
                try
                {
                    item.State = InstallState.Working;
                    WriteLog($"-> downloading {item.Name}");
                    string folder = Path.Combine(target, SafeFolderName(item.Name));
                    Action<Winget.Progress> progress = p => Dispatcher.InvokeAsync(() =>
                    {
                        fractions[index] = p.Fraction;
                        SetProgress(fractions.Sum(), selected.Count);
                    });
                    if (item.Program.IsDirect)
                    {
                        // On failure FetchDirectAsync opens the vendor page (Opened) or marks the item Failed.
                        if (await FetchDirectAsync(item, folder, progress, openedUrls, token) != null)
                        {
                            item.State = InstallState.Done;
                            WriteLog($"   {item.Name}: OK");
                        }
                        else if (item.State == InstallState.Failed)
                        {
                            failed.Add(item.Name);
                        }
                    }
                    else
                    {
                        int code = await Winget.DownloadAsync(item.Program.Id, item.Version, folder, ShowDetail, progress,
                            token);
                        if (code == 0)
                        {
                            item.State = InstallState.Done;
                            WriteLog($"   {item.Name}: OK");
                        }
                        else
                        {
                            item.State = InstallState.Failed;
                            WriteLog($"   {item.Name}: ERROR (code 0x{code:X8})");
                            failed.Add(item.Name);
                        }
                    }
                }
                finally
                {
                    gate.Release();
                }
                fractions[index] = 1;
                finished++;
                StatusText.Text = $"Downloaded {finished} of {selected.Count}...";
                SetProgress(fractions.Sum(), selected.Count);
            }).ToList();
            await Task.WhenAll(downloads);

            StatusText.Text = "Installers saved to: " + target;
            WriteLog("Saved to: " + target);
            if (failed.Count > 0)
            {
                MessageBox.Show(this, "Could not download:\n- " + string.Join("\n- ", failed),
                    "apathy", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            if (Directory.Exists(target)) Process.Start("explorer.exe", target);
        });
    }

    private async void Upgrade_Click(object sender, RoutedEventArgs e)
    {
        await RunBusy(async token =>
        {
            StatusText.Text = "Updating all programs on this PC...";
            WriteLog("-> updating all programs");
            SetProgress(0, 1);
            int code = await Winget.UpgradeAllAsync(ShowDetail, ProgressFor(0, 1), token);
            SetProgress(1, 1);
            StatusText.Text = "Update finished.";
            WriteLog($"Finished (code {code}).");
        });
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _cancel?.Cancel();
        StopButton.IsEnabled = false;
        StatusText.Text = "Stopping...";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Locks the menu, makes sure winget is there, runs the work and reports errors.</summary>
    private async Task RunBusy(Func<CancellationToken, Task> work)
    {
        SetBusy(true);
        _cancel = new CancellationTokenSource();
        try
        {
            if (!await Winget.EnsureAsync(WriteLog))
            {
                MessageBox.Show(this, "Could not start winget (Windows Package Manager).\n\n" +
                                      "Update \"App Installer\" in the Microsoft Store, " +
                                      "restart the computer and try again.",
                    "apathy", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (FastDownloadsBox.IsChecked == true && !_fastDownloadsSet)
            {
                Winget.UseFastDownloads(WriteLog);
                _fastDownloadsSet = true;
            }
            SetProgress(0, 1);
            await work(_cancel.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Stopped.";
            WriteLog("Stopped.");
            foreach (var item in _items.Where(i => i.State is InstallState.Waiting or InstallState.Working))
            {
                item.State = InstallState.None;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error.";
            WriteLog("ERROR: " + ex.Message);
            Log.Write(ex.ToString());
            MessageBox.Show(this, "Something went wrong:\n" + ex.Message, "apathy",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DetailText.Text = " ";
            _cancel.Dispose();
            _cancel = null;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (var button in new[] { InstallButton, DownloadButton, UpgradeButton, RecommendedButton, ClearButton })
        {
            button.IsEnabled = !busy;
        }
        StopButton.IsEnabled = busy;
        foreach (TabItem tab in MainTabs.Items)
        {
            if (tab != InstallTab && tab.Content is UIElement page) page.IsEnabled = !busy;
        }
    }

    private void ShowNothingSelected() =>
        MessageBox.Show(this, "No programs selected.\nPick programs in the tabs on the left.",
            "apathy", MessageBoxButton.OK, MessageBoxImage.Information);

    private static string SafeFolderName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    // ---------- Direct downloads ----------

    /// <summary>Where "Install selected" keeps ISO images.</summary>
    private static string IsoFolder => Path.Combine(Native.DownloadsFolder(), "apathy");

    /// <summary>Where "Install selected" keeps downloaded installers until they run.</summary>
    private static string InstallerCache => Path.Combine(Path.GetTempPath(), "apathy");

    /// <summary>
    /// Downloads a program that isn't in winget from its vendor and returns the file. If that fails, the vendor's
    /// page is opened instead (state Opened) or the item is marked Failed, and null is returned.
    /// </summary>
    private async Task<string?> FetchDirectAsync(AppItem item, string folder, Action<Winget.Progress> progress,
        HashSet<string> openedUrls, CancellationToken token)
    {
        try
        {
            var (file, referer) = await DirectDownload.ResolveAsync(item.Program, token);
            WriteLog($"   {item.Name}: {file}");
            return await DirectDownload.SaveAsync(file, referer, folder, progress, token);
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            WriteLog($"   {item.Name}: download failed: {ex.Message}");
            Log.Write(ex.ToString());
            if (item.Program.Url is { Length: > 0 } url)
            {
                if (openedUrls.Add(url)) OpenUrl(url);
                WriteLog("   opened the official download page instead");
                item.State = InstallState.Opened;
            }
            else
            {
                item.State = InstallState.Failed;
            }
            return null;
        }
    }

    // ---------- Versions ----------

    /// <summary>The "latest ▾" next to a selected program: a menu with every version winget has.</summary>
    private async void Version_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_busy || sender is not FrameworkElement { DataContext: AppItem item } target) return;

        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        menu.Items.Add(MenuNote("loading versions..."));
        menu.IsOpen = true;

        if (!_versions.TryGetValue(item.Program.Id, out var versions))
        {
            try
            {
                versions = await Winget.VersionsAsync(item.Program.Id, CancellationToken.None);
                if (versions.Count > 0) _versions[item.Program.Id] = versions;
            }
            catch (Exception ex)
            {
                Log.Write("winget show --versions failed: " + ex);
                versions = new List<string>();
            }
        }

        menu.Items.Clear();
        AddVersion(menu, item, null, "latest");
        // winget lists the newest first; very old versions are rarely useful and would overflow the screen.
        foreach (string version in versions.Take(20)) AddVersion(menu, item, version, version);
        if (versions.Count == 0) menu.Items.Add(MenuNote("no other versions found"));
    }

    private void AddVersion(ContextMenu menu, AppItem item, string? version, string text)
    {
        var entry = new MenuItem { Header = text, IsChecked = item.Version == version };
        entry.Click += (_, _) =>
        {
            item.Version = version;
            WriteLog(version == null ? $"{item.Name}: latest version" : $"{item.Name}: version {version}");
        };
        menu.Items.Add(entry);
    }

    private MenuItem MenuNote(string text) => new()
    {
        Header = text,
        IsHitTestVisible = false,
        Foreground = (Brush)FindResource("Sk.TextMuted"),
    };

    // ---------- Output ----------

    private void SetProgress(double done, int total)
    {
        double width = Math.Max(0, ProgressTrack.ActualWidth - 2);
        ProgressFill.Width = total > 0 ? width * Math.Min(1, done / total) : 0;
    }

    /// <summary>
    /// Live download progress of program number <paramref name="index"/>: fills its share of the bar and
    /// shows size and speed under the status. Called from background threads.
    /// </summary>
    private Action<Winget.Progress> ProgressFor(int index, int total)
    {
        var clock = Stopwatch.StartNew();
        double firstBytes = -1;
        long lastDraw = -1000;
        return progress =>
        {
            if (progress.Done > 0 && firstBytes < 0) firstBytes = progress.Done;
            long now = clock.ElapsedMilliseconds;
            if (now - lastDraw < 150 && progress.Fraction < 1) return;
            lastDraw = now;

            string text = progress.Total > 0 ? $"Downloading {Size(progress.Done)} / {Size(progress.Total)}"
                : progress.Done > 0 ? $"Downloading {Size(progress.Done)}"
                : $"Working... {progress.Fraction:P0}";
            if (progress.Total > 0 && now > 1000 && progress.Done > firstBytes)
            {
                text += $"  ·  {Size((progress.Done - firstBytes) / (now / 1000.0))}/s";
            }
            Dispatcher.InvokeAsync(() =>
            {
                DetailText.Text = text;
                SetProgress(index + progress.Fraction, total);
            });
        };
    }

    private static string Size(double bytes) =>
        bytes >= 1024 * 1024 * 1024 ? $"{bytes / (1024 * 1024 * 1024):0.0} GB"
        : bytes >= 1024 * 1024 ? $"{bytes / (1024 * 1024):0.0} MB"
        : $"{bytes / 1024:0} KB";

    /// <summary>Latest winget line under the status (called from background threads).</summary>
    private void ShowDetail(string line)
    {
        Log.Write("   " + line);
        Dispatcher.InvokeAsync(() => DetailText.Text = line);
    }

    private void WriteLog(string text)
    {
        Log.Write(text);
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => WriteLog(text));
            return;
        }
        LogText.Text += $"[{DateTime.Now:HH:mm:ss}] {text}\n";
        FindChild<ScrollViewer>(LogGroup)?.ScrollToEnd();
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>
    /// Makes the window big enough that every category tab shows all its programs, with no scrolling and no
    /// cut-off names, as far as the screen allows. It only ever grows the window.
    /// </summary>
    private void FitToContent()
    {
        const double gap = 18;
        // Room for rounding, so a scrollbar or a cut-off letter never comes back.
        const double slack = 12;
        var unlimited = new Size(double.PositiveInfinity, double.PositiveInfinity);
        double pageWidth = 0, pageHeight = 0;

        // Each page is measured while it is shown, so templates and inherited fonts are exactly as on screen.
        // Nothing is drawn in between: the original tab and focus are back before this method returns.
        var shown = MainTabs.SelectedItem;
        var focused = Keyboard.FocusedElement;
        foreach (var tab in MainTabs.Items.OfType<TabItem>())
        {
            if (tab.Content is not Grid page || !_categoryPages.Contains(page)) continue;
            MainTabs.SelectedItem = tab;
            UpdateLayout();

            double columnWidth = 0;
            foreach (var column in page.Children.OfType<Grid>())
            {
                double columnHeight = 0;
                foreach (var group in column.Children.OfType<FrameworkElement>())
                {
                    group.Measure(unlimited);
                    double need = group.DesiredSize.Height + slack;
                    // Spare height is shared in proportion to what each group needs.
                    column.RowDefinitions[Grid.GetRow(group)].Height = new GridLength(need, GridUnitType.Star);
                    columnHeight += need;
                    columnWidth = Math.Max(columnWidth, group.DesiredSize.Width + slack);
                }
                columnHeight += gap * Math.Max(0, column.Children.Count - 1);
                pageHeight = Math.Max(pageHeight, columnHeight);
            }
            // Both columns get the same width, so the wider one decides.
            pageWidth = Math.Max(pageWidth, 2 * columnWidth + gap);
        }
        MainTabs.SelectedItem = shown;
        UpdateLayout();
        focused?.Focus();

        // Frame, tab column and page margins around the page area.
        if (MainTabs.Template.FindName("PART_SelectedContentHost", MainTabs) is not FrameworkElement host
            || host.ActualWidth <= 0) return;
        double chromeWidth = Root.ActualWidth - host.ActualWidth;
        double chromeHeight = Root.ActualHeight - host.ActualHeight;

        // The icon column on the left must show every tab down to Install: a 16px strip on top, then the icons.
        double tabsHeight = 16 + MainTabs.Items.OfType<TabItem>().Sum(t => t.DesiredSize.Height);
        double tabsChrome = Root.ActualHeight - MainTabs.ActualHeight;

        double scale = (Root.LayoutTransform as ScaleTransform)?.ScaleX ?? 1;
        var screen = SystemParameters.WorkArea;
        double needHeight = Math.Max(pageHeight + chromeHeight, tabsHeight + tabsChrome);
        double width = Math.Min(Math.Max(Root.Width, pageWidth + chromeWidth), screen.Width / scale - 20);
        double height = Math.Min(Math.Max(Root.Height, needHeight), screen.Height / scale - 20);
        if (width <= Root.Width && height <= Root.Height && _fitted) return;

        double oldWidth = ActualWidth, oldHeight = ActualHeight;
        Root.Width = Math.Max(Root.Width, width);
        Root.Height = Math.Max(Root.Height, height);
        UpdateLayout();

        // First time: centre on the screen. Later: grow around the current centre, staying on screen.
        double left = _fitted ? Left - (ActualWidth - oldWidth) / 2 : screen.Left + (screen.Width - ActualWidth) / 2;
        double top = _fitted ? Top - (ActualHeight - oldHeight) / 2 : screen.Top + (screen.Height - ActualHeight) / 2;
        Left = Math.Max(screen.Left, Math.Min(left, screen.Right - ActualWidth));
        Top = Math.Max(screen.Top, Math.Min(top, screen.Bottom - ActualHeight));
        _fitted = true;
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Root.Width = Math.Max(MinMenuWidth, Root.Width + e.HorizontalChange);
        Root.Height = Math.Max(MinMenuHeight, Root.Height + e.VerticalChange);
    }
}
