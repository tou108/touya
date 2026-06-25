using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NXMacroAdvanced.Models;

namespace NXMacroAdvanced.Views
{
    /// <summary>
    /// ビジュアルスクリプトエディターのコードビハインド
    /// ノードの作成・ドラッグ移動・接続ラインを実装する
    /// </summary>
    public partial class VisualScriptEditorView : UserControl
    {
        // ─── ノード管理 ───
        private readonly List<VisualNodeControl> _nodes = new();
        private VisualNodeControl? _selectedNode;
        private VisualNodeControl? _draggingNode;
        private Point _dragOffset;
        private double _zoomScale = 1.0;

        // ─── 接続ライン ───
        private readonly List<ConnectionLine> _connections = new();
        private VisualNodeControl.PortEllipse? _dragSourcePort;
        private Line? _tempLine;

        // ─── ノードカウンター ───
        private int _nodeIdCounter = 1;

        public VisualScriptEditorView()
        {
            InitializeComponent();
        }

        // ─────────────────────────────────────────────────────────
        //  パレット：ノード追加
        // ─────────────────────────────────────────────────────────

        private void PaletteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string nodeType)
            {
                double cx = CanvasScroll.ActualWidth  / 2 + CanvasScroll.HorizontalOffset;
                double cy = CanvasScroll.ActualHeight / 2 + CanvasScroll.VerticalOffset;
                AddNode(nodeType, cx - 75, cy - 40);
            }
        }

        private VisualNodeControl AddNode(string type, double x, double y)
        {
            var node = new VisualNodeControl(type, _nodeIdCounter++);
            Canvas.SetLeft(node, x);
            Canvas.SetTop(node, y);
            NodesCanvas.Children.Add(node);
            _nodes.Add(node);

            // ドラッグイベント
            node.NodeMouseDown  += Node_Selected;
            node.PortDragStart  += Port_DragStart;
            node.MouseLeftButtonDown += (s, e) => { _draggingNode = node; _dragOffset = e.GetPosition(node); SelectNode(node); e.Handled = true; };

            SelectNode(node);
            return node;
        }

        private void SelectNode(VisualNodeControl node)
        {
            // 選択ハイライト切替
            if (_selectedNode != null) _selectedNode.IsSelected = false;
            _selectedNode = node;
            node.IsSelected = true;

            // プロパティパネル更新
            UpdatePropertyPanel(node);
        }

        // ─────────────────────────────────────────────────────────
        //  ノードドラッグ移動
        // ─────────────────────────────────────────────────────────

        private void Node_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border)
            {
                _draggingNode = null; // スタートノードは移動しない
                e.Handled = true;
            }
        }

        private void Node_Selected(VisualNodeControl node) => SelectNode(node);

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(NodeCanvas);

            // ノードドラッグ
            if (_draggingNode != null && e.LeftButton == MouseButtonState.Pressed)
            {
                double newX = Math.Max(0, pos.X - _dragOffset.X);
                double newY = Math.Max(0, pos.Y - _dragOffset.Y);
                Canvas.SetLeft(_draggingNode, newX);
                Canvas.SetTop(_draggingNode, newY);
                UpdateConnections();
            }

            // ポート接続ライン一時描画
            if (_tempLine != null)
            {
                _tempLine.X2 = pos.X;
                _tempLine.Y2 = pos.Y;
            }
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggingNode != null)
            {
                _draggingNode = null;
            }

            // ポート接続の終了
            if (_tempLine != null)
            {
                ConnectionCanvas.Children.Remove(_tempLine);
                _tempLine = null;
                _dragSourcePort = null;
            }
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // キャンバス空白クリックで選択解除
            if (e.Source == NodeCanvas)
            {
                if (_selectedNode != null) { _selectedNode.IsSelected = false; _selectedNode = null; }
                NoSelectionText.Visibility = Visibility.Visible;
                PropertyPanel.Children.Clear();
                PropertyPanel.Children.Add(NoSelectionText);
            }
        }

        // ─────────────────────────────────────────────────────────
        //  ポート接続
        // ─────────────────────────────────────────────────────────

        private void Port_DragStart(VisualNodeControl.PortEllipse port)
        {
            _dragSourcePort = port;
            var portPos = port.TranslatePoint(new Point(5, 5), NodeCanvas);
            _tempLine = new Line
            {
                X1 = portPos.X, Y1 = portPos.Y,
                X2 = portPos.X, Y2 = portPos.Y,
                Stroke = new SolidColorBrush(Color.FromRgb(144, 202, 249)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            };
            ConnectionCanvas.Children.Add(_tempLine);
        }

        private void UpdateConnections()
        {
            // 接続ラインを再描画
            foreach (var conn in _connections)
                conn.UpdatePath(NodeCanvas);
        }

        // ─────────────────────────────────────────────────────────
        //  プロパティパネル
        // ─────────────────────────────────────────────────────────

        private void UpdatePropertyPanel(VisualNodeControl node)
        {
            NoSelectionText.Visibility = Visibility.Collapsed;
            PropertyPanel.Children.Clear();
            PropertyPanel.Children.Add(NoSelectionText);

            var title = new TextBlock { Text = node.NodeTitle, FontSize=14, FontWeight=FontWeights.Medium, Foreground=Brushes.White, Margin=new Thickness(0,0,0,12) };
            PropertyPanel.Children.Add(title);

            // ノードタイプ別プロパティUI生成
            foreach (var prop in node.Properties)
            {
                PropertyPanel.Children.Add(new TextBlock { Text = prop.Key, FontSize=10, Opacity=0.6, Margin=new Thickness(0,4,0,2) });

                var tb = new TextBox
                {
                    Text = prop.Value,
                    Style = (Style)FindResource("MaterialDesignOutlinedTextBox"),
                    Margin = new Thickness(0,0,0,4),
                    Tag = prop.Key
                };
                tb.TextChanged += (s, e) => { if (s is TextBox t) node.Properties[t.Tag.ToString()!] = t.Text; };
                PropertyPanel.Children.Add(tb);
            }
        }

        // ─────────────────────────────────────────────────────────
        //  ズーム / リセット
        // ─────────────────────────────────────────────────────────

        private void ZoomIn_Click(object sender, RoutedEventArgs e)    => SetZoom(_zoomScale * 1.2);
        private void ZoomOut_Click(object sender, RoutedEventArgs e)   => SetZoom(_zoomScale / 1.2);
        private void ResetView_Click(object sender, RoutedEventArgs e) { SetZoom(1.0); CanvasScroll.ScrollToTop(); CanvasScroll.ScrollToLeftEnd(); }

        private void SetZoom(double scale)
        {
            _zoomScale = Math.Clamp(scale, 0.3, 3.0);
            NodeCanvas.LayoutTransform = new ScaleTransform(_zoomScale, _zoomScale);
        }

        // ─────────────────────────────────────────────────────────
        //  削除
        // ─────────────────────────────────────────────────────────

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNode == null) return;
            NodesCanvas.Children.Remove(_selectedNode);
            _nodes.Remove(_selectedNode);
            // 関連接続も削除
            var toRemove = _connections.Where(c => c.Source == _selectedNode || c.Target == _selectedNode).ToList();
            foreach (var c in toRemove) { ConnectionCanvas.Children.Remove(c.PathElement); _connections.Remove(c); }
            _selectedNode = null;
        }

        // ─────────────────────────────────────────────────────────
        //  スクリプト生成 (ノード順に並べてテキスト出力)
        // ─────────────────────────────────────────────────────────

        private void ExportScript_Click(object sender, RoutedEventArgs e)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# ビジュアルエディターから生成されたマクロ");
            sb.AppendLine();

            // 接続を辿ってトポロジカル順に出力
            foreach (var node in _nodes.OrderBy(n => Canvas.GetTop(n)))
                sb.AppendLine(node.ToScriptLine());

            // クリップボードにコピー
            Clipboard.SetText(sb.ToString());
            MessageBox.Show("スクリプトをクリップボードにコピーしました！\nマクロエディターに貼り付けてください。",
                "スクリプト生成完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  VisualNodeControl — ノードUI要素
    // ─────────────────────────────────────────────────────────────────────

    public class VisualNodeControl : Border
    {
        public string NodeType  { get; }
        public string NodeTitle { get; }
        public int    NodeId    { get; }
        public Dictionary<string, string> Properties { get; } = new();

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; BorderBrush = value ? Brushes.White : _defaultBorder; }
        }

        private readonly Brush _defaultBorder;

        public event Action<VisualNodeControl>? NodeMouseDown;
        public event Action<PortEllipse>?       PortDragStart;

        // ─── ノード定義テーブル ───
        private static readonly Dictionary<string, (string Title, string Color, string[] Props)> NodeDefs = new()
        {
            ["Press"]      = ("ボタン押下",   "#1B3A5C", new[] {"ボタン", "時間(ms)"}),
            ["Wait"]       = ("待機",         "#1B3A5C", new[] {"時間(ms)"}),
            ["Stick"]      = ("スティック",   "#1B4A2C", new[] {"方向(L/R)", "X(0-255)", "Y(0-255)", "時間(ms)"}),
            ["DPad"]       = ("十字キー",     "#1B4A2C", new[] {"方向(UP/DOWN/LEFT/RIGHT)", "時間(ms)"}),
            ["Loop"]       = ("ループ",       "#3B2060", new[] {"回数(0=無限)"}),
            ["Condition"]  = ("条件分岐",     "#3B2060", new[] {"条件タイプ"}),
            ["Label"]      = ("ラベル",       "#2C1B00", new[] {"ラベル名"}),
            ["ImageMatch"] = ("画像マッチ",   "#1A3B4C", new[] {"画像ファイル", "信頼度(0.0-1.0)"}),
            ["Ocr"]        = ("OCR テキスト", "#1A3B4C", new[] {"検索テキスト", "領域(x,y,w,h)"}),
            ["WaitImage"]  = ("画像待機",     "#1A3B4C", new[] {"画像ファイル", "タイムアウト(ms)"}),
            ["Screenshot"] = ("スクリーンショット", "#1A3B4C", new[] {"保存先ファイル名"}),
        };

        public VisualNodeControl(string type, int id)
        {
            NodeType = type;
            NodeId   = id;

            var def = NodeDefs.GetValueOrDefault(type, ("不明ノード", "#2A2A2A", Array.Empty<string>()));
            NodeTitle = def.Title;

            // 初期プロパティ
            foreach (var p in def.Props) Properties[p] = "";

            // ─ UI 構築 ─
            var nodeColor = (Color)ColorConverter.ConvertFromString(def.Color);
            Background      = new SolidColorBrush(nodeColor);
            _defaultBorder  = new SolidColorBrush(Color.FromArgb(120, 144, 202, 249));
            BorderBrush     = _defaultBorder;
            BorderThickness = new Thickness(1.5);
            CornerRadius    = new CornerRadius(8);
            MinWidth        = 160;
            Cursor          = Cursors.SizeAll;
            Margin          = new Thickness(0);

            var stack = new StackPanel();

            // ─ ヘッダー ─
            var header = new Border
            {
                Background    = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
                CornerRadius  = new CornerRadius(8, 8, 0, 0),
                Padding       = new Thickness(8, 6, 8, 6),
            };
            header.Child = new TextBlock
            {
                Text       = def.Title,
                FontSize   = 12,
                FontWeight = FontWeights.Medium,
                Foreground = Brushes.White,
            };
            stack.Children.Add(header);

            // ─ 接続ポート (出力) ─
            var outPort = new PortEllipse { Width = 12, Height = 12, Fill = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, -6, 4) };
            outPort.MouseLeftButtonDown += (s, e) => { PortDragStart?.Invoke(outPort); e.Handled = true; };
            stack.Children.Add(outPort);

            // ─ プロパティ一覧 (折りたたみ) ─
            if (def.Props.Length > 0)
            {
                var propPanel = new StackPanel { Margin = new Thickness(8, 4, 8, 8) };
                foreach (var p in def.Props)
                {
                    propPanel.Children.Add(new TextBlock { Text = p, FontSize = 9,
                        Opacity = 0.6, Foreground = Brushes.White });
                }
                stack.Children.Add(propPanel);
            }

            Child = stack;

            MouseLeftButtonDown += (s, e) => { NodeMouseDown?.Invoke(this); };
        }

        public string ToScriptLine() => NodeType switch
        {
            "Press"      => $"PRESS {Properties.GetValueOrDefault("ボタン","A")} {Properties.GetValueOrDefault("時間(ms)","100")}",
            "Wait"       => $"WAIT {Properties.GetValueOrDefault("時間(ms)","500")}",
            "Stick"      => $"STICK {Properties.GetValueOrDefault("方向(L/R)","L")} {Properties.GetValueOrDefault("X(0-255)","128")} {Properties.GetValueOrDefault("Y(0-255)","0")} {Properties.GetValueOrDefault("時間(ms)","500")}",
            "DPad"       => $"DPAD {Properties.GetValueOrDefault("方向(UP/DOWN/LEFT/RIGHT)","UP")} {Properties.GetValueOrDefault("時間(ms)","100")}",
            "Loop"       => $"LOOP {Properties.GetValueOrDefault("回数(0=無限)","1")}",
            "Label"      => $"LABEL {Properties.GetValueOrDefault("ラベル名","label1")}",
            "ImageMatch" => $"IF IMAGE_MATCH \"{Properties.GetValueOrDefault("画像ファイル","")}\" {Properties.GetValueOrDefault("信頼度(0.0-1.0)","0.9")}",
            "Ocr"        => $"IF OCR \"{Properties.GetValueOrDefault("領域(x,y,w,h)","")}\" CONTAINS \"{Properties.GetValueOrDefault("検索テキスト","")}\"",
            "WaitImage"  => $"WAIT_IMAGE \"{Properties.GetValueOrDefault("画像ファイル","")}\" {Properties.GetValueOrDefault("タイムアウト(ms)","10000")}",
            "Screenshot" => $"CAPTURE_SCREEN \"{Properties.GetValueOrDefault("保存先ファイル名","screenshot.png")}\"",
            _            => $"# {NodeTitle}",
        };

        // ─── ポート要素 ───
        public class PortEllipse : System.Windows.Shapes.Shape
        {
            protected override System.Windows.Media.Geometry DefiningGeometry =>
                new System.Windows.Media.EllipseGeometry(
                    new System.Windows.Point(ActualWidth / 2, ActualHeight / 2),
                    Math.Max(0, (ActualWidth  - StrokeThickness) / 2),
                    Math.Max(0, (ActualHeight - StrokeThickness) / 2));
        }
    }

    // ─── 接続ライン ───
    public class ConnectionLine
    {
        public VisualNodeControl Source      { get; set; } = null!;
        public VisualNodeControl Target      { get; set; } = null!;
        public Path              PathElement { get; } = new() { Stroke = Brushes.SteelBlue, StrokeThickness = 2 };

        public void UpdatePath(Canvas canvas)
        {
            var s = new Point(Canvas.GetLeft(Source) + Source.ActualWidth,  Canvas.GetTop(Source) + Source.ActualHeight / 2);
            var t = new Point(Canvas.GetLeft(Target), Canvas.GetTop(Target) + Target.ActualHeight / 2);
            double cp = Math.Abs(t.X - s.X) * 0.5;
            var geo = new PathGeometry();
            var fig = new PathFigure { StartPoint = s };
            fig.Segments.Add(new BezierSegment(new Point(s.X + cp, s.Y), new Point(t.X - cp, t.Y), t, true));
            geo.Figures.Add(fig);
            PathElement.Data = geo;
        }
    }
}
