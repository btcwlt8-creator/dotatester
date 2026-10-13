using System.Drawing;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace DotaLobbyOverlay;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new OverlayForm());
    }
}

internal sealed class OverlayForm : Form
{
    private const string PipeName = "DotaLobbyOverlay.LiveRoster";
    private readonly Label status = new();
    private readonly Label subtitle = new();
    private readonly FlowLayoutPanel teams = new();
    private readonly Button hideButton = new();
    private CancellationTokenSource? listenerCts;

    public OverlayForm()
    {
        Text = "Dota Lobby Overlay";
        Size = new Size(760, 690);
        MinimumSize = new Size(620, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(15, 18, 26);
        ForeColor = Color.FromArgb(239, 243, 250);
        Font = new Font("Segoe UI", 10);
        ShowInTaskbar = true;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(20) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(new Label { Text = "DOTA 2  /  LOBBY RADAR", AutoSize = true, Location = new Point(0, 0), Font = new Font("Segoe UI Semibold", 19, FontStyle.Bold), ForeColor = Color.FromArgb(241, 245, 253) });
        subtitle.Text = "Список игроков обрабатывается в памяти — файлы дампов не создаются этой программой.";
        subtitle.AutoSize = true; subtitle.Location = new Point(2, 42); subtitle.ForeColor = Color.FromArgb(151, 163, 183);
        header.Controls.Add(subtitle);
        root.Controls.Add(header, 0, 0);

        status.Text = "●  Ожидаю данные от интеграции NetHookAnalyzer2…";
        status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft;
        status.ForeColor = Color.FromArgb(247, 190, 91);
        root.Controls.Add(status, 0, 1);

        teams.Dock = DockStyle.Fill; teams.AutoScroll = true; teams.FlowDirection = FlowDirection.TopDown; teams.WrapContents = false;
        teams.BackColor = Color.FromArgb(20, 24, 34);
        root.Controls.Add(teams, 0, 2);

        var footer = new Panel { Dock = DockStyle.Fill };
        hideButton.Text = "Скрыть окно"; hideButton.Width = 130; hideButton.Height = 36; hideButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        hideButton.Left = 570; hideButton.Top = 2; StyleButton(hideButton);
        hideButton.Click += (_, _) => Hide();
        footer.Controls.Add(hideButton);
        footer.Resize += (_, _) => hideButton.Left = footer.ClientSize.Width - hideButton.Width;
        root.Controls.Add(footer, 0, 3);

        RenderWaiting();
        Shown += (_, _) => StartListener();
        FormClosed += (_, _) => { listenerCts?.Cancel(); listenerCts?.Dispose(); };
    }

    private static void StyleButton(Button b)
    {
        b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
        b.BackColor = Color.FromArgb(78, 105, 229); b.ForeColor = Color.White;
        b.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold); b.Cursor = Cursors.Hand;
    }

    private void RenderWaiting()
    {
        teams.Controls.Clear();
        teams.Controls.Add(new Label {
            Text = "Окно готово. После интеграции с NetHookAnalyzer2 список появится автоматически, без сохранения .bin-дампов на диск.",
            AutoSize = false, Width = Math.Max(400, teams.ClientSize.Width - 40), Height = 90,
            ForeColor = Color.FromArgb(151, 163, 183), Padding = new Padding(16),
            BackColor = Color.FromArgb(28, 34, 47), Margin = new Padding(8)
        });
        teams.Controls.Add(new Label {
            Text = "Важно: этот EXE не подключается к игре сам. Он принимает roster в памяти от адаптера NetHookAnalyzer2 через локальный named pipe.",
            AutoSize = false, Width = Math.Max(400, teams.ClientSize.Width - 40), Height = 76,
            ForeColor = Color.FromArgb(247, 190, 91), Padding = new Padding(16),
            BackColor = Color.FromArgb(43, 37, 26), Margin = new Padding(8)
        });
    }

    private void StartListener()
    {
        listenerCts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoop(listenerCts.Token));
    }

    private async Task ListenLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(token);
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
                var line = await reader.ReadLineAsync(token);
                if (string.IsNullOrWhiteSpace(line)) continue;
                var roster = JsonSerializer.Deserialize<RosterMessage>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (roster?.Players is { Count: >= 2 })
                    BeginInvoke(new Action(() => ShowRoster(roster)));
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { await Task.Delay(200, token).ContinueWith(_ => { }); }
            catch (JsonException) { SetStatus("●  Получено некорректное сообщение", Color.FromArgb(255, 115, 115)); }
            catch (ObjectDisposedException) { break; }
        }
    }

    private void ShowRoster(RosterMessage roster)
    {
        teams.Controls.Clear();
        // Preserve the packet's player order: in the supplied capture the first five
        // entries are one side and the next five are the other side.
        var ordered = roster.Players!.ToList();
        if (ordered.Count >= 10)
        {
            AddTeam("КОМАНДА 1", ordered.Take(5).ToList(), Color.FromArgb(27, 42, 60), roster.MyAccountId);
            AddTeam("КОМАНДА 2", ordered.Skip(5).Take(5).ToList(), Color.FromArgb(49, 32, 37), roster.MyAccountId);
            if (ordered.Count > 10)
                AddTeam("ДРУГИЕ ЗАПИСИ", ordered.Skip(10).ToList(), Color.FromArgb(30, 36, 49), roster.MyAccountId);
        }
        else
        {
            teams.Controls.Add(new Label { Text = "Пока получено меньше 10 записей — показываю список без разделения на две пятёрки.", AutoSize = true, ForeColor = Color.FromArgb(247, 190, 91), Margin = new Padding(10) });
            AddTeam("ИГРОКИ ИЗ ПАКЕТА", ordered, Color.FromArgb(30, 36, 49), roster.MyAccountId);
        }
        var selfFound = !string.IsNullOrWhiteSpace(roster.MyAccountId) && ordered.Any(p => !string.IsNullOrWhiteSpace(p.AccountId) && p.AccountId == roster.MyAccountId);
        subtitle.Text = selfFound ? "Ваш аккаунт отмечен зелёной меткой · обработка в памяти" : "Игроки получены · свой аккаунт пока не сопоставлен";
        SetStatus($"●  Найдено игроков: {ordered.Count}", Color.FromArgb(105, 220, 165));
        TopMost = true; WindowState = FormWindowState.Normal; Show(); Activate(); BringToFront(); TopMost = true;
    }

    private void AddTeam(string title, List<PlayerInfo> players, Color cardColor, string? myAccountId)
    {
        teams.Controls.Add(new Label { Text = title, AutoSize = false, Width = Math.Max(400, teams.ClientSize.Width - 55), Height = 34, ForeColor = title.Contains("1") ? Color.FromArgb(122, 187, 255) : Color.FromArgb(255, 145, 132), Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold), Margin = new Padding(10, 10, 10, 2), TextAlign = ContentAlignment.MiddleLeft });
        int slot = 1;
        foreach (var player in players)
        {
            bool isMe = !string.IsNullOrWhiteSpace(myAccountId) && !string.IsNullOrWhiteSpace(player.AccountId) && player.AccountId == myAccountId;
            var card = new Panel { Width = Math.Max(400, teams.ClientSize.Width - 50), Height = 52, BackColor = isMe ? Color.FromArgb(31, 65, 53) : cardColor, Margin = new Padding(8, 3, 8, 3) };
            card.Controls.Add(new Label { Text = $"{slot:00}", AutoSize = true, Location = new Point(12, 16), ForeColor = Color.FromArgb(139, 153, 180), Font = new Font("Segoe UI Semibold", 9, FontStyle.Bold) });
            card.Controls.Add(new Label { Text = player.Name ?? "(без ника)", AutoSize = false, Location = new Point(56, 7), Size = new Size(card.Width - 160, 38), ForeColor = Color.FromArgb(239, 243, 250), Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true });
            if (isMe) card.Controls.Add(new Label { Text = "ЭТО ВЫ", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(card.Width - 88, 17), ForeColor = Color.FromArgb(105, 220, 165), Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold) });
            teams.Controls.Add(card); slot++;
        }
    }

    private void SetStatus(string text, Color color)
    {
        status.Text = text; status.ForeColor = color;
    }
}

internal sealed class RosterMessage
{
    public List<PlayerInfo>? Players { get; set; }
    public string? MyAccountId { get; set; }
}
internal sealed class PlayerInfo
{
    public string? Name { get; set; }
    public int Team { get; set; } = -1;
    public int Slot { get; set; }
    public string? AccountId { get; set; }
}
