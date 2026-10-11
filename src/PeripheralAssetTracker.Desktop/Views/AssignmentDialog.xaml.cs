using System.Collections.ObjectModel;
using System.Windows;
using PeripheralAssetTracker.Desktop.Presentation;
using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Desktop.Views;

internal enum AssignmentMode
{
    Register,
    Assign,
    Transfer
}

/// <summary>Register a detected device, assign it, or transfer it. Validation mirrors the CLI workflows.</summary>
internal partial class AssignmentDialog : Window
{
    private readonly AssignmentMode _mode;
    private readonly Func<Window, Task<PersonRow?>> _createPerson;
    private readonly ObservableCollection<Option<int?>> _people;
    private readonly List<Option<int?>> _computers;
    private readonly List<Option<DeviceType>> _types;

    public AssignmentDialog(
        AssignmentMode mode,
        string deviceName,
        IEnumerable<PersonRow> people,
        IEnumerable<ComputerRow> computers,
        Func<Window, Task<PersonRow?>> createPerson)
    {
        InitializeComponent();
        _mode = mode;
        _createPerson = createPerson;

        _people = new ObservableCollection<Option<int?>>([new Option<int?>(null, "Nenhum"), .. people.Select(PersonOption)]);
        _computers =
        [
            new Option<int?>(null, "Nenhum"),
            .. computers.Select(c => new Option<int?>(c.Id, c.IsCurrent ? $"{c.Hostname} (este computador)" : c.Hostname))
        ];
        _types = InventoryFilter.Categories.Where(o => o.Value is not null).Select(o => new Option<DeviceType>(o.Value!.Value, o.Label)).ToList();

        PersonBox.ItemsSource = _people;
        PersonBox.SelectedIndex = 0;
        ComputerBox.ItemsSource = _computers;
        ComputerBox.SelectedIndex = 0;
        TypeBox.ItemsSource = _types;
        TypeBox.SelectedIndex = 0;
        DeviceLabel.Text = deviceName;
        NameBox.Text = deviceName;

        (Title, ConfirmButton.Content, HintText.Text) = mode switch
        {
            AssignmentMode.Register => ("Cadastrar dispositivo", "Cadastrar",
                "Responsável e computador esperado são opcionais. Sem responsável, o dispositivo aparece com o alerta \"Sem responsável\"."),
            AssignmentMode.Assign => ("Atribuir dispositivo", "Atribuir",
                "Informe ao menos um responsável ou um computador esperado."),
            _ => ("Transferir dispositivo", "Transferir",
                "Escolha o novo responsável e/ou o novo computador. O motivo fica registrado no histórico.")
        };
        Heading.Text = Title;
        RegisterFields.Visibility = mode == AssignmentMode.Register ? Visibility.Visible : Visibility.Collapsed;
        ReasonFields.Visibility = mode == AssignmentMode.Transfer ? Visibility.Visible : Visibility.Collapsed;
        if (mode == AssignmentMode.Transfer)
        {
            PersonLabel.Text = "Novo responsável";
            ComputerLabel.Text = "Novo computador esperado";
        }

        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += (_, _) =>
        {
            if (mode == AssignmentMode.Register)
            {
                NameBox.Focus();
                NameBox.SelectAll();
            }
            else
            {
                PersonBox.Focus();
            }
        };
    }

    public DeviceType DeviceType
    {
        get => (TypeBox.SelectedItem as Option<DeviceType>)?.Value ?? DeviceType.Other;
        set => TypeBox.SelectedItem = _types.FirstOrDefault(o => o.Value == value) ?? _types[^1];
    }

    public int? PersonId
    {
        get => (PersonBox.SelectedItem as Option<int?>)?.Value;
        set => PersonBox.SelectedItem = _people.FirstOrDefault(o => o.Value == value) ?? _people[0];
    }

    public int? ComputerId
    {
        get => (ComputerBox.SelectedItem as Option<int?>)?.Value;
        set => ComputerBox.SelectedItem = _computers.FirstOrDefault(o => o.Value == value) ?? _computers[0];
    }

    public string FriendlyName => NameBox.Text.Trim();
    public string Reason => ReasonBox.Text.Trim();

    public string? Details
    {
        get => DetailsText.Text;
        set
        {
            DetailsText.Text = value;
            DetailsText.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private static Option<int?> PersonOption(PersonRow p) =>
        new(p.Id, p.Department == Labels.Missing ? p.Name : $"{p.Name} ({p.Department})");

    private async void NewPerson_Click(object sender, RoutedEventArgs e)
    {
        if (await _createPerson(this) is { } person)
        {
            var option = PersonOption(person);
            _people.Add(option);
            PersonBox.SelectedItem = option;
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var error = _mode switch
        {
            AssignmentMode.Register when FriendlyName.Length == 0 => "Informe o nome do dispositivo.",
            AssignmentMode.Assign or AssignmentMode.Transfer when PersonId is null && ComputerId is null =>
                "Informe ao menos um responsável ou um computador esperado.",
            AssignmentMode.Transfer when Reason.Length == 0 => "O motivo da transferência é obrigatório.",
            _ => null
        };

        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
