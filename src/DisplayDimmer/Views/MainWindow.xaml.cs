using System.Windows;
using System.Windows.Input;
using DisplayDimmer.ViewModels;

namespace DisplayDimmer.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _allowClose;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Hide();
            }
        };
    }

    public event EventHandler? SettingsRequested;

    public MainViewModel ViewModel => _viewModel;

    /// <summary>Fecha de verdade (usado ao sair pelo menu da bandeja).</summary>
    public void CloseForReal()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>Mostra a janela centralizada na tela atual.</summary>
    public void ShowAndFocus()
    {
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void Reset_Click(object sender, RoutedEventArgs e) => _viewModel.ResetLevels();

    private void ToggleLevels_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => _viewModel.ToggleLevels();

    private void BlackLevelBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyBlackLevel();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            BlackLevelBox.Text = _viewModel.BlackLevelText;
            e.Handled = true;
        }
    }

    private void BlackLevelBox_LostFocus(object sender, RoutedEventArgs e) => ApplyBlackLevel();

    private void WhiteLevelBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyWhiteLevel();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            WhiteLevelBox.Text = _viewModel.WhiteLevelText;
            e.Handled = true;
        }
    }

    private void WhiteLevelBox_LostFocus(object sender, RoutedEventArgs e) => ApplyWhiteLevel();

    private void ApplyBlackLevel()
    {
        if (Services.ColorMath.TryParseHexLevel(BlackLevelBox.Text, out var level))
        {
            _viewModel.BlackLevel = level;
        }

        BlackLevelBox.Text = _viewModel.BlackLevelText;
    }

    private void ApplyWhiteLevel()
    {
        if (Services.ColorMath.TryParseHexLevel(WhiteLevelBox.Text, out var level))
        {
            _viewModel.WhiteLevel = level;
        }

        WhiteLevelBox.Text = _viewModel.WhiteLevelText;
    }
}
