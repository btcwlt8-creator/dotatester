using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DotaLobbyOverlay;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new OverlayForm());
    }
}

internal sealed class OverlayForm : Form
{
    private const int WmCopyData = 0x004A;
    private const long CaptureTag = 0x44524F53;
    private const string NativeResource = "DotaLobbyOverlay.NetHook2.dll";

    private readonly Label status = new();
    private readonly Label subtitle = new();
    private readonly FlowLayoutPanel teams = new();
    private readonly Button hideButton = new();
    private bool injectionAttempted;

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
        subtitle.Text = "Захват NetHook встроен · пакеты обрабатываются в памяти";
        subtitle.AutoSize = true; subtitle.Location = new Point(2, 42); subtitle.ForeColor = Color.FromArgb(151, 163, 183);
        header.Controls.Add(subtitle);
        root.Controls.Add(header, 0, 0);

        status.Text = "●  Подготовка встроенного NetHook…";
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
        Shown += (_, _) => BeginInvoke(new Action(StartCapture));
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
            Text = "Запусти Steam и Dota 2. Приложение подключит встроенный NetHook к Steam и будет искать обновление состава лобби.",
            AutoSize = false, Width = Math.Max(400, teams.ClientSize.Width - 40), Height = 90,
            ForeColor = Color.FromArgb(151, 163, 183), Padding = new Padding(16),
            BackColor = Color.FromArgb(28, 34, 47), Margin = new Padding(8)
        });
        teams.Controls.Add(new Label {
            Text = "Сетевые сообщения передаются прямо в это окно через WM_COPYDATA. Отдельный bridge и named pipe не используются; .bin-дампы не создаются.",
            AutoSize = false, Width = Math.Max(400, teams.ClientSize.Width - 40), Height = 88,
            ForeColor = Color.FromArgb(105, 220, 165), Padding = new Padding(16),
            BackColor = Color.FromArgb(25, 43, 38), Margin = new Padding(8)
        });
    }

    private void StartCapture()
    {
        if (injectionAttempted) return;
        injectionAttempted = true;

        try
        {
            var resource = typeof(OverlayForm).Assembly.GetManifestResourceStream(NativeResource);
            if (resource is null)
            {
                SetStatus("●  Встроенный NetHook отсутствует в этой сборке", Color.FromArgb(255, 115, 115));
                subtitle.Text = "Нужна сборка с нативным компонентом NetHook2";
                return;
            }

            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DotaLobbyOverlay");
            Directory.CreateDirectory(folder);
            string dllPath = Path.Combine(folder, "NetHook2.dll");
            using (resource)
            using (var output = new FileStream(dllPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                resource.CopyTo(output);

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "rundll32.exe"),
                Arguments = $"\"{dllPath}\",Inject window={Handle.ToInt64()}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(startInfo);
            SetStatus("●  NetHook запускается с правами администратора. Ожидаю состав лобби…", Color.FromArgb(247, 190, 91));
        }
        catch (Win32Exception ex)
        {
            SetStatus($"●  Ошибка Windows: {ex.Message}", Color.FromArgb(255, 115, 115));
        }
        catch (Exception ex)
        {
            SetStatus($"●  Не удалось запустить NetHook: {ex.Message}", Color.FromArgb(255, 115, 115));
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmCopyData && m.LParam != IntPtr.Zero)
        {
            var copy = Marshal.PtrToStructure<CopyDataStruct>(m.LParam);
            if (copy.DataTag == new IntPtr(CaptureTag) && copy.ByteCount > 0 && copy.ByteCount <= 1_048_576 && copy.Data != IntPtr.Zero)
            {
                var bytes = new byte[copy.ByteCount];
                Marshal.Copy(copy.Data, bytes, 0, bytes.Length);
                if (DotaPacketParser.TryReadRoster(bytes, out var names))
                {
                    BeginInvoke(new Action(() => ShowRoster(names)));
                    m.Result = new IntPtr(1);
                    return;
                }
            }
        }
        base.WndProc(ref m);
    }

    private void ShowRoster(IReadOnlyList<string> names)
    {
        if (names.Count < 2) return;
        teams.Controls.Clear();
        if (names.Count >= 10)
        {
            AddTeam("КОМАНДА 1", names.Take(5).ToList(), Color.FromArgb(27, 42, 60), Color.FromArgb(122, 187, 255));
            AddTeam("КОМАНДА 2", names.Skip(5).Take(5).ToList(), Color.FromArgb(49, 32, 37), Color.FromArgb(255, 145, 132));
        }
        else
        {
            teams.Controls.Add(new Label { Text = $"Получено записей: {names.Count}. Разделение на две пятёрки появится при наличии 10 игроков.", AutoSize = true, ForeColor = Color.FromArgb(247, 190, 91), Margin = new Padding(10) });
            AddTeam("ИГРОКИ ИЗ ЛОББИ", names.ToList(), Color.FromArgb(30, 36, 49), Color.FromArgb(192, 203, 223));
        }

        subtitle.Text = "Состав получен из GC-пакета · обработка в оперативной памяти";
        SetStatus($"●  Найдено игроков: {names.Count}", Color.FromArgb(105, 220, 165));
        TopMost = true;
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        Activate();
        BringToFront();
    }

    private void AddTeam(string title, List<string> players, Color cardColor, Color accent)
    {
        teams.Controls.Add(new Label { Text = title, AutoSize = false, Width = Math.Max(400, teams.ClientSize.Width - 55), Height = 34, ForeColor = accent, Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold), Margin = new Padding(10, 10, 10, 2), TextAlign = ContentAlignment.MiddleLeft });
        int slot = 1;
        foreach (string player in players)
        {
            var card = new Panel { Width = Math.Max(400, teams.ClientSize.Width - 50), Height = 52, BackColor = cardColor, Margin = new Padding(8, 3, 8, 3) };
            card.Controls.Add(new Label { Text = $"{slot:00}", AutoSize = true, Location = new Point(12, 16), ForeColor = Color.FromArgb(139, 153, 180), Font = new Font("Segoe UI Semibold", 9, FontStyle.Bold) });
            card.Controls.Add(new Label { Text = player, AutoSize = false, Location = new Point(56, 7), Size = new Size(card.Width - 80, 38), ForeColor = Color.FromArgb(239, 243, 250), Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true });
            teams.Controls.Add(card); slot++;
        }
    }

    private void SetStatus(string text, Color color)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(text, color))); return; }
        status.Text = text; status.ForeColor = color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyDataStruct
    {
        public IntPtr DataTag;
        public int ByteCount;
        public IntPtr Data;
    }
}

internal static class DotaPacketParser
{
    private const uint ClientFromGc = 5453;
    private const ulong StaticLobbyType = 2014;

    public static bool TryReadRoster(byte[] packet, out IReadOnlyList<string> names)
    {
        names = Array.Empty<string>();
        try
        {
            if (packet.Length < 8) return false;
            uint emsg = BitConverter.ToUInt32(packet, 0) & 0x7fffffff;
            if ((emsg & 0xffff) != ClientFromGc) return false;

            int headerLength = BitConverter.ToInt32(packet, 4);
            if (headerLength < 0 || headerLength > packet.Length - 8) return false;
            var body = packet.AsSpan(8 + headerLength);
            byte[]? gcPayload = null;
            uint appId = 0;
            foreach (var field in ProtoFields(body))
            {
                if (field.Number == 1 && field.Wire == 0) appId = (uint)field.Varint;
                else if (field.Number == 3 && field.Wire == 2) gcPayload = field.Bytes;
            }
            if (appId != 570 || gcPayload is null || gcPayload.Length < 8) return false;

            uint innerType = BitConverter.ToUInt32(gcPayload, 0) & 0x7fffffff;
            int innerHeader = BitConverter.ToInt32(gcPayload, 4);
            if (innerHeader < 0 || innerHeader > gcPayload.Length - 8) return false;
            var innerBody = gcPayload.AsSpan(8 + innerHeader);
            if (innerType == 26 && TryReadUpdateMultiple(innerBody, out var updateNames))
            {
                names = updateNames;
                return names.Count >= 2;
            }
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or OverflowException)
        {
            return false;
        }
    }

    private static bool TryReadUpdateMultiple(ReadOnlySpan<byte> body, out IReadOnlyList<string> names)
    {
        names = Array.Empty<string>();
        foreach (var field in ProtoFields(body))
        {
            if ((field.Number != 1 && field.Number != 2) || field.Wire != 2) continue;
            foreach (var single in ProtoFields(field.Bytes))
            {
                if (single.Number != 2 || single.Wire != 2) continue;
                ulong type = 0;
                byte[]? objectData = null;
                foreach (var item in ProtoFields(single.Bytes))
                {
                    if (item.Number == 1 && item.Wire == 0) type = item.Varint;
                    else if (item.Number == 2 && item.Wire == 2) objectData = item.Bytes;
                }
                if (type == StaticLobbyType && objectData is not null && TryReadStaticLobby(objectData, out var found))
                {
                    if (found.Count >= 2) names = found;
                }
            }
        }
        return names.Count >= 2;
    }

    private static bool TryReadStaticLobby(byte[] objectData, out IReadOnlyList<string> names)
    {
        var result = new List<string>();
        foreach (var field in ProtoFields(objectData))
        {
            if (field.Number != 1 || field.Wire != 2) continue;
            foreach (var memberField in ProtoFields(field.Bytes))
            {
                if (memberField.Number == 1 && memberField.Wire == 2)
                {
                    string name = Encoding.UTF8.GetString(memberField.Bytes).Trim();
                    if (!string.IsNullOrWhiteSpace(name) && !result.Contains(name, StringComparer.Ordinal))
                        result.Add(name);
                }
            }
        }
        names = result;
        return result.Count >= 2;
    }

    private readonly record struct ProtoField(int Number, int Wire, ulong Varint, byte[] Bytes);

    private static List<ProtoField> ProtoFields(ReadOnlySpan<byte> source)
    {
        var result = new List<ProtoField>();
        int offset = 0;
        while (offset < source.Length)
        {
            ulong key = ReadVarint(source, ref offset);
            int number = checked((int)(key >> 3));
            int wire = (int)(key & 7);
            if (number == 0) throw new InvalidDataException("Invalid protobuf field.");
            switch (wire)
            {
                case 0:
                    result.Add(new ProtoField(number, wire, ReadVarint(source, ref offset), Array.Empty<byte>()));
                    break;
                case 1:
                    Ensure(source, offset, 8); offset += 8;
                    result.Add(new ProtoField(number, wire, 0, Array.Empty<byte>()));
                    break;
                case 2:
                    ulong length = ReadVarint(source, ref offset);
                    if (length > int.MaxValue) throw new InvalidDataException("Field too large.");
                    int count = (int)length;
                    Ensure(source, offset, count);
                    result.Add(new ProtoField(number, wire, 0, source.Slice(offset, count).ToArray()));
                    offset += count;
                    break;
                case 5:
                    Ensure(source, offset, 4); offset += 4;
                    result.Add(new ProtoField(number, wire, 0, Array.Empty<byte>()));
                    break;
                default:
                    throw new InvalidDataException("Unsupported protobuf wire type.");
            }
        }
        return result;
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> source, ref int offset)
    {
        ulong value = 0;
        int shift = 0;
        while (offset < source.Length && shift < 64)
        {
            byte b = source[offset++];
            value |= (ulong)(b & 0x7f) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
        }
        throw new InvalidDataException("Malformed protobuf varint.");
    }

    private static void Ensure(ReadOnlySpan<byte> source, int offset, int count)
    {
        if (count < 0 || offset < 0 || offset > source.Length - count)
            throw new InvalidDataException("Truncated protobuf field.");
    }
}
