using System.Windows;

namespace PeripheralAssetTracker.Desktop.Views;

internal partial class PersonDialog : Window
{
    public PersonDialog()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += (_, _) => NameBox.Focus();
    }

    public string PersonName => NameBox.Text.Trim();
    public string? Department => string.IsNullOrWhiteSpace(DepartmentBox.Text) ? null : DepartmentBox.Text.Trim();

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (PersonName.Length == 0)
        {
            ErrorText.Text = "Informe o nome do responsável.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
