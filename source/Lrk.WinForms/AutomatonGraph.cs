using System.Drawing.Drawing2D;
using System.Text;

namespace Lrk.WinForms;

public sealed record GraphEdge(int From, int To, string Symbol);

public sealed class AutomatonGraph : UserControl
{
    private const int OverviewLimit = 40;
    private const int NeighborsPerSide = 12;
    private static readonly Color Ink = Color.FromArgb(22, 38, 55);
    private static readonly Color Muted = Color.FromArgb(80, 100, 120);
    private static readonly Color Accent = Color.FromArgb(22, 107, 98);
    private static readonly Color Conflict = Color.FromArgb(158, 52, 41);
    private static readonly Color Surface = Color.FromArgb(242, 245, 249);

    private readonly Label _caption = new()
    {
        Dock = DockStyle.Top,
        Height = 44,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 8, 0),
        ForeColor = Muted,
        BackColor = Color.White
    };

    private readonly Panel _viewport = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        BackColor = Surface
    };

    private readonly GraphCanvas _canvas = new();
    private GraphEdge[] _edges = [];
    private ILookup<int, GraphEdge> _outgoing = Array.Empty<GraphEdge>().ToLookup(edge => edge.From);
    private ILookup<int, GraphEdge> _incoming = Array.Empty<GraphEdge>().ToLookup(edge => edge.To);
    private HashSet<int> _conflictStates = [];
    private int _stateCount;
    private int? _selectedState;

    public AutomatonGraph()
    {
        AccessibleName = "Граф автомата";
        _viewport.Controls.Add(_canvas);
        Controls.Add(_viewport);
        Controls.Add(_caption);
        _canvas.StateClicked += state => SelectedState = state;
        _canvas.MoveSelection += delta =>
        {
            if (_stateCount > 0)
                SelectedState = Math.Clamp((_selectedState ?? 0) + delta, 0, _stateCount - 1);
        };
        _viewport.SizeChanged += (_, _) => EnsureSelectedVisible();
        RebuildDiagram();
    }

    public event EventHandler? SelectedStateChanged;

    public int? SelectedState
    {
        get => _selectedState;
        set
        {
            if (value is int state && (state < 0 || state >= _stateCount))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (value == _selectedState)
                return;

            _selectedState = value;
            if (_stateCount > OverviewLimit)
                RebuildDiagram();

            _canvas.SelectedState = value;
            EnsureSelectedVisible();
            SelectedStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetGraph(int stateCount, IEnumerable<GraphEdge> edges, IEnumerable<int> conflictStates)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stateCount);
        _stateCount = stateCount;
        _edges = edges.Distinct().OrderBy(edge => edge.From).ThenBy(edge => edge.To)
            .ThenBy(edge => edge.Symbol, StringComparer.Ordinal).ToArray();
        _outgoing = _edges.ToLookup(edge => edge.From);
        _incoming = _edges.ToLookup(edge => edge.To);
        _conflictStates = conflictStates.ToHashSet();

        int? previousSelection = _selectedState;
        _selectedState = stateCount > 0 ? 0 : null;
        _canvas.SelectedState = _selectedState;
        RebuildDiagram();
        EnsureSelectedVisible();
        if (previousSelection != _selectedState)
            SelectedStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public string ExportDot()
    {
        var dot = new StringBuilder();
        dot.AppendLine("digraph LRk {");
        dot.AppendLine("  rankdir=LR;");
        dot.AppendLine("  node [shape=box, style=rounded];");
        if (_stateCount > 0)
        {
            dot.AppendLine("  start [shape=point];");
            dot.AppendLine("  start -> I0;");
        }
        for (int state = 0; state < _stateCount; state++)
        {
            dot.Append($"  I{state}");
            if (_conflictStates.Contains(state))
                dot.Append(" [color=\"#9E3429\", fontcolor=\"#9E3429\", label=\"I").Append(state).Append("\\nКонфликт\"]");
            dot.AppendLine(";");
        }
        foreach (GraphEdge edge in _edges)
            dot.AppendLine($"  I{edge.From} -> I{edge.To} [label=\"{EscapeDot(edge.Symbol)}\"];");
        dot.AppendLine("}");
        return dot.ToString();
    }

    private static string EscapeDot(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);

    private void RebuildDiagram()
    {
        _viewport.AutoScrollPosition = Point.Empty;
        List<int>[] columns = _stateCount <= OverviewLimit ? OverviewColumns() : FocusedColumns();
        HashSet<int> visible = columns.SelectMany(column => column).ToHashSet();

        var edges = _stateCount <= OverviewLimit
            ? _edges
            : visible.Order().SelectMany(state => _outgoing[state]).Where(edge => visible.Contains(edge.To)).ToArray();

        _canvas.SetDiagram(columns, edges, _conflictStates);
        if (_stateCount == 0)
        {
            _caption.Text = "После проверки здесь появятся состояния и переходы.";
            return;
        }

        if (_stateCount > OverviewLimit)
        {
            int focus = _selectedState ?? 0;
            _caption.Text = $"Окрестность I{focus} · показано {visible.Count} из {_stateCount} состояний.\n"
                + "Другие состояния можно выбрать в списке. Экспорт DOT включает весь граф.";
            return;
        }

        _caption.Text = "Выберите состояние, чтобы увидеть его ситуации. Подпись на стрелке — символ перехода.";
    }

    private List<int>[] OverviewColumns()
    {
        if (_stateCount == 0)
            return [];

        var queue = new Queue<int>();
        var levels = new Dictionary<int, int> { [0] = 0 };
        queue.Enqueue(0);
        while (queue.TryDequeue(out int state))
        {
            foreach (GraphEdge edge in _outgoing[state])
            {
                if (levels.TryAdd(edge.To, levels[state] + 1))
                    queue.Enqueue(edge.To);
            }
        }

        int lastLevel = levels.Values.Max();
        return Enumerable.Range(0, _stateCount)
            .GroupBy(state => levels.GetValueOrDefault(state, lastLevel + 1))
            .OrderBy(group => group.Key)
            .Select(group => group.ToList())
            .ToArray();
    }

    private List<int>[] FocusedColumns()
    {
        int focus = _selectedState ?? 0;
        List<int> incoming = _incoming[focus].Select(edge => edge.From).Where(state => state != focus)
            .Distinct().Order().Take(NeighborsPerSide).ToList();
        var outgoing = _outgoing[focus].Select(edge => edge.To)
            .Where(state => state != focus && !incoming.Contains(state)).Distinct().Order()
            .Take(NeighborsPerSide).ToList();
        return new List<int>[] { incoming, [focus], outgoing }.Where(column => column.Count > 0).ToArray();
    }

    private void EnsureSelectedVisible()
    {
        if (_selectedState is not int state || !_canvas.TryGetNode(state, out Rectangle node))
            return;

        Point position = _viewport.AutoScrollPosition;
        Size visibleSize = _viewport.ClientSize;
        var viewport = new Rectangle(-position.X, -position.Y, visibleSize.Width, visibleSize.Height);
        if (visibleSize.Width > 0 && visibleSize.Height > 0 && !viewport.Contains(node))
        {
            _viewport.AutoScrollPosition = new Point(
                Math.Max(0, node.Left + node.Width / 2 - visibleSize.Width / 2),
                Math.Max(0, node.Top + node.Height / 2 - visibleSize.Height / 2));
        }
    }

    private sealed class GraphCanvas : Control
    {
        private const int NodeWidth = 104;
        private const int NodeHeight = 50;
        private const int HorizontalStride = 220;
        private const int VerticalStride = 122;
        private const int LeftMargin = 84;
        private const int TopMargin = 64;

        private readonly Dictionary<int, Rectangle> _nodes = [];
        private readonly List<DiagramEdge> _edges = [];
        private HashSet<int> _conflicts = [];
        private int? _active;

        public GraphCanvas()
        {
            SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = true;
            BackColor = Surface;
            AccessibleName = "Состояния автомата";
            AccessibleDescription = "Стрелками клавиатуры можно выбрать предыдущее или следующее состояние.";
        }

        public event Action<int>? StateClicked;
        public event Action<int>? MoveSelection;

        public int? SelectedState
        {
            get => _active;
            set
            {
                _active = value;
                Invalidate();
            }
        }

        public bool TryGetNode(int state, out Rectangle node) => _nodes.TryGetValue(state, out node);

        public void SetDiagram(List<int>[] columns, IEnumerable<GraphEdge> edges, HashSet<int> conflicts)
        {
            _nodes.Clear();
            _edges.Clear();
            _conflicts = conflicts;
            if (columns.Length == 0)
            {
                Size = new Size(1, 1);
                Invalidate();
                return;
            }

            int rowCount = columns.Max(column => column.Count);
            for (int column = 0; column < columns.Length; column++)
            {
                int offset = (rowCount - columns[column].Count) * VerticalStride / 2;
                for (int row = 0; row < columns[column].Count; row++)
                {
                    _nodes.Add(columns[column][row], new Rectangle(
                        LeftMargin + column * HorizontalStride,
                        TopMargin + row * VerticalStride + offset, NodeWidth, NodeHeight));
                }
            }

            foreach (var group in edges.GroupBy(edge => (edge.From, edge.To)))
            {
                string[] symbols = group.Select(edge => edge.Symbol).ToArray();
                string label = string.Join("  ", symbols.Take(4));
                if (symbols.Length > 4)
                    label += $"  +{symbols.Length - 4}";
                _edges.Add(new DiagramEdge(group.Key.From, group.Key.To, label));
            }

            Size = new Size(_nodes.Values.Max(node => node.Right) + 78, _nodes.Values.Max(node => node.Bottom) + 64);
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is
            Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.Up:
                    MoveSelection?.Invoke(-1);
                    break;
                case Keys.Right:
                case Keys.Down:
                    MoveSelection?.Invoke(1);
                    break;
                default:
                    return;
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            foreach (var (state, node) in _nodes)
            {
                if (!node.Contains(e.Location))
                    continue;
                Focus();
                StateClicked?.Invoke(state);
                break;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = _nodes.Values.Any(node => node.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var arrow = new AdjustableArrowCap(4, 5, true);
            using var ordinary = new Pen(Color.FromArgb(127, 149, 167), 1.25f) { CustomEndCap = arrow };
            using var active = new Pen(Accent, 1.65f) { CustomEndCap = arrow };
            using var paper = new SolidBrush(Surface);
            var labels = new List<(DiagramEdge Edge, PointF Position, bool Highlighted)>();
            foreach (DiagramEdge edge in _edges.OrderBy(IsSelectedEdge))
            {
                bool highlighted = IsSelectedEdge(edge);
                PointF position = DrawEdge(g, highlighted ? active : ordinary, _nodes[edge.From], _nodes[edge.To], edge);
                labels.Add((edge, position, highlighted));
            }
            foreach (var (edge, position, highlighted) in labels)
            {
                Size text = TextRenderer.MeasureText(edge.Label, Font);
                var bounds = new Rectangle((int)position.X - text.Width / 2, (int)position.Y - text.Height / 2,
                    text.Width, text.Height);
                g.FillRectangle(paper, bounds);
                TextRenderer.DrawText(g, edge.Label, Font, bounds, highlighted ? Accent : Ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            if (_nodes.TryGetValue(0, out Rectangle initial))
            {
                int middleY = initial.Top + initial.Height / 2;
                g.DrawLine(ordinary, initial.Left - 38, middleY, initial.Left - 2, middleY);
                TextRenderer.DrawText(g, "Старт", Font, new Point(initial.Left - 61, middleY - Font.Height - 2), Muted);
            }

            foreach (var (state, node) in _nodes)
            {
                if (e.ClipRectangle.IntersectsWith(Rectangle.Inflate(node, 2, 2)))
                    DrawNode(g, state, node);
            }
        }

        private bool IsSelectedEdge(DiagramEdge edge) => edge.From == _active || edge.To == _active;

        private static PointF DrawEdge(Graphics g, Pen pen, Rectangle from, Rectangle to, DiagramEdge edge)
        {
            float fromX = from.Left + from.Width / 2f;
            float toX = to.Left + to.Width / 2f;
            float fromY = from.Top + from.Height / 2f;
            float toY = to.Top + to.Height / 2f;

            if (edge.From == edge.To)
            {
                g.DrawBezier(pen, fromX + 22, from.Top, fromX + 68, from.Top - 54,
                    fromX - 68, from.Top - 54, fromX - 22, from.Top - 2);
                return new PointF(fromX, from.Top - 41);
            }

            if (from.Left == to.Left)
            {
                float lane = from.Right + (to.Top > from.Top ? 36 : 62);
                g.DrawBezier(pen, from.Right, fromY, lane, fromY, lane, toY, to.Right + 2, toY);
                return new PointF(from.Right * 0.25f + lane * 0.75f, (fromY + toY) / 2f);
            }

            if (from.Left > to.Left || to.Left - from.Left > HorizontalStride)
            {
                // Route reverse and longer edges above the node row to distinguish them from forward arrows.
                float lift = Math.Min(from.Top, to.Top) - 17 - (edge.From + edge.To) % 3 * 10;
                PointF[] route =
                [
                    new(fromX - 18, from.Top), new(fromX - 18, lift),
                    new(toX + 18, lift), new(toX + 18, to.Top - 2)
                ];
                g.DrawLines(pen, route);
                return new PointF((fromX + toX) / 2f, lift);
            }

            float middleX = (from.Right + to.Left) / 2f;
            g.DrawBezier(pen, from.Right, fromY, middleX, fromY, middleX, toY, to.Left - 2, toY);
            return new PointF(middleX, (fromY + toY) / 2f - 1);
        }

        private void DrawNode(Graphics g, int state, Rectangle rectangle)
        {
            bool conflict = _conflicts.Contains(state);
            bool selected = state == _active;
            Color outline = conflict ? Conflict : selected ? Accent : Color.FromArgb(169, 188, 204);
            Color fill = conflict ? Color.FromArgb(255, 237, 231)
                : selected ? Color.FromArgb(220, 244, 237) : Color.White;
            using var body = RoundedRectangle(rectangle, 11);
            using var brush = new SolidBrush(fill);
            using var pen = new Pen(outline, selected ? 2.25f : 1.1f);
            g.FillPath(brush, body);
            g.DrawPath(pen, body);

            var heading = rectangle;
            if (conflict)
                heading.Height = rectangle.Height / 2 + 4;
            TextRenderer.DrawText(g, $"I{state}", Font, heading, Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (conflict)
            {
                var subtitle = Rectangle.FromLTRB(rectangle.Left, heading.Bottom - 2, rectangle.Right, rectangle.Bottom - 3);
                TextRenderer.DrawText(g, "Конфликт", Font, subtitle, Conflict,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private sealed record DiagramEdge(int From, int To, string Label);
    }
}
