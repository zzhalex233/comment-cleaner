using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using Microsoft.Win32;
using CommentCleanerWpf.Core;

using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CommentCleanerWpf;

public partial class MainWindow : Window
{
    private string? _pickedFolder;
    private System.Collections.Generic.List<string> _pickedFiles = new();

    private FileJobs.Result? _lastResult;

    public MainWindow()
    {
        InitializeComponent();
        ThemeCombo.SelectionChanged += (_, _) => ApplyTheme();
        ModeCombo.SelectionChanged += (_, _) => RefreshModeUi();

        ApplyTheme();
        RefreshModeUi();

        ChangedFileCombo.IsEnabled = false;
    }

    private void ApplyTheme()
    {
        bool dark = ThemeCombo.SelectedIndex == 1;

        Color bg = (Color)FindResource(dark ? "BgDark" : "BgLight");
        Color panel = (Color)FindResource(dark ? "PanelDark" : "PanelLight");
        Color fg = (Color)FindResource(dark ? "FgDark" : "FgLight");
        Color muted = (Color)FindResource(dark ? "MutedDark" : "MutedLight");
        Color border = (Color)FindResource(dark ? "BorderDark" : "BorderLight");
        Color field = (Color)FindResource(dark ? "FieldDark" : "FieldLight");
        Color delBg = (Color)FindResource(dark ? "DelBgDark" : "DelBgLight");
        Color hover = (Color)FindResource(dark ? "HoverDark" : "HoverLight");
        Color selected = (Color)FindResource(dark ? "SelectedDark" : "SelectedLight");
        Color accent = (Color)FindResource(dark ? "AccentDark" : "AccentLight");

        Resources["HoverBrush"] = new SolidColorBrush(hover);
        Resources["SelectedBrush"] = new SolidColorBrush(selected);
        Resources["BgBrush"] = new SolidColorBrush(bg);
        Resources["PanelBrush"] = new SolidColorBrush(panel);
        Resources["FgBrush"] = new SolidColorBrush(fg);
        Resources["MutedBrush"] = new SolidColorBrush(muted);
        Resources["BorderBrush"] = new SolidColorBrush(border);
        Resources["FieldBrush"] = new SolidColorBrush(field);
        Resources["DelBrush"] = new SolidColorBrush(delBg);
        Resources["AccentBrush"] = new SolidColorBrush(accent);

        Background = (Brush)Resources["BgBrush"];

        InvalidateVisual();
    }

    private void RefreshModeUi()
    {
        bool folderMode = ModeCombo.SelectedIndex == 0;

        PickFolderBtn.IsEnabled = folderMode;
        PickFilesBtn.IsEnabled = !folderMode;

        if (folderMode)
        {
            _pickedFiles.Clear();
            PathBox.Text = _pickedFolder ?? "";
        }
        else
        {
            _pickedFolder = null;
            PathBox.Text = _pickedFiles.Count > 0 ? $"已选择 {_pickedFiles.Count} 个文件" : "";
        }
    }

    private void FillCommon_Click(object sender, RoutedEventArgs e)
    {
        SuffixBox.Text = ".cpp,.cc,.cxx,.h,.hpp,.java,.cs,.js,.jsx,.ts,.tsx,.go,.rs,.kt,.kts,.swift,.py";
    }

    private void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var folder = FolderPickerWin32.PickFolder(hwnd, "选择工作区文件夹");
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            _pickedFolder = folder;
            PathBox.Text = _pickedFolder;
        }
    }

    private Task<string?> PickFolderWin11Async()
    {
        var tcs = new TaskCompletionSource<string?>();

        Dispatcher.InvokeAsync(async () =>
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add("*"); 

            var hwnd = new WindowInteropHelper(this).Handle;
            InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            tcs.SetResult(folder?.Path);
        });

        return tcs.Task;
    }

    private void PickFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择要处理的文件（可多选）",
            Multiselect = true,
            Filter = "All files (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            _pickedFiles = dlg.FileNames.ToList();
            PathBox.Text = $"已选择 {_pickedFiles.Count} 个文件";
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        LogBox.Clear();
        UnifiedDiffBox.Clear();
        SideGrid.ItemsSource = null;

        _lastResult = null;
        ChangedFileCombo.ItemsSource = null;
        ChangedFileCombo.IsEnabled = false;

        StatusText.Text = "已清空";
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            LogBox.Clear();
            UnifiedDiffBox.Clear();
            SideGrid.ItemsSource = null;

            _lastResult = null;
            ChangedFileCombo.ItemsSource = null;
            ChangedFileCombo.IsEnabled = false;

            bool folderMode = ModeCombo.SelectedIndex == 0;
            bool dryRun = DryRunCheck.IsChecked == true;
            bool backup = BackupCheck.IsChecked == true && !dryRun;
            bool ignoreHidden = IgnoreHiddenCheck.IsChecked == true;

            bool clearCommentOnly = ClearCommentOnlyLinesCheck.IsChecked == true;
            bool deleteCleared = DeleteClearedLinesCheck.IsChecked == true;

            var suffixes = FileJobs.ParseSuffixes(SuffixBox.Text);
            if (suffixes.Count == 0)
            {
                MessageBox.Show("请填写至少一个后缀，例如：.cpp,.java", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            System.Collections.Generic.List<string> targets;
            if (folderMode)
            {
                if (string.IsNullOrWhiteSpace(_pickedFolder) || !Directory.Exists(_pickedFolder))
                {
                    MessageBox.Show("文件夹模式下请选择有效工作区目录。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                targets = FileJobs.CollectFilesBySuffix(_pickedFolder!, suffixes, ignoreHidden);
            }
            else
            {
                targets = _pickedFiles
                    .Where(f => suffixes.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                if (targets.Count == 0)
                {
                    MessageBox.Show("文件模式：没有匹配后缀的文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            StatusText.Text = $"开始处理：{targets.Count} 个文件…";

            var opt = new FileJobs.Options(
                DryRun: dryRun,
                Backup: backup,
                ClearCommentOnlyLines: clearCommentOnly,
                DeleteClearedLines: deleteCleared
            );

            var runner = new FileJobs();
            var result = await Task.Run(() =>
                runner.ProcessFiles(targets, _pickedFolder, opt,
                    log: s => Dispatcher.Invoke(() => AppendLog(s)))
            );

            StatusText.Text = $"完成：扫描 {result.Total}，修改 {result.Changed}，失败 {result.Failed}";

            if (!dryRun)
            {
                _lastResult = null;
                return;
            }

            _lastResult = result;

            var changedFiles = result.UnifiedDiffs.Keys.OrderBy(x => x).ToList();
            ChangedFileCombo.ItemsSource = changedFiles;
            ChangedFileCombo.IsEnabled = changedFiles.Count > 0;

            if (changedFiles.Count > 0)
                ChangedFileCombo.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "异常", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ChangedFileCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_lastResult is null) return;
        if (ChangedFileCombo.SelectedItem is not string file) return;

        if (_lastResult.UnifiedDiffs.TryGetValue(file, out var ud))
            UnifiedDiffBox.Text = ud;
        else
            UnifiedDiffBox.Clear();

        if (!_lastResult.SideBySide.TryGetValue(file, out var rows))
        {
            SideGrid.ItemsSource = null;
            return;
        }

        var list = rows.Select(x => new SideRow(x.Left, x.Right, x.Kind)).ToList();
        SideGrid.ItemsSource = list;

        SideGrid.LoadingRow -= SideGrid_LoadingRow;
        SideGrid.LoadingRow += SideGrid_LoadingRow;
    }

    private void SideGrid_LoadingRow(object? sender, System.Windows.Controls.DataGridRowEventArgs e)
    {
        if (e.Row.Item is SideRow r && r.Kind == DiffUtil.RowKind.Deleted)
            e.Row.Background = (Brush)Resources["DelBrush"];
        else
            e.Row.ClearValue(System.Windows.Controls.DataGridRow.BackgroundProperty);
    }

    private void AppendLog(string s)
    {
        LogBox.AppendText(s + Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    private record SideRow(string Left, string Right, DiffUtil.RowKind Kind);
}