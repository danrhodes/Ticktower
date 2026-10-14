using System.Collections.ObjectModel;
using System.Windows;
using Ticktower.Core.Engine;

namespace Ticktower;

public partial class BoardWindow : Window
{
    private readonly AppHost _host;
    private readonly Dictionary<Guid, CardViewModel> _cards = new();
    private readonly ObservableCollection<CardViewModel> _active = new();
    private readonly ObservableCollection<CardViewModel> _expired = new();
    private readonly ObservableCollection<CardViewModel> _acknowledged = new();

    public BoardWindow(AppHost host)
    {
        InitializeComponent();
        _host = host;
        ActiveList.ItemsSource = _active;
        ExpiredList.ItemsSource = _expired;
        AckList.ItemsSource = _acknowledged;

        host.Ticked += Sync;
        host.Engine.Changed += Sync;
        Sync();
    }

    private void Sync()
    {
        var timers = _host.Engine.Timers;
        var now = DateTime.UtcNow;

        foreach (var id in _cards.Keys.Where(id => timers.All(t => t.Id != id)).ToList())
            _cards.Remove(id);
        foreach (var t in timers)
            if (!_cards.ContainsKey(t.Id)) _cards[t.Id] = new CardViewModel(t, _host);

        Fill(_active, Lane.Active, now);
        Fill(_expired, Lane.Expired, now);
        Fill(_acknowledged, Lane.Acknowledged, now);

        ActiveHeader.Text = $"Active ({_active.Count})";
        ExpiredHeader.Text = $"Expired ({_expired.Count})";
        AckHeader.Text = $"Acknowledged ({_acknowledged.Count})";
    }

    /// <summary>Only rebuilds a lane when membership or order changed, so buttons stay clickable between ticks.</summary>
    private void Fill(ObservableCollection<CardViewModel> lane, Lane which, DateTime now)
    {
        var desired = BoardLogic.InLane(_host.Engine.Timers, which).Select(t => _cards[t.Id]).ToList();
        if (!lane.SequenceEqual(desired))
        {
            lane.Clear();
            foreach (var vm in desired) lane.Add(vm);
        }
        foreach (var vm in lane) vm.Refresh(now);
    }
}
