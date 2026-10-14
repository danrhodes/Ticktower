using System.Windows;
using Ticktower.Core.Models;

namespace Ticktower;

/// <summary>Creates a timer from a type, or edits the descriptive fields of an existing one.</summary>
public partial class EntryWindow : Window
{
    private readonly AppHost _host;
    private readonly TimerItem? _editing;

    public EntryWindow(AppHost host, TimerType? type = null, TimerItem? editing = null)
    {
        InitializeComponent();
        _host = host;
        _editing = editing;

        TypeBox.ItemsSource = host.Types;
        PriorityBox.ItemsSource = Enum.GetValues<Priority>();

        if (editing is null)
        {
            TypeBox.SelectedItem = type ?? host.Types.FirstOrDefault();
            PriorityBox.SelectedItem = (TypeBox.SelectedItem as TimerType)?.DefaultPriority ?? Priority.P3;
            TypeBox.SelectionChanged += (_, _) =>
            {
                if (TypeBox.SelectedItem is TimerType t) PriorityBox.SelectedItem = t.DefaultPriority;
            };
        }
        else
        {
            Title = "Edit timer";
            OkButton.Content = "Save";
            TypeBox.SelectedItem = host.Types.FirstOrDefault(t => t.Id == editing.TypeId);
            TypeBox.IsEnabled = false;
            NameBox.Text = editing.Name;
            TicketBox.Text = editing.Ticket;
            ClientBox.Text = editing.Client;
            PriorityBox.SelectedItem = editing.Priority;
            NotesBox.Text = editing.Notes;
        }

        Loaded += (_, _) => NameBox.Focus();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var priority = (Priority)PriorityBox.SelectedItem;

        if (_editing is not null)
        {
            _host.Engine.Edit(_editing.Id, t =>
            {
                if (!string.IsNullOrWhiteSpace(NameBox.Text)) t.Name = NameBox.Text.Trim();
                t.Ticket = TicketBox.Text.Trim();
                t.Client = ClientBox.Text.Trim();
                t.Priority = priority;
                t.Notes = NotesBox.Text;
            });
        }
        else if (TypeBox.SelectedItem is TimerType type)
        {
            _host.Engine.Add(type.CreateTimer(NameBox.Text.Trim(), TicketBox.Text.Trim(), ClientBox.Text.Trim(), priority, NotesBox.Text));
        }
        Close();
    }
}
