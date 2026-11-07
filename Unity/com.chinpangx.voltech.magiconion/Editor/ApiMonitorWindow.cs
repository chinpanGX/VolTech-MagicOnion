using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using Unity.Scripting.LifecycleManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace VolTech.Client.Editor
{
    /// <summary>
    /// Monitor。API のリクエストとレスポンスを 1 件ずつ、ヘッダーと Body(JSON)に分けて見る。
    /// </summary>
    /// <remarks>
    /// UI Toolkit で、USS・UXML は使わずに C# だけで組み立てる。見るだけで、送り直しはしない(アプリの操作で送る)。
    /// </remarks>
    [NoAutoStaticsCleanup]
    internal sealed class ApiMonitorWindow : EditorWindow
    {
        private const float HeaderKeyWidth = 180;
        private const long RefreshIntervalMs = 200;

        // 開いたときの大きさ。一覧(4列)と詳細の両方が見えるようにする
        private static readonly Vector2 initialSize = new(1100, 640);
        private const float ListPaneWidth = 460;

        // 詳細の Body の TextField に出す文字数の上限
        private const int MaxShownBodyLength = 64 * 1024;

        private static readonly Color errorColor = new(0.95f, 0.45f, 0.4f);
        private static readonly Color pendingColor = new(0.6f, 0.6f, 0.6f);
        private static readonly Color keyColor = new(0.65f, 0.75f, 0.9f);
        private static readonly Color columnBorderColor = new(0.5f, 0.5f, 0.5f, 0.35f);

        // ドメインのリロードをまたいで残す設定
        [SerializeField]
        private bool followLatest = true;

        private readonly List<ApiTrace> shown = new();
        private MultiColumnListView list;
        private ScrollView detail;
        private string filter = "";
        private int shownVersion = -1;
        private int selectedId = -1;
        private ApiTrace detailTrace;
        private Label detailSummary;
        private VisualElement detailResult;

        // 一覧の時刻の文字列。行を描き直すたびに時刻を読み直さないように残す(キーは記録の StartedAt)
        private readonly Dictionary<string, string> timeLabels = new();

        [MenuItem("Window/VolTech MagicOnion/Monitor")]
        private static void Open()
        {
            var window = GetWindow<ApiMonitorWindow>("Monitor");
            // 小さいままだと詳細(右側)が見えないので、開いたときに広げる。ドッキングしているときは広げない
            if (!window.docked && (window.position.width < initialSize.x || window.position.height < initialSize.y))
            {
                var position = window.position;
                window.position = new Rect(position.x, position.y, Mathf.Max(position.width, initialSize.x), Mathf.Max(position.height, initialSize.y));
            }
        }

        private void CreateGUI()
        {
            rootVisualElement.Add(CreateToolbar());

            var split = new TwoPaneSplitView(0, ListPaneWidth, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            split.Add(CreateList());
            detail = new ScrollView(ScrollViewMode.Vertical);
            detail.style.paddingLeft = 8;
            detail.style.paddingRight = 8;
            detail.style.paddingTop = 4;
            split.Add(detail);
            rootVisualElement.Add(split);

            ShowDetail(null);
            // 記録は別スレッドから足されるので、UI はここで定期的に版を見て描き直す
            rootVisualElement.schedule.Execute(Refresh).Every(RefreshIntervalMs);
        }

        private Toolbar CreateToolbar()
        {
            var toolbar = new Toolbar();

            var clear = new ToolbarButton(() => ApiTraceStore.Shared.Clear()) { text = "Clear" };
            toolbar.Add(clear);

            var recordToggle = new ToolbarToggle { text = "Record", value = ApiTraceStore.Shared.Recording };
            recordToggle.RegisterValueChangedCallback(change => ApiTraceStore.Shared.Recording = change.newValue);
            toolbar.Add(recordToggle);

            var clearOnPlayToggle = new ToolbarToggle { text = "Clear on Play", value = ApiMonitorStartup.ClearOnPlay };
            clearOnPlayToggle.RegisterValueChangedCallback(change => ApiMonitorStartup.ClearOnPlay = change.newValue);
            toolbar.Add(clearOnPlayToggle);

            var followToggle = new ToolbarToggle { text = "Follow latest", value = followLatest };
            followToggle.RegisterValueChangedCallback(change => followLatest = change.newValue);
            toolbar.Add(followToggle);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            toolbar.Add(spacer);

            var search = new ToolbarSearchField();
            search.RegisterValueChangedCallback(change =>
            {
                filter = change.newValue ?? "";
                shownVersion = -1;
            });
            toolbar.Add(search);

            return toolbar;
        }

        // 一覧: 時刻・API(RPC の短い名前)・結果・通信時間の4列。列の幅は見出しの境目で変えられる
        private MultiColumnListView CreateList()
        {
            list = new MultiColumnListView
            {
                fixedItemHeight = 20,
                selectionType = SelectionType.Single,
                itemsSource = shown,
                // 1行おきに背景の色を変えて、行を目で追いやすくする
                showAlternatingRowBackgrounds = AlternatingRowBackground.All,
                showBorder = true,
            };
            list.columns.Add(CreateColumn("time", "Time", 70, false, TimeLabel));
            list.columns.Add(CreateColumn("api", "API", 140, true, trace => trace.ShortMethod));
            list.columns.Add(CreateColumn("result", "Result", 150, false, StateText));
            list.columns.Add(CreateColumn("duration", "Duration", 70, false, trace => trace.DurationMs < 0 ? "" : $"{trace.DurationMs:0} ms", TextAnchor.MiddleRight));
            list.style.flexGrow = 1;
            list.selectionChanged += selected =>
            {
                var trace = selected.OfType<ApiTrace>().FirstOrDefault();
                selectedId = trace?.Id ?? -1;
                ShowDetail(trace);
            };
            return list;
        }

        private Column CreateColumn(string name, string title, float width, bool stretchable, Func<ApiTrace, string> text, TextAnchor align = TextAnchor.MiddleLeft)
        {
            return new Column
            {
                name = name,
                title = title,
                width = width,
                minWidth = 40,
                stretchable = stretchable,
                resizable = true,
                sortable = false,
                makeCell = () => MakeCell(name == "time", align),
                bindCell = (element, index) => BindCell((Label)element, shown[index], text),
            };
        }

        private static Label MakeCell(bool first, TextAnchor align)
        {
            var label = new Label();
            label.style.paddingLeft = 4;
            label.style.paddingRight = 4;
            label.style.unityTextAlign = align;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.flexGrow = 1;
            // 列の区切りを、セルの左の線で見せる
            if (!first)
            {
                label.style.borderLeftWidth = 1;
                label.style.borderLeftColor = columnBorderColor;
            }

            return label;
        }

        private static void BindCell(Label label, ApiTrace trace, Func<ApiTrace, string> text)
        {
            label.text = text(trace);
            label.style.color = trace.State switch
            {
                ApiTraceState.Failed => errorColor,
                ApiTraceState.Pending => pendingColor,
                _ => (StyleColor)StyleKeyword.Null,
            };
        }

        private void Refresh()
        {
            var store = ApiTraceStore.Shared;
            if (store.Version == shownVersion)
            {
                return;
            }

            shownVersion = store.Version;
            shown.Clear();
            shown.AddRange(store.Snapshot().Where(trace =>
                string.IsNullOrEmpty(filter) || trace.Method.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0));
            list.RefreshItems();

            var index = followLatest && shown.Count > 0
                ? shown.Count - 1
                : shown.FindIndex(trace => trace.Id == selectedId);
            if (index >= 0)
            {
                list.SetSelectionWithoutNotify(new[] { index });
                // 最新を追うときだけスクロールする。追わないときは、手で動かした一覧の位置をそのままにする
                if (followLatest)
                {
                    list.ScrollToItem(index);
                }

                // 送信中だった記録が埋まったら、詳細も描き直す
                if (!ReferenceEquals(shown[index], detailTrace))
                {
                    selectedId = shown[index].Id;
                    ShowDetail(shown[index]);
                }
            }
            else
            {
                list.ClearSelection();
                ShowDetail(null);
            }
        }

        // 同じ通信の記録が埋まった(送信中 → 結果)ときは、Request は変わらないので、上の行と Response・Error だけを作り直す。
        // Body の TextField は大きいと組み立てが重いので、作り直す数を減らす
        private void ShowDetail(ApiTrace trace)
        {
            var sameCall = trace != null && detailTrace != null && detailResult != null
                && trace.Id == detailTrace.Id;
            detailTrace = trace;
            if (sameCall)
            {
                detailSummary.text = SummaryText(trace);
                detailResult.Clear();
                AddResult(detailResult, trace);
                return;
            }

            detail.Clear();
            detailSummary = null;
            detailResult = null;
            if (trace == null)
            {
                detail.Add(new Label("通信を選ぶと、ヘッダーと Body を表示します。"));
                return;
            }

            var title = new Label(trace.Method);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.selection.isSelectable = true;
            detail.Add(title);
            detailSummary = new Label(SummaryText(trace));
            detail.Add(detailSummary);

            var request = Section("Request");
            request.Add(HeadersSection("Headers", trace.RequestHeaders));
            request.Add(BodySection(trace.RequestBody));
            detail.Add(request);

            detailResult = new VisualElement();
            AddResult(detailResult, trace);
            detail.Add(detailResult);
        }

        private static string SummaryText(ApiTrace trace)
        {
            var duration = trace.DurationMs < 0 ? "" : $"  {trace.DurationMs:0} ms";
            var deadline = string.IsNullOrEmpty(trace.Deadline) ? "" : $"  deadline {trace.Deadline}";
            return $"{FormatTime(trace.StartedAt, "yyyy-MM-dd HH:mm:ss.fff")}{duration}  {StateText(trace)}{deadline}";
        }

        private static void AddResult(VisualElement container, ApiTrace trace)
        {
            var response = Section("Response");
            if (trace.State == ApiTraceState.Pending)
            {
                response.Add(new Label("応答を待っています。"));
            }
            else
            {
                response.Add(HeadersSection("Headers", trace.ResponseHeaders));
                response.Add(BodySection(trace.ResponseBody));
                response.Add(HeadersSection("Trailers", trace.Trailers));
            }

            container.Add(response);

            if (trace.State == ApiTraceState.Failed)
            {
                var error = Section("Error");
                var rows = new List<ApiTraceHeader>
                {
                    new("status", trace.StatusCode),
                    new("detail", trace.StatusDetail),
                };
                if (!string.IsNullOrEmpty(trace.ErrorReason))
                {
                    rows.Add(new ApiTraceHeader("reason", trace.ErrorReason));
                    rows.Add(new ApiTraceHeader("domain", trace.ErrorDomain));
                }

                error.Add(HeaderRows(rows));
                error.Add(HeadersSection("Metadata", trace.ErrorMetadata));
                container.Add(error);
            }
        }

        private static Foldout Section(string text)
        {
            var foldout = new Foldout { text = text, value = true };
            foldout.style.marginTop = 6;
            foldout.Q<Toggle>().style.unityFontStyleAndWeight = FontStyle.Bold;
            return foldout;
        }

        // ヘッダーは項目ごとに1行(名前の Label と値の Label)
        private static Foldout HeadersSection(string text, List<ApiTraceHeader> headers)
        {
            var foldout = new Foldout { text = $"{text} ({headers?.Count ?? 0})", value = true };
            foldout.Add(HeaderRows(headers));
            return foldout;
        }

        private static VisualElement HeaderRows(List<ApiTraceHeader> headers)
        {
            var rows = new VisualElement();
            if (headers == null || headers.Count == 0)
            {
                rows.Add(new Label("(none)"));
                return rows;
            }

            foreach (var header in headers)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = 1;

                var key = new Label(header.Key);
                key.style.width = HeaderKeyWidth;
                key.style.flexShrink = 0;
                key.style.color = keyColor;
                key.selection.isSelectable = true;
                row.Add(key);

                // 値は選んで一部だけコピーできるようにする
                var value = new Label(header.Value);
                value.style.flexGrow = 1;
                value.style.flexShrink = 1;
                value.style.whiteSpace = WhiteSpace.Normal;
                value.selection.isSelectable = true;
                row.Add(value);

                rows.Add(row);
            }

            return rows;
        }

        // Body は読み取り専用の複数行の TextField に JSON を出し、Copy ボタンで全体をコピーできるようにする。
        // ConvertToJson は字下げしないので、出すときに字下げする
        private static VisualElement BodySection(string rawBody)
        {
            var body = IndentJson(rawBody);

            var section = new VisualElement();
            section.style.marginTop = 2;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            var label = new Label("Body");
            label.style.flexGrow = 1;
            header.Add(label);
            var copy = new Button(() => EditorGUIUtility.systemCopyBuffer = body ?? "") { text = "Copy" };
            copy.SetEnabled(!string.IsNullOrEmpty(body));
            header.Add(copy);
            section.Add(header);

            // 大きな Body をそのまま TextField に入れるとエディタが固まるので、表示は先頭だけにする(Copy は全体)
            var shownBody = string.IsNullOrEmpty(body) ? "" : body;
            if (shownBody.Length > MaxShownBodyLength)
            {
                shownBody = shownBody.Substring(0, MaxShownBodyLength)
                    + $"\n... (表示は先頭 {MaxShownBodyLength} 文字まで。全体は Copy でコピーする)";
            }

            var text = new TextField
            {
                multiline = true,
                isReadOnly = true,
                value = shownBody,
            };
            text.style.whiteSpace = WhiteSpace.Normal;
            section.Add(text);
            return section;
        }

        private static string StateText(ApiTrace trace)
        {
            return trace.State switch
            {
                ApiTraceState.Pending => "Pending",
                ApiTraceState.Succeeded => "OK",
                _ => string.IsNullOrEmpty(trace.ErrorReason) ? trace.StatusCode : trace.ErrorReason,
            };
        }

        private string TimeLabel(ApiTrace trace)
        {
            var iso = trace.StartedAt ?? "";
            if (!timeLabels.TryGetValue(iso, out var label))
            {
                // 一覧に出る記録は Capacity 件までなので、それを大きく超えたら作り直す
                if (timeLabels.Count > ApiTraceStore.Capacity * 2)
                {
                    timeLabels.Clear();
                }

                label = FormatTime(iso, "HH:mm:ss");
                timeLabels[iso] = label;
            }

            return label;
        }

        // 字下げの無い JSON を、2 文字ずつ字下げした形にする。文字列の中は変えない。
        // JSON でないもの({ か [ で始まらない)は、そのまま返す
        private static string IndentJson(string json)
        {
            if (string.IsNullOrEmpty(json) || (json[0] != '{' && json[0] != '['))
            {
                return json;
            }

            var builder = new StringBuilder(json.Length * 2);
            var depth = 0;
            var inString = false;
            for (var i = 0; i < json.Length; i++)
            {
                var c = json[i];
                if (inString)
                {
                    builder.Append(c);
                    if (c == '\\' && i + 1 < json.Length)
                    {
                        builder.Append(json[++i]);
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        builder.Append(c);
                        break;
                    case '{':
                    case '[':
                        builder.Append(c);
                        var next = NextNonSpace(json, i + 1);
                        if (next < json.Length && (json[next] == '}' || json[next] == ']'))
                        {
                            // 空のオブジェクト・配列は {} / [] のまま
                            builder.Append(json[next]);
                            i = next;
                        }
                        else
                        {
                            depth++;
                            NewLine(builder, depth);
                        }

                        break;
                    case '}':
                    case ']':
                        depth = Math.Max(0, depth - 1);
                        NewLine(builder, depth);
                        builder.Append(c);
                        break;
                    case ',':
                        builder.Append(c);
                        NewLine(builder, depth);
                        break;
                    case ':':
                        builder.Append(": ");
                        break;
                    default:
                        // 文字列の外の空白は捨て、字下げを付け直す
                        if (!char.IsWhiteSpace(c))
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            return builder.ToString();
        }

        private static int NextNonSpace(string text, int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            return index;
        }

        private static void NewLine(StringBuilder builder, int depth)
        {
            builder.Append('\n');
            builder.Append(' ', depth * 2);
        }

        private static string FormatTime(string iso, string format)
        {
            return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
                ? time.ToString(format, CultureInfo.InvariantCulture)
                : iso;
        }
    }
}
