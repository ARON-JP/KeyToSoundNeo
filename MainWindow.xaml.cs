using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Windows.Interop;

namespace KeyToSoundNeo;

public partial class MainWindow : Window
{
    private readonly ProfileStore _store = new();
    private readonly AudioEngine _audio = new();
    private readonly HashSet<Key> _pressedKeys = [];
    private BackgroundKeyListener? _backgroundKeyListener;
    private AppPreferences _preferences = new();
    private SoundProfile _profile = new();
    private string? _selectedKey;
    private string _page = "keyboard";
    private bool _soundsEnabled = true;
    private bool _capturingKey;
    private bool _suppressNextKeyUp;
    private bool _backgroundModeEnabled;
    private bool _updatingStartupCheckbox;
    private bool _updatingBackgroundCheckbox;
    private readonly DispatcherTimer _settingsSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += Window_SourceInitialized;
        _audio.PlaybackFailed += (path, exception) =>
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (IsVisible)
                    ShowError($"音源を再生できません: {Path.GetFileName(path)}", exception);
            });
        };
        _settingsSaveTimer.Tick += (_, _) =>
        {
            _settingsSaveTimer.Stop();
            SaveCurrent();
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var profiles = _store.GetProfileNames();
            if (profiles.Length > 0)
                _profile = _store.Load(profiles[0]) ?? new SoundProfile();
            else
            {
                _profile = new SoundProfile { Name = "Neo Base" };
                _store.Save(_profile);
            }
            _preferences = _store.LoadPreferences();
            if (_preferences.ProcessPriority is not ("BelowNormal" or "Normal" or "AboveNormal"))
                _preferences.ProcessPriority = "Normal";
            SetPriority(_preferences.ProcessPriority, persist: false);
            if (_preferences.BackgroundKeySoundsEnabled)
            {
                try
                {
                    (_backgroundKeyListener ?? throw new InvalidOperationException("The window input listener is not ready.")).Start();
                    _backgroundModeEnabled = true;
                }
                catch (Exception ex)
                {
                    _preferences.BackgroundKeySoundsEnabled = false;
                    _store.SavePreferences(_preferences);
                    ShowError("バックグラウンド再生を開始できませんでした", ex);
                }
            }
            _audio.Warm(AllSoundPaths());
            RefreshBackgroundKeys();
            RenderPage();
            UpdateSidebar();
            WindowSurface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)));
        }
        catch (Exception ex)
        {
            ShowError("起動に失敗しました", ex);
        }
    }

    private void RenderPage()
    {
        var oldPage = MainContent.Children.Count > 0 ? MainContent.Children[0] : null;
        MainContent.Children.Clear();
        SettingsContent.Children.Clear();
        UIElement page = _page switch
        {
            "profiles" => BuildProfilesPage(),
            "advanced" => BuildAboutPage(),
            _ => BuildKeyboardPage()
        };
        page.Opacity = 0;
        MainContent.Children.Add(page);
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        if (_page == "keyboard")
            BuildKeySettings();
        else if (_page == "profiles")
            BuildProfileSettings();
        else if (_page == "advanced")
            BuildAdvancedSettings();
        else
            BuildAboutSettings();
    }

    private UIElement BuildKeyboardPage()
    {
        var panel = new StackPanel();
        panel.Children.Add(Heading("キーボード", "キーごとに音を割り当て。押す音と離す音を組み合わせて、自分らしい打鍵感に。"));

        var card = Card();
        var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 20) };
        var subtitle = new StackPanel();
        subtitle.Children.Add(Text("レイアウト", 15, "#24303C", FontWeights.SemiBold));
        subtitle.Children.Add(Text("キーを選ぶと、右側で音源と配置ゾーンを設定できます。", 11, "#7C8792"));
        DockPanel.SetDock(subtitle, Dock.Left);
        header.Children.Add(subtitle);
        var layoutOptions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var layoutPicker = new ComboBox
        {
            Width = 128,
            Height = 34,
            Padding = new Thickness(7, 4, 7, 4),
            Margin = new Thickness(0, 0, 10, 0)
        };
        layoutPicker.Items.Add(new ComboBoxItem { Content = "JIS · 日本語", Tag = "JIS" });
        layoutPicker.Items.Add(new ComboBoxItem { Content = "ANSI · US", Tag = "ANSI" });
        layoutPicker.SelectedItem = layoutPicker.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(item => (string)item.Tag == _profile.KeyboardLayout);
        layoutPicker.SelectionChanged += (_, _) =>
        {
            if (layoutPicker.SelectedItem is not ComboBoxItem { Tag: string value } || value == _profile.KeyboardLayout)
                return;
            _profile.KeyboardLayout = value;
            SaveCurrent();
            RenderPage();
        };
        layoutOptions.Children.Add(layoutPicker);
        var layoutRows = _profile.KeyboardLayout == "ANSI" ? KeyNames.AnsiRows : KeyNames.JisRows;
        var count = Text($"{layoutRows.Sum(row => row.Length)} キー", 11, "#778391");
        layoutOptions.Children.Add(count);
        DockPanel.SetDock(layoutOptions, Dock.Right);
        header.Children.Add(layoutOptions);
        card.Children.Add(header);
        foreach (var row in BuildKeyboardRows())
        {
            row.Margin = new Thickness(0, 0, 0, 7);
            card.Children.Add(row);
        }
        var keyboardView = new Viewbox
        {
            Child = card,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        panel.Children.Add(keyboardView);

        var bottom = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition());
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var legend = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        legend.Children.Add(Legend("#EAF0FF", "中央"));
        legend.Children.Add(Legend("#EAF6F0", "端"));
        legend.Children.Add(Legend("#FFF4E8", "特殊キー"));
        bottom.Children.Add(legend);
        var layoutNote = Text(
            _backgroundModeEnabled ? "バックグラウンド入力を有効にしています" : "キー入力はアプリが前面のときのみ",
            10, "#8A95A0");
        Grid.SetColumn(layoutNote, 1);
        bottom.Children.Add(layoutNote);
        panel.Children.Add(bottom);

        var activity = Card();
        activity.Margin = new Thickness(0, 16, 0, 0);
        activity.Padding = new Thickness(17, 14, 17, 14);
        activity.Children.Add(Text("入力モニター", 11, "#7C8792", FontWeights.SemiBold));
        activity.Children.Add(Text("キーを押すと、割り当てた音がここで再生されます。", 12, "#566371")
            .WithName("ActivityText"));
        panel.Children.Add(activity);
        return panel;
    }

    private IEnumerable<StackPanel> BuildKeyboardRows()
    {
        var rows = _profile.KeyboardLayout == "ANSI" ? KeyNames.AnsiRows : KeyNames.JisRows;
        foreach (var keys in rows)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var item in keys)
            {
                var key = item.Key;
                var button = new Button
                {
                    Tag = key,
                    Content = item.Label,
                    Width = KeyWidth(key),
                    Height = 42,
                    Padding = new Thickness(2),
                    Margin = new Thickness(3, 0, 3, 0),
                    FontSize = key.Length > 4 ? 9 : 11,
                    FontWeight = FontWeights.Medium,
                    Focusable = false,
                    Background = ZoneBrush(GetZone(key)),
                    BorderBrush = key == _selectedKey ? Brush("#5576E8") : Brush("#E3E9F0"),
                    BorderThickness = key == _selectedKey ? new Thickness(2) : new Thickness(1),
                    Foreground = Brush("#394653"),
                    ToolTip = $"{KeyNames.Display(key)} · {ZoneLabel(GetZone(key))}"
                };
                button.Click += KeyboardKey_Click;
                row.Children.Add(button);
            }
            yield return row;
        }
    }

    private UIElement BuildProfilesPage()
    {
        var panel = new StackPanel();
        panel.Children.Add(Heading("プロファイル", "キー配置と音源の組み合わせを保存。用途ごとに切り替えて使えます。"));
        var card = Card();
        card.Children.Add(Text("保存済みプロファイル", 14, "#263340", FontWeights.SemiBold));
        card.Children.Add(Text("選ぶと、そのプロファイルのキー設定が読み込まれます。", 11, "#7D8994")
            .WithMargin(new Thickness(0, 4, 0, 16)));
        var list = new StackPanel();
        var names = _store.GetProfileNames();
        if (names.Length == 0)
            list.Children.Add(Text("保存済みプロファイルはありません。", 12, "#88939D"));
        foreach (var name in names)
        {
            var row = new Border
            {
                Background = name == _profile.Name ? Brush("#F0F3FF") : Brush("#FAFBFC"),
                BorderBrush = Brush(name == _profile.Name ? "#DDE4FF" : "#EBEFF3"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand,
                Tag = name
            };
            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition());
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.Children.Add(Text(name, 13, "#2B3744", FontWeights.SemiBold));
            var count = Text($"{_store.Load(name)?.Keys.Count(k => k.PressSounds.Count > 0 || k.ReleaseSounds.Count > 0) ?? 0} キー", 10, "#85909B");
            Grid.SetColumn(count, 1);
            rowGrid.Children.Add(count);
            row.Child = rowGrid;
            row.MouseLeftButtonUp += ProfileRow_Click;
            list.Children.Add(row);
        }
        card.Children.Add(list);
        panel.Children.Add(card);
        return panel;
    }

    private UIElement BuildAboutPage()
    {
        var panel = new StackPanel();
        panel.Children.Add(Heading("詳細設定", "起動時の動作やアプリ情報を確認できます。"));
        var card = Card();
        card.Children.Add(Text("KeyToSound Neo", 20, "#263340", FontWeights.SemiBold));
        card.Children.Add(Text("キーボード入力に、選んだ音を重ねるデスクトップアプリです。"
            + "\n音源、レイアウト、プロファイルはこのPC上に保存されます。", 13, "#65717D")
            .WithMargin(new Thickness(0, 10, 0, 0)));
        var divider = new Border { Height = 1, Background = Brush("#E9EDF2"), Margin = new Thickness(0, 20, 0, 16) };
        card.Children.Add(divider);
        card.Children.Add(Text("プライバシー", 12, "#273441", FontWeights.SemiBold));
        card.Children.Add(Text("キー入力はアプリが前面にある間だけ処理します。入力内容の記録や、バックグラウンドでの監視は行いません。"
            + "\n入力欄を編集中は、キー音を再生しません。", 12, "#65717D")
            .WithMargin(new Thickness(0, 6, 0, 0)));
        panel.Children.Add(card);
        return panel;
    }

    private void BuildKeySettings()
    {
        SettingsContent.Children.Add(Text("キー設定", 15, "#263340", FontWeights.SemiBold));
        SettingsContent.Children.Add(Text(_selectedKey is null ? "キーを選択してください" : $"{KeyNames.Display(_selectedKey)} の設定",
            12, "#7B8793").WithMargin(new Thickness(0, 4, 0, 18)));
        if (_selectedKey is null)
        {
            SettingsContent.Children.Add(EmptyState("キーボードからキーを選ぶと、音源を設定できます。"));
            return;
        }
        var keySound = GetKey(_selectedKey);
        SettingsContent.Children.Add(SectionLabel("配置ゾーン"));
        var zones = new ComboBox { Margin = new Thickness(0, 4, 0, 17), Height = 36, Padding = new Thickness(8, 4, 8, 4) };
        zones.Items.Add(new ComboBoxItem { Content = "中央", Tag = "Center" });
        zones.Items.Add(new ComboBoxItem { Content = "端", Tag = "Edge" });
        zones.Items.Add(new ComboBoxItem { Content = "特殊キー", Tag = "Special" });
        zones.SelectedItem = zones.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == keySound.Zone);
        zones.SelectionChanged += (_, _) =>
        {
            if (zones.SelectedItem is not ComboBoxItem { Tag: string value }) return;
            keySound.Zone = value;
            SaveCurrent();
            RenderPage();
        };
        SettingsContent.Children.Add(zones);

        var capture = new Button
        {
            Content = "⌨  実際のキーを押して登録",
            Margin = new Thickness(0, 0, 0, 16),
            Background = Brush(_capturingKey ? "#FFF4E8" : "#FFFFFF"),
            BorderBrush = Brush(_capturingKey ? "#F0CFA7" : "#E5EAF0")
        };
        capture.Click += (_, _) =>
        {
            _capturingKey = true;
            capture.Content = "登録するキーを押してください（Esc で取消）";
            capture.Background = Brush("#FFF4E8");
            Keyboard.ClearFocus();
            Focus();
        };
        SettingsContent.Children.Add(capture);

        AddSoundSection("押したとき", keySound.PressSounds);
        AddSoundSection("離したとき", keySound.ReleaseSounds);

        var note = Text("複数の音源を追加すると、キーを押すたびにランダムで再生します。"
            + "\n対応形式: WAV / MP3 / WMA / AAC / M4A など（Windows対応形式）", 10, "#7C8792")
            .WithMargin(new Thickness(0, 9, 0, 0));
        SettingsContent.Children.Add(note);
    }

    private void AddSoundSection(string title, List<string> paths)
    {
        SettingsContent.Children.Add(SectionLabel(title).WithMargin(new Thickness(0, 2, 0, 8)));
        if (paths.Count == 0)
        {
            SettingsContent.Children.Add(Text("音源なし", 11, "#9AA4AE").WithMargin(new Thickness(0, 0, 0, 8)));
        }
        else
        {
            foreach (var path in paths.ToArray())
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var name = Text(Path.GetFileName(path), 10, "#485563");
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(name);
                var remove = new Button { Content = "×", Padding = new Thickness(8, 3, 8, 3), FontSize = 12 };
                remove.Click += (_, _) =>
                {
                    paths.Remove(path);
                    SaveCurrent();
                    RenderPage();
                };
                Grid.SetColumn(remove, 1);
                row.Children.Add(remove);
                SettingsContent.Children.Add(row);
            }
        }
        var add = new Button
        {
            Content = "＋  音源を追加",
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(8, 8, 8, 8)
        };
        add.Click += (_, _) => AddSound(paths);
        SettingsContent.Children.Add(add);
    }

    private void BuildProfileSettings()
    {
        SettingsContent.Children.Add(Text("プロファイル", 15, "#263340", FontWeights.SemiBold));
        SettingsContent.Children.Add(Text("音の反応を調整", 11, "#7B8793").WithMargin(new Thickness(0, 4, 0, 17)));
        var nameBox = new TextBox
        {
            Text = _profile.Name,
            Height = 36,
            Padding = new Thickness(10, 7, 10, 6),
            BorderBrush = Brush("#E3E8EE"),
            BorderThickness = new Thickness(1),
            Background = Brush("#FBFCFD")
        };
        nameBox.TextChanged += (_, _) =>
        {
            _profile.Name = nameBox.Text;
            UpdateSidebar();
            ScheduleSave();
        };
        SettingsContent.Children.Add(SectionLabel("プロファイル名"));
        SettingsContent.Children.Add(nameBox.WithMargin(new Thickness(0, 5, 0, 17)));

        AddSlider("全体音量", _profile.Volume, 0, 1, value => _profile.Volume = value, x => $"{x:P0}");
        AddSlider("連打時の音量低下", _profile.SpeedVolume, 0, 1, value => _profile.SpeedVolume = value, x => $"{x:P0}");
        AddSlider("音程のゆらぎ", _profile.PitchVariation, 0, 0.06, value => _profile.PitchVariation = value, x => $"{x * 100:F1}%");
        SettingsContent.Children.Add(SectionLabel("音のプリセット").WithMargin(new Thickness(0, 5, 0, 7)));
        var presets = new StackPanel();
        presets.Children.Add(PresetButton("バランス", 0.82, 0.3, 0.012));
        presets.Children.Add(PresetButton("やわらか", 0.58, 0.18, 0.006));
        presets.Children.Add(PresetButton("しっかり", 0.92, 0.48, 0.02));
        SettingsContent.Children.Add(presets);
        var save = new Button { Content = "変更を保存", Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 16, 0, 8) };
        save.Click += (_, _) => SaveCurrent();
        SettingsContent.Children.Add(save);
        var create = new Button { Content = "＋  新しいプロファイル", Margin = new Thickness(0, 0, 0, 8) };
        create.Click += (_, _) => CreateProfile();
        SettingsContent.Children.Add(create);
        var export = new Button { Content = "共有ファイルを書き出す", Margin = new Thickness(0, 0, 0, 8) };
        export.Click += (_, _) => ExportProfile();
        SettingsContent.Children.Add(export);
        var import = new Button { Content = "共有ファイルを読み込む" };
        import.Click += (_, _) => ImportProfile();
        SettingsContent.Children.Add(import);
    }

    private void BuildAboutSettings()
    {
        SettingsContent.Children.Add(Text("アプリ情報", 15, "#263340", FontWeights.SemiBold));
        SettingsContent.Children.Add(Text("KeyToSound Neo", 12, "#7B8793").WithMargin(new Thickness(0, 5, 0, 14)));
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyToSoundNeo");
        var box = new Border { Background = Brush("#F5F7FA"), CornerRadius = new CornerRadius(9), Padding = new Thickness(11) };
        box.Child = Text(path, 10, "#55616D");
        SettingsContent.Children.Add(box);
        SettingsContent.Children.Add(Text("このPCで動作するキーボードサウンドアプリです。"
            + "\nキー入力は前面にあるときだけ処理し、記録・送信しません。", 11, "#7B8793")
            .WithMargin(new Thickness(0, 12, 0, 0)));
    }

    private void BuildAdvancedSettings()
    {
        SettingsContent.Children.Add(Text("詳細設定", 15, "#263340", FontWeights.SemiBold));
        SettingsContent.Children.Add(Text("起動と動作", 11, "#7B8793").WithMargin(new Thickness(0, 4, 0, 18)));

        var startup = new CheckBox
        {
            Content = "Windowsへのサインイン時に起動",
            IsChecked = StartupManager.IsEnabled(),
            FontSize = 12,
            Foreground = Brush("#394653"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        startup.Checked += (_, _) => SetStartup(startup, true);
        startup.Unchecked += (_, _) => SetStartup(startup, false);
        SettingsContent.Children.Add(startup);
        SettingsContent.Children.Add(Text("現在のユーザーのスタートアップに登録します。", 10, "#87919B")
            .WithMargin(new Thickness(0, 0, 0, 19)));

        SettingsContent.Children.Add(SectionLabel("プロセス優先度").WithMargin(new Thickness(0, 0, 0, 6)));
        var priority = new ComboBox { Height = 36, Padding = new Thickness(8, 4, 8, 4) };
        priority.Items.Add(new ComboBoxItem { Content = "低", Tag = "BelowNormal" });
        priority.Items.Add(new ComboBoxItem { Content = "通常", Tag = "Normal" });
        priority.Items.Add(new ComboBoxItem { Content = "高", Tag = "AboveNormal" });
        priority.SelectedItem = priority.Items.Cast<ComboBoxItem>()
            .            FirstOrDefault(item => (string)item.Tag == _preferences.ProcessPriority);
        priority.SelectionChanged += (_, _) =>
        {
            if (priority.SelectedItem is ComboBoxItem { Tag: string value })
                SetPriority(value);
        };
        SettingsContent.Children.Add(priority);
        SettingsContent.Children.Add(Text("「高」は他のアプリの応答性に影響することがあります。リアルタイム優先度は使用しません。", 10, "#87919B")
            .WithMargin(new Thickness(0, 7, 0, 17)));

        var toggleSound = new CheckBox
        {
            Content = "キーサウンドを有効にする",
            IsChecked = _soundsEnabled,
            FontSize = 12,
            Foreground = Brush("#394653")
        };
        toggleSound.Checked += (_, _) => SetSoundsEnabled(true);
        toggleSound.Unchecked += (_, _) => SetSoundsEnabled(false);
        SettingsContent.Children.Add(toggleSound);
        SettingsContent.Children.Add(Text("アプリが前面にあるときだけキー入力に反応します。", 10, "#87919B")
            .WithMargin(new Thickness(0, 5, 0, 0)));

        var background = new CheckBox
        {
            Content = "バックグラウンドでもキー音を再生",
            IsChecked = _backgroundModeEnabled,
            FontSize = 12,
            Foreground = Brush("#394653"),
            Margin = new Thickness(0, 17, 0, 0)
        };
        background.Checked += (_, _) => SetBackgroundMode(background, true);
        background.Unchecked += (_, _) => SetBackgroundMode(background, false);
        SettingsContent.Children.Add(background);
        SettingsContent.Children.Add(Text(
            "有効にすると、他のアプリを操作中もキー音を再生します。割り当て済みのキーだけに反応し、入力を遮断・保存しません。",
            10, "#87919B").WithMargin(new Thickness(0, 5, 0, 0)));
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)
            ?? throw new InvalidOperationException("Could not initialize the keyboard input window.");
        _backgroundKeyListener = new BackgroundKeyListener(source);
        _backgroundKeyListener.KeyEdge += BackgroundKeyListener_KeyEdge;
        _backgroundKeyListener.InputFailed += exception =>
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(() => ShowError("バックグラウンド入力を処理できませんでした", exception));
        };
    }

    private void BackgroundKeyListener_KeyEdge(Key key, bool isDown)
    {
        if (!_backgroundModeEnabled)
            return;
        if (_capturingKey)
            return;
        if (isDown && IsActive && (_page != "keyboard" || IsTextEntryFocused()))
            return;
        HandleKeyEdge(key, isDown);
    }

    private void SetBackgroundMode(CheckBox control, bool enabled)
    {
        if (_updatingBackgroundCheckbox)
            return;

        var wasEnabled = _backgroundModeEnabled;
        try
        {
            if (enabled)
            {
                (_backgroundKeyListener ?? throw new InvalidOperationException("The window input listener is not ready.")).Start();
            }
            else
            {
                _backgroundKeyListener?.Stop();
                _pressedKeys.Clear();
            }

            var updatedPreferences = _store.LoadPreferences();
            updatedPreferences.BackgroundKeySoundsEnabled = enabled;
            _store.SavePreferences(updatedPreferences);
            _preferences = updatedPreferences;
            _backgroundModeEnabled = enabled;
        }
        catch (Exception ex)
        {
            try
            {
                if (wasEnabled)
                    _backgroundKeyListener?.Start();
                else
                    _backgroundKeyListener?.Stop();
            }
            catch (Exception rollbackException)
            {
                ex = new AggregateException(ex, rollbackException);
            }

            _backgroundModeEnabled = wasEnabled;
            _updatingBackgroundCheckbox = true;
            control.IsChecked = wasEnabled;
            _updatingBackgroundCheckbox = false;
            ShowError("バックグラウンド再生の設定を変更できませんでした", ex);
        }
    }

    private void SetStartup(CheckBox control, bool enabled)
    {
        if (_updatingStartupCheckbox)
            return;
        try
        {
            StartupManager.SetEnabled(enabled);
        }
        catch (Exception ex)
        {
            _updatingStartupCheckbox = true;
            control.IsChecked = !enabled;
            _updatingStartupCheckbox = false;
            ShowError("自動起動の設定を変更できませんでした", ex);
        }
    }

    private void SetPriority(string value, bool persist = true)
    {
        if (value is not ("BelowNormal" or "Normal" or "AboveNormal")
            || !Enum.TryParse<ProcessPriorityClass>(value, out var priority))
            return;
        try
        {
            using var process = Process.GetCurrentProcess();
            var previousPriority = process.PriorityClass;
            process.PriorityClass = priority;
            try
            {
                if (persist)
                {
                    var updatedPreferences = new AppPreferences
                    {
                        ProcessPriority = value,
                        BackgroundKeySoundsEnabled = _preferences.BackgroundKeySoundsEnabled
                    };
                    _store.SavePreferences(updatedPreferences);
                    _preferences = updatedPreferences;
                }
            }
            catch
            {
                process.PriorityClass = previousPriority;
                throw;
            }
            if (!persist)
                _preferences.ProcessPriority = value;
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or IOException
            or UnauthorizedAccessException)
        {
            ShowError("プロセス優先度を変更できませんでした", ex);
            RenderPage();
        }
    }

    private void AddSlider(string title, double value, double minimum, double maximum, Action<double> update, Func<double, string> format)
    {
        SettingsContent.Children.Add(SectionLabel(title).WithMargin(new Thickness(0, 4, 0, 5)));
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value, VerticalAlignment = VerticalAlignment.Center };
        var valueText = Text(format(value), 10, "#73808C");
        Grid.SetColumn(valueText, 1);
        row.Children.Add(slider);
        row.Children.Add(valueText);
        slider.ValueChanged += (_, args) =>
        {
            update(args.NewValue);
            valueText.Text = format(args.NewValue);
            ScheduleSave();
        };
        SettingsContent.Children.Add(row.WithMargin(new Thickness(0, 0, 0, 13)));
    }

    private Button PresetButton(string title, double volume, double speed, double pitch)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(10, 7, 10, 7) };
        button.Click += (_, _) =>
        {
            _profile.Volume = volume;
            _profile.SpeedVolume = speed;
            _profile.PitchVariation = pitch;
            SaveCurrent();
            RenderPage();
        };
        return button;
    }

    private async void AddSound(List<string> paths)
    {
        var dialog = new OpenFileDialog
        {
            Title = "音源を選択",
            Multiselect = true,
            Filter = "オーディオファイル|*.wav;*.mp3;*.wma;*.aac;*.m4a;*.aiff;*.aif;*.mp4|すべてのファイル|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        var failures = new List<string>();
        var addedAny = false;
        foreach (var sourcePath in dialog.FileNames)
        {
            string? storedPath = null;
            try
            {
                storedPath = _store.ImportSound(sourcePath);
                await _audio.PreloadAsync(storedPath);
                paths.Add(storedPath);
                addedAny = true;
            }
            catch (Exception ex)
            {
                if (storedPath is not null && File.Exists(storedPath))
                    File.Delete(storedPath);
                failures.Add($"{Path.GetFileName(sourcePath)}: {ex.Message}");
            }
        }
        if (addedAny)
        {
            SaveCurrent();
            _audio.Warm(paths);
            RenderPage();
        }
        if (failures.Count > 0)
        {
            MessageBox.Show(this, "読み込めなかった音源:\n\n" + string.Join("\n", failures),
                "音源を追加できませんでした", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CreateProfile()
    {
        SaveCurrent();
        _profile = new SoundProfile
        {
            Name = NextProfileName(),
            KeyboardLayout = _profile.KeyboardLayout,
            Volume = _profile.Volume,
            PitchVariation = _profile.PitchVariation,
            SpeedVolume = _profile.SpeedVolume,
            Keys = _profile.Keys.Select(k => new KeySound
            {
                Key = k.Key, Zone = k.Zone,
                PressSounds = [.. k.PressSounds],
                ReleaseSounds = [.. k.ReleaseSounds]
            }).ToList()
        };
        SaveCurrent();
        UpdateSidebar();
        RenderPage();
    }

    private void ImportProfile()
    {
        var dialog = new OpenFileDialog { Title = "共有プロファイルを開く", Filter = "KeyToSound共有ファイル|*.ksn" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _profile = _store.ImportKsn(dialog.FileName);
            SaveCurrent();
            _audio.Warm(AllSoundPaths());
            RefreshBackgroundKeys();
            _selectedKey = null;
            UpdateSidebar();
            RenderPage();
        }
        catch (Exception ex)
        {
            ShowError("共有ファイルを読み込めませんでした", ex);
        }
    }

    private void ExportProfile()
    {
        try
        {
            SaveCurrent();
            var dialog = new SaveFileDialog
            {
                Title = "共有ファイルを書き出す",
                FileName = $"{SafeFileName(_profile.Name)}.ksn",
                DefaultExt = ".ksn",
                Filter = "KeyToSound共有ファイル|*.ksn"
            };
            if (dialog.ShowDialog(this) != true) return;
            _store.ExportKsn(_profile, dialog.FileName);
            MessageBox.Show(this, "共有ファイルを書き出しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError("共有ファイルを書き出せませんでした", ex);
        }
    }

    private void ProfileRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string name }) return;
        try
        {
            SaveCurrent();
            _profile = _store.Load(name) ?? throw new FileNotFoundException($"Profile '{name}' no longer exists.");
            _selectedKey = null;
            _audio.Warm(AllSoundPaths());
            RefreshBackgroundKeys();
            UpdateSidebar();
            RenderPage();
        }
        catch (Exception ex)
        {
            ShowError("プロファイルを読み込めませんでした", ex);
        }
    }

    private void KeyboardKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;
        _selectedKey = key;
        var keySound = GetKey(key);
        if (string.IsNullOrEmpty(keySound.Zone))
            keySound.Zone = KeyNames.DefaultZone(key, _profile.KeyboardLayout);
        RenderPage();
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string page }) return;
        _page = page;
        if (page != "keyboard")
            _capturingKey = false;
        KeyboardNav.Foreground = Brush(page == "keyboard" ? "#FFFFFF" : "#BAC4D0");
        ProfilesNav.Foreground = Brush(page == "profiles" ? "#FFFFFF" : "#BAC4D0");
        AdvancedNav.Foreground = Brush(page == "advanced" ? "#FFFFFF" : "#BAC4D0");
        KeyboardNav.Background = Brush(page == "keyboard" ? "#283749" : "Transparent");
        ProfilesNav.Background = Brush(page == "profiles" ? "#283749" : "Transparent");
        AdvancedNav.Background = Brush(page == "advanced" ? "#283749" : "Transparent");
        RenderPage();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingKey)
        {
            e.Handled = true;
            if (e.Key == Key.Escape)
            {
                _capturingKey = false;
                RenderPage();
                return;
            }
            var actualKey = NormalizeKey(e.Key);
            if (actualKey.Length == 0) return;
            if (_selectedKey is not null)
            {
                var existing = _profile.Keys.FirstOrDefault(item => KeyNames.CanonicalKey(item.Key) == actualKey);
                var selected = _profile.Keys.FirstOrDefault(item => KeyNames.CanonicalKey(item.Key) == _selectedKey);
                if (selected is not null && !string.Equals(actualKey, _selectedKey, StringComparison.Ordinal))
                {
                    if (existing is not null)
                    {
                        selected.PressSounds.AddRange(existing.PressSounds);
                        selected.ReleaseSounds.AddRange(existing.ReleaseSounds);
                        _profile.Keys.Remove(existing);
                    }
                    selected.Key = actualKey;
                }
                _selectedKey = actualKey;
                _capturingKey = false;
                _suppressNextKeyUp = true;
                SaveCurrent();
                RenderPage();
            }
            return;
        }
        if (_backgroundModeEnabled)
            return;
        if (!_soundsEnabled || !IsActive || _page != "keyboard" || IsTextEntryFocused())
            return;
        if (!e.IsRepeat)
            HandleKeyEdge(e.Key, true);
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (_suppressNextKeyUp)
        {
            _suppressNextKeyUp = false;
            e.Handled = true;
            return;
        }
        if (_backgroundModeEnabled)
            return;
        if (!_pressedKeys.Remove(e.Key))
            return;
        if (!_soundsEnabled || !IsActive || _page != "keyboard" || IsTextEntryFocused())
            return;
        HandleKeyUp(e.Key);
    }

    private void HandleKeyEdge(Key key, bool isDown)
    {
        var keyName = NormalizeKey(key);
        if (string.IsNullOrEmpty(keyName) || keyName == "System")
            return;

        if (isDown)
        {
            if (!_soundsEnabled || _page != "keyboard")
                return;
            var sound = _profile.Keys.FirstOrDefault(item => KeyNames.CanonicalKey(item.Key) == keyName);
            if (sound is null || (sound.PressSounds.Count == 0 && sound.ReleaseSounds.Count == 0))
                return;
            if (!_pressedKeys.Add(key))
                return;
            _audio.Play(sound.PressSounds, _profile, $"{sound.Key}:press");
            if (IsActive)
                SetActivity($"{KeyNames.Display(keyName)} を押しました  ·  {DateTime.Now:HH:mm:ss}");
        }
        else
        {
            if (!_pressedKeys.Remove(key))
                return;
            if (_soundsEnabled && _page == "keyboard")
                HandleKeyUp(key);
        }
    }

    private void HandleKeyUp(Key key)
    {
        var keyName = NormalizeKey(key);
        var sound = _profile.Keys.FirstOrDefault(item => KeyNames.CanonicalKey(item.Key) == keyName);
        if (sound is not null)
            _audio.Play(sound.ReleaseSounds, _profile, $"{sound.Key}:release");
    }

    private void SoundToggle_Click(object sender, RoutedEventArgs e)
    {
        SetSoundsEnabled(!_soundsEnabled);
    }

    private void SetSoundsEnabled(bool enabled)
    {
        _soundsEnabled = enabled;
        if (!enabled)
            _pressedKeys.Clear();
        SoundStatusText.Text = _soundsEnabled ? "サウンド ON" : "サウンド OFF";
        SoundStatusDot.Fill = Brush(_soundsEnabled ? "#42AD79" : "#A1AAB3");
        SoundToggleButton.Background = Brush(_soundsEnabled ? "#ECF7F1" : "#F1F3F5");
        SoundToggleButton.BorderBrush = Brush(_soundsEnabled ? "#D9EFE3" : "#E4E8EC");
        SoundToggleButton.Foreground = Brush(_soundsEnabled ? "#248558" : "#66727D");
    }

    private void SaveCurrent()
    {
        try
        {
            _store.Save(_profile);
            RefreshBackgroundKeys();
            UpdateSidebar();
        }
        catch (Exception ex)
        {
            ShowError("プロファイルを保存できませんでした", ex);
        }
    }

    private KeySound GetKey(string key)
    {
        var sound = _profile.Keys.FirstOrDefault(item => KeyNames.CanonicalKey(item.Key) == KeyNames.CanonicalKey(key));
        if (sound is not null) return sound;
        sound = new KeySound { Key = key, Zone = KeyNames.DefaultZone(key, _profile.KeyboardLayout) };
        _profile.Keys.Add(sound);
        return sound;
    }

    private string NextProfileName()
    {
        const string baseName = "新しいプロファイル";
        var names = _store.GetProfileNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName)) return baseName;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} {suffix}";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    private string GetZone(string key) =>
        _profile.Keys.FirstOrDefault(item => KeyNames.CanonicalKey(item.Key) == KeyNames.CanonicalKey(key))?.Zone
        ?? KeyNames.DefaultZone(key, _profile.KeyboardLayout);

    private static string SafeFileName(string name) =>
        new(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());

    private IEnumerable<string> AllSoundPaths() =>
        _profile.Keys.SelectMany(k => k.PressSounds.Concat(k.ReleaseSounds));

    private void RefreshBackgroundKeys() =>
        _backgroundKeyListener?.SetTrackedKeys(_profile.Keys
            .Where(item => item.PressSounds.Count > 0 || item.ReleaseSounds.Count > 0)
            .Select(item => Enum.TryParse<Key>(KeyNames.CanonicalKey(item.Key), out var key) ? key : Key.None)
            .Where(key => key != Key.None && key != Key.System));

    private void UpdateSidebar()
    {
        if (SidebarProfileName is null) return;
        SidebarProfileName.Text = _profile.Name;
        SidebarKeyCount.Text = $"{_profile.Keys.Count(k => k.PressSounds.Count > 0 || k.ReleaseSounds.Count > 0)} キー設定済み";
    }

    private void SetActivity(string text)
    {
        if (MainContent.Children.Count == 0 || MainContent.Children[0] is not StackPanel page) return;
        var target = FindNamedText(page, "ActivityText");
        if (target is not null) target.Text = text;
    }

    private static TextBlock? FindNamedText(DependencyObject root, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock block && block.Name == name) return block;
            var nested = FindNamedText(child, name);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static bool IsTextEntryFocused() =>
        Keyboard.FocusedElement is TextBoxBase or ComboBox or PasswordBox;

    private static string NormalizeKey(Key key)
    {
        if (key == Key.System) return "";
        var normalized = key.ToString();
        return KeyNames.CanonicalKey(normalized);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _settingsSaveTimer.Stop();
        SaveCurrent();
        try
        {
            _backgroundKeyListener?.Dispose();
        }
        catch (Exception ex)
        {
            ShowError("バックグラウンド入力を停止できませんでした", ex);
        }
        _audio.Dispose();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_backgroundModeEnabled)
            _pressedKeys.Clear();
    }

    private static double KeyWidth(string key) => key switch
    {
        "Space" => 210,
        "Back" or "Return" or "LeftShift" or "RightShift" => 66,
        "Tab" or "Capital" => 55,
        "Escape" or "LeftCtrl" or "RightCtrl" or "LeftAlt" or "RightAlt" => 52,
        _ => 40
    };

    private static string ZoneLabel(string zone) => zone switch
    {
        "Edge" => "端",
        "Special" => "特殊キー",
        _ => "中央"
    };

    private static Brush ZoneBrush(string zone) => zone switch
    {
        "Edge" => Brush("#EAF6F0"),
        "Special" => Brush("#FFF4E8"),
        _ => Brush("#EAF0FF")
    };

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private void ScheduleSave()
    {
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private static CardPanel Card() => new()
    {
        Background = Brush("#FFFFFF"),
        BorderBrush = Brush("#E8EDF2"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(21),
        SnapsToDevicePixels = true
    };

    private static StackPanel Heading(string title, string description)
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 23) };
        block.Children.Add(Text(title, 23, "#24303C", FontWeights.SemiBold));
        block.Children.Add(Text(description, 12, "#78838F").WithMargin(new Thickness(0, 7, 0, 0)));
        return block;
    }

    private static TextBlock Text(string text, double size, string color, FontWeight? weight = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = Brush(color),
        FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap
    };

    private static TextBlock SectionLabel(string label) => Text(label, 11, "#697581", FontWeights.SemiBold);

    private static Border EmptyState(string text) => new()
    {
        Background = Brush("#F6F8FA"),
        BorderBrush = Brush("#EBEFF3"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(13),
        Child = Text(text, 11, "#7D8994")
    };

    private static UIElement Legend(string color, string label)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 15, 0) };
        line.Children.Add(new Border
        {
            Width = 11, Height = 11, CornerRadius = new CornerRadius(3),
            Background = Brush(color), BorderBrush = Brush("#DDE4EA"), BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 0)
        });
        line.Children.Add(Text(label, 10, "#75818D"));
        return line;
    }

    private void ShowError(string title, Exception ex) =>
        MessageBox.Show(this, $"{title}\n\n{ex.Message}", "KeyToSound Neo", MessageBoxButton.OK, MessageBoxImage.Error);
}

internal static class UiElementExtensions
{
    public static T WithName<T>(this T element, string name) where T : FrameworkElement
    {
        element.Name = name;
        return element;
    }

    public static T WithMargin<T>(this T element, Thickness margin) where T : FrameworkElement
    {
        element.Margin = margin;
        return element;
    }
}

internal sealed class CardPanel : Border
{
    public CardPanel()
    {
        Child = new StackPanel();
    }

    public UIElementCollection Children => ((StackPanel)Child).Children;
}
