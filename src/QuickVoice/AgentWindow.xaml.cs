using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace QuickVoice;

/// <summary>Where Copilot runs the spoken tasks, with which model and which MCP plugins.</summary>
internal partial class AgentWindow : Window
{
    private readonly Settings settings;
    private readonly ObservableCollection<McpServer> servers;

    public AgentWindow(Settings settings)
    {
        this.settings = settings;
        InitializeComponent();
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 40);
        servers = new(settings.McpServers.Select(s => new McpServer { Enabled = s.Enabled, Name = s.Name, Command = s.Command, Args = s.Args }));
        McpGrid.ItemsSource = servers;
        CliRadio.IsChecked = settings.AgentHarness != "vscode";
        CodeRadio.IsChecked = settings.AgentHarness == "vscode";
        OpenCodeCheck.IsChecked = settings.AgentOpensVsCode;
        AutonomousCheck.IsChecked = settings.AgentAutonomous;
        FolderBox.Text = settings.AgentFolder;
        Loaded += async (_, _) =>
        {
            Activate();
            Reveal();
            await RefreshAsync();
        };
    }

    /// <summary>The cards rise into place one after another.</summary>
    private void Reveal()
    {
        var delay = 0;
        foreach (var card in Cards.Children.OfType<FrameworkElement>())
        {
            var shift = new TranslateTransform(0, 18);
            card.RenderTransform = shift;
            card.Opacity = 0;
            var begin = TimeSpan.FromMilliseconds(delay);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(380)) { BeginTime = begin, EasingFunction = ease });
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(320)) { BeginTime = begin });
            delay += 60;
        }
    }

    private async Task RefreshAsync()
    {
        var version = await Agent.CliVersionAsync();
        StatusDot.Fill = new SolidColorBrush(version is null ? Color.FromRgb(0xE3, 0x7C, 0x00) : Color.FromRgb(0x1E, 0x8E, 0x3E));
        StatusTitle.Text = version is null ? "Copilot CLI não instalado" : $"{version.TrimEnd('.')} pronto";
        StatusHint.Text = version is null
            ? "Instale (precisa do Node.js) e entre com a conta GitHub que tem o Copilot. Sem ele, as tarefas vão para o chat do VS Code."
            : "Na primeira vez, entre na conta GitHub que tem o Copilot. O login fica salvo no PC.";
        InstallButton.Visibility = version is null ? Visibility.Visible : Visibility.Collapsed;
        LoginButton.IsEnabled = version is not null;

        ModelsState.Text = "carregando…";
        var models = await Agent.ModelsAsync();
        ModelList.ItemsSource = models;
        ModelList.SelectedItem = models.FirstOrDefault(m => m.Id == settings.AgentModel) ?? models[0];
        ModelList.ScrollIntoView(ModelList.SelectedItem);
        ModelsState.Text = $"{models.Count - 1} modelos";
    }

    /// <summary>Five bars, as many lit as the model is fast.</summary>
    private void OnSpeedBars(object sender, RoutedEventArgs e)
    {
        if (sender is not StackPanel panel || panel.Children.Count > 0 || panel.Tag is not int speed || speed == 0) return;
        var lit = 6 - speed;
        var color = speed switch { <= 2 => Color.FromRgb(0x1E, 0x8E, 0x3E), 3 => Color.FromRgb(0x0A, 0x62, 0xD0), _ => Color.FromRgb(0x8E, 0x44, 0xAD) };
        for (var i = 0; i < 5; i++)
            panel.Children.Add(new Rectangle
            {
                Width = 5, Height = 6 + i * 2.5, Margin = new Thickness(0, 0, 2, 0), RadiusX = 1.5, RadiusY = 1.5, VerticalAlignment = VerticalAlignment.Bottom,
                Fill = i < lit ? new SolidColorBrush(color) : new SolidColorBrush(Color.FromRgb(0xDD, 0xE1, 0xE8)),
            });
    }

    private void Apply()
    {
        McpGrid.CommitEdit(DataGridEditingUnit.Row, true);
        settings.AgentHarness = CodeRadio.IsChecked == true ? "vscode" : "copilot-cli";
        settings.AgentModel = (ModelList.SelectedItem as AgentModel)?.Id ?? "auto";
        settings.AgentOpensVsCode = OpenCodeCheck.IsChecked == true;
        settings.AgentAutonomous = AutonomousCheck.IsChecked == true;
        settings.AgentFolder = string.IsNullOrWhiteSpace(FolderBox.Text) ? new Settings().AgentFolder : FolderBox.Text.Trim();
        settings.McpServers = servers.Where(s => s.Name.Trim().Length > 0).ToList();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Apply();
        try
        {
            settings.Save();
            DialogResult = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SavedText.Text = $"Não salvou: {error.Message}";
        }
    }

    private async void OnRun(object sender, RoutedEventArgs e)
    {
        var task = TaskBox.Text.Trim();
        if (task.Length == 0) return;
        Apply();
        try
        {
            settings.Save();
            RunState.Text = "Começando…";
            RunState.Text = await Agent.StartAsync(task, settings);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            RunState.Text = $"Não começou: {error.Message}";
        }
    }

    private async void OnCheckModel(object sender, RoutedEventArgs e)
    {
        if (ModelList.SelectedItem is not AgentModel model) return;
        CheckButton.IsEnabled = false;
        CheckState.Text = $"Perguntando ao Copilot com {model}…";
        var (ok, message) = await Agent.CheckModelAsync(model.Id);
        CheckState.Foreground = new SolidColorBrush(ok ? Color.FromRgb(0x1E, 0x8E, 0x3E) : Color.FromRgb(0xB3, 0x26, 0x1E));
        CheckState.Text = $"{model}: {message}";
        CheckButton.IsEnabled = true;
    }

    private void OnInstall(object sender, RoutedEventArgs e)
    {
        Agent.InstallCli();
        StatusHint.Text = "Instalando num terminal… Quando terminar, feche e abra esta tela de novo.";
    }

    private void OnLogin(object sender, RoutedEventArgs e) => Agent.Login();

    private void OnAddPlaywright(object sender, RoutedEventArgs e) => Add(McpServer.Playwright());
    private void OnAddFiles(object sender, RoutedEventArgs e) => Add(McpServer.Files(FolderBox.Text.Trim()));
    private void OnAddFetch(object sender, RoutedEventArgs e) => Add(McpServer.Fetch());
    private void OnAddCustom(object sender, RoutedEventArgs e) => Add(new McpServer { Name = "meu-servidor", Command = "npx", Args = "-y pacote-mcp" });

    private void Add(McpServer server)
    {
        if (servers.Any(s => s.Name == server.Name)) server.Name += $"-{servers.Count + 1}";
        servers.Add(server);
        McpGrid.SelectedItem = server;
        McpGrid.ScrollIntoView(server);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (McpGrid.SelectedItem is McpServer server) servers.Remove(server);
    }

    private void OnPickFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Pasta onde o agente cria os projetos", InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : null };
        if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName;
    }
}
