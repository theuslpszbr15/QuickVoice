using System.Windows;

namespace QuickVoice;

internal partial class KeyWindow : Window
{
    public string Key => Field.Password.Trim();

    public KeyWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Activate();
            Field.Focus();
        };
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (Key.Length > 0) DialogResult = true;
    }
}
