using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BukaMusicDesktop.Core;

/// <summary>Languages the console can be shown in.</summary>
public enum UiLanguage { Zh, En, Ja, Ko }

/// <summary>
/// UI strings of the whole console in the four supported languages.
///
/// The table is keyed by the Chinese source text, so XAML keeps its readable
/// Chinese literal and <see cref="Apply"/> swaps the texts of a page once when
/// it is navigated to. Switching the language therefore only has to re-navigate
/// the current page - every element is re-created from these literals and the
/// new language is applied again.
/// </summary>
public sealed class Loc
{
    private readonly Dictionary<string, string[]> _table = new(StringComparer.Ordinal);

    public static Loc Current { get; } = new();

    public UiLanguage Language { get; private set; } = UiLanguage.Zh;

    /// <summary>How many elements the last <see cref="Apply"/> translated.</summary>
    public int LastTranslatedCount { get; private set; }

    /// <summary>Looks a key up in the current language (unknown keys pass through).</summary>
    public string this[string key] => Text(key);

    public string Text(string key)
    {
        if (key.Length == 0) return key;
        if (!_table.TryGetValue(key, out string[]? values)) return key;
        return values[(int)Language];
    }

    /// <summary>Text with {0}, {1} … placeholders filled in.</summary>
    public string Text(string key, params object?[] args)
        => string.Format(Text(key), args);

    public void SetLanguage(UiLanguage language) => Language = language;

    /// <summary>
    /// Text for a tag the device could not read. Missing tags arrive as an empty
    /// string or as the Chinese placeholder ("未知歌手"), both show as「暂无」.
    /// </summary>
    public string Tag(string value, string chinesePlaceholder)
        => string.IsNullOrWhiteSpace(value) || value == chinesePlaceholder
            ? Text("暂无")
            : Text(value);

    /// <summary>
    /// Translates the status line a device sends, which is already formatted in
    /// the device's own language (for example "多房间播放中（2 台）").
    /// </summary>
    public string DeviceStatus(string value)
    {
        if (value.Length == 0) return value;
        var match = System.Text.RegularExpressions.Regex.Match(
            value, @"^多房间播放中（(\d+) 台）$");
        if (match.Success)
        {
            return Text("多房间播放中（{0} 台）", match.Groups[1].Value);
        }
        return value;
    }

    public static UiLanguage ParseLanguage(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "en" or "en-us" or "english" => UiLanguage.En,
        "ja" or "jp" or "ja-jp" or "japanese" => UiLanguage.Ja,
        "ko" or "kr" or "ko-kr" or "korean" => UiLanguage.Ko,
        _ => UiLanguage.Zh,
    };

    public static string LanguageCode(UiLanguage language) => language switch
    {
        UiLanguage.En => "en",
        UiLanguage.Ja => "ja",
        UiLanguage.Ko => "ko",
        _ => "zh",
    };

    /// <summary>Chinese source texts of every entry (used by the tests/diagnostics).</summary>
    public IEnumerable<string> Keys => _table.Keys;

    /// <summary>
    /// Literals that live inside list item templates. A template is realised
    /// after the page was translated, so those few are bound in XAML instead
    /// (<c>{x:Bind core:Loc.CardHint}</c>) and re-evaluated when the page is
    /// re-created on a language change.
    /// </summary>
    public static string CardHint => Current["点击卡片开始控制 →"];

    private void Add(string zh, string en, string ja, string ko)
        => _table[zh] = new[] { zh, en, ja, ko };

    public Loc()
    {
        AddIdentity("中文", "English", "日本語", "한국어");
        AddIdentity("BukaMusic", "AirPlay", "—", "0:00", "0", "0.00");

        // ---------------------------------------------------------------- 通用
        Add("BukaMusic 控制台",
            "BukaMusic Console", "BukaMusic コンソール", "BukaMusic 콘솔");
        Add("正在搜索局域网内的设备…",
            "Searching for devices on the local network…",
            "ネットワーク上のデバイスを検索しています…",
            "네트워크에서 기기를 검색하는 중…");
        Add("在线", "Online", "オンライン", "온라인");
        Add("离线", "Offline", "オフライン", "오프라인");
        Add("未在播放", "Not playing", "再生していません", "재생 중 아님");
        Add("本地播放", "Local playback", "ローカル再生", "로컬 재생");
        Add("多房间接收", "Multi-room receiver", "マルチルーム受信", "멀티룸 수신");
        Add("连接中断", "Connection lost", "接続が切断されました", "연결이 끊겼습니다");
        Add("{0} 首", "{0} songs", "{0} 曲", "{0}곡");
        Add("未知", "Unknown", "不明", "알 수 없음");
        // 列表里缺信息时的占位（歌手 / 专辑 / 时长 / 大小）
        Add("暂无", "N/A", "なし", "없음");
        // Placeholders the device sends when a tag is missing.
        Add("未知歌曲", "Unknown title", "不明な曲", "알 수 없는 곡");
        Add("未知歌手", "Unknown artist", "不明なアーティスト", "알 수 없는 아티스트");
        Add("未知专辑", "Unknown album", "不明なアルバム", "알 수 없는 앨범");
        // 输入框提示
        Add("歌曲名", "Song name", "曲名", "곡 이름");
        Add("IP:端口", "IP:port", "IP:ポート", "IP:포트");
        Add("取消", "Cancel", "キャンセル", "취소");
        Add("知道了", "Got it", "OK", "확인");
        Add("确定", "OK", "OK", "확인");
        Add("保存", "Save", "保存", "저장");
        Add("刷新", "Refresh", "更新", "새로 고침");

        // ------------------------------------------------------------ 设备页
        Add("选择设备", "Select a device", "デバイスを選択", "기기 선택");
        Add("同一局域网内的 BukaMusic 设备会自动出现在下方；点击设备卡片即可开始控制。",
            "BukaMusic devices on the same network appear below automatically. Click a card to start controlling it.",
            "同じネットワーク上の BukaMusic デバイスが下に自動表示されます。カードをクリックすると操作を開始できます。",
            "같은 네트워크에 있는 BukaMusic 기기가 아래에 자동으로 표시됩니다. 카드를 클릭하면 제어를 시작할 수 있습니다.");
        Add("手动添加 IP 或 IP:端口",
            "Add an IP or IP:port manually",
            "IP または IP:ポートを手動で追加",
            "IP 또는 IP:포트 직접 추가");
        Add("添加", "Add", "追加", "추가");
        Add("点击卡片开始控制 →",
            "Click a card to start →",
            "カードをクリックして操作開始 →",
            "카드를 클릭해 제어 시작 →");
        Add("还没有发现设备…\n请确认设备与电脑在同一局域网，或手动添加 IP。",
            "No devices found yet…\nMake sure the device and this PC are on the same network, or add an IP manually.",
            "デバイスが見つかりません…\nデバイスと PC が同じネットワークにあるか確認するか、IP を手動で追加してください。",
            "아직 기기를 찾지 못했습니다…\n기기와 PC가 같은 네트워크에 있는지 확인하거나 IP를 직접 추가하세요.");
        Add("打开设备失败", "Could not open the device", "デバイスを開けませんでした", "기기를 열 수 없습니다");
        Add("设备已离线", "Device is offline", "デバイスはオフラインです", "기기가 오프라인입니다");
        Add("设备 {0} ({1}) 已经离线，暂时无法控制。\n请确认手机/盒子上的 BukaMusic 正在运行，且与本机在同一网络。",
            "Device {0} ({1}) is offline and cannot be controlled right now.\nMake sure BukaMusic is running on it and that it is on the same network as this PC.",
            "デバイス {0} ({1}) はオフラインのため操作できません。\nデバイス側で BukaMusic が起動していること、同じネットワークにあることを確認してください。",
            "기기 {0} ({1})이(가) 오프라인이라 제어할 수 없습니다.\n기기에서 BukaMusic이 실행 중이고 이 PC와 같은 네트워크인지 확인하세요.");
        Add("无法连接", "Cannot connect", "接続できません", "연결할 수 없습니다");
        Add("设备 {0} ({1}) 没有响应。\n请确认手机/盒子上的 BukaMusic 正在运行，且与本机在同一网络。",
            "Device {0} ({1}) did not respond.\nMake sure BukaMusic is running on it and that it is on the same network as this PC.",
            "デバイス {0} ({1}) から応答がありません。\nデバイス側で BukaMusic が起動していること、同じネットワークにあることを確認してください。",
            "기기 {0} ({1})이(가) 응답하지 않습니다.\n기기에서 BukaMusic이 실행 중이고 이 PC와 같은 네트워크인지 확인하세요.");
        Add("正在连接 {0} ({1})…",
            "Connecting to {0} ({1})…",
            "{0} ({1}) に接続しています…",
            "{0} ({1})에 연결하는 중…");
        Add("连接 {0} 失败，请确认设备在线",
            "Could not connect to {0}; make sure the device is online",
            "{0} に接続できませんでした。デバイスがオンラインか確認してください",
            "{0} 연결에 실패했습니다. 기기가 온라인인지 확인하세요");
        Add("未发现设备，正在持续搜索…",
            "No devices found, still searching…",
            "デバイスが見つかりません。検索を続けています…",
            "기기를 찾지 못했습니다. 계속 검색 중…");
        Add("发现 {0} 台设备，点击卡片开始控制",
            "Found {0} device(s) - click a card to start",
            "{0} 台のデバイスを検出しました。カードをクリックして操作を開始してください",
            "{0}대의 기기를 찾았습니다. 카드를 클릭해 제어를 시작하세요");
        Add("{0} · {1} 首 · v{2}", "{0} · {1} songs · v{2}", "{0} · {1} 曲 · v{2}", "{0} · {1}곡 · v{2}");

        // ------------------------------------------------------------ 侧栏/导航
        Add("设备", "Device", "デバイス", "기기");
        Add("播放控制", "Playback", "再生コントロール", "재생 제어");
        Add("歌词预览", "Lyrics", "歌詞プレビュー", "가사 미리보기");
        Add("多房间同步", "Multi-room", "マルチルーム同期", "멀티룸 동기화");
        Add("曲库与文件", "Library & files", "ライブラリとファイル", "라이브러리 및 파일");
        Add("设置", "Settings", "設定", "설정");
        Add("运行日志", "Logs", "動作ログ", "실행 로그");
        Add("关于", "About", "このアプリについて", "정보");
        Add("打开网页上传页", "Open web upload page", "Web アップロードページを開く", "웹 업로드 페이지 열기");
        Add("切换设备", "Switch device", "デバイスを切り替え", "기기 전환");
        Add("已连接 {0} ({1})", "Connected to {0} ({1})", "{0} ({1}) に接続しました", "{0} ({1})에 연결됨");
        Add("与 {0} 的连接中断，正在重试…",
            "Lost the connection to {0}, retrying…",
            "{0} との接続が切断されました。再試行中…",
            "{0}와의 연결이 끊겼습니다. 다시 시도 중…");

        // ------------------------------------------------------------ 播放控制
        Add("AirPlay 播放进度由发送端（手机）控制，此处不显示进度条",
            "AirPlay progress is controlled by the sending device (phone), so no progress bar is shown here.",
            "AirPlay の再生位置は送信側（スマートフォン）が制御するため、ここにプログレスバーは表示されません。",
            "AirPlay 재생 위치는 송신 기기(휴대폰)가 제어하므로 여기에 진행 막대를 표시하지 않습니다.");
        Add("重新扫描音乐", "Rescan music", "音楽を再スキャン", "음악 다시 검색");
        // 设备屏幕切换按钮
        Add("歌词界面", "Lyrics screen", "歌詞画面", "가사 화면");
        Add("播放界面", "Playback screen", "再生画面", "재생 화면");
        // 电脑端本地播放
        Add("电脑播放", "This PC", "パソコンで再生", "PC에서 재생");
        Add("左键点击曲库里的歌曲即在此播放",
            "Left-click a song in the library to play it here",
            "ライブラリの曲を左クリックするとここで再生します",
            "라이브러리에서 곡을 왼쪽 클릭하면 여기에서 재생됩니다");
        Add("在 {0} 上播放", "Play on {0}", "{0} で再生", "{0}에서 재생");

        // -------------------------------------------------------------- 曲库页
        Add("搜索标题 / 歌手 / 专辑 / 文件名",
            "Search title / artist / album / file name",
            "タイトル / アーティスト / アルバム / ファイル名で検索",
            "제목 / 아티스트 / 앨범 / 파일 이름 검색");
        Add("默认顺序", "Default order", "既定の順序", "기본 순서");
        Add("按专辑", "By album", "アルバム別", "앨범별");
        Add("按歌手", "By artist", "アーティスト別", "아티스트별");
        Add("上传音乐", "Upload music", "音楽をアップロード", "음악 업로드");
        Add("删除所选", "Delete selected", "選択項目を削除", "선택 항목 삭제");
        Add("删除所选（{0}）", "Delete selected ({0})", "選択項目を削除（{0}）", "선택 항목 삭제 ({0})");
        Add("管理", "Manage", "管理", "관리");
        Add("完成", "Done", "完了", "완료");
        Add("刷新列表", "Refresh list", "リストを更新", "목록 새로 고침");
        Add("← 返回", "← Back", "← 戻る", "← 뒤로");
        Add("标题 / 文件名", "Title / file name", "タイトル / ファイル名", "제목 / 파일 이름");
        Add("歌手", "Artist", "アーティスト", "아티스트");
        Add("专辑", "Album", "アルバム", "앨범");
        Add("时长", "Duration", "長さ", "길이");
        Add("大小", "Size", "サイズ", "크기");
        Add("正在读取曲库…", "Reading the library…", "ライブラリを読み込んでいます…", "라이브러리를 불러오는 중…");
        Add("共 {0} 首", "{0} songs in total", "全 {0} 曲", "총 {0}곡");
        Add("{0} 张专辑 · {1} 首", "{0} albums · {1} songs", "{0} 枚のアルバム · {1} 曲", "앨범 {0}개 · {1}곡");
        Add("{0} 位歌手 · {1} 首", "{0} artists · {1} songs", "{0} 人のアーティスト · {1} 曲", "아티스트 {0}명 · {1}곡");
        Add("按专辑浏览：{0} 张专辑 / {1} 首",
            "Browsing by album: {0} albums / {1} songs",
            "アルバム別表示：{0} 枚 / {1} 曲",
            "앨범별 보기: 앨범 {0}개 / {1}곡");
        Add("按歌手浏览：{0} 位歌手 / {1} 首",
            "Browsing by artist: {0} artists / {1} songs",
            "アーティスト別表示：{0} 人 / {1} 曲",
            "아티스트별 보기: {0}명 / {1}곡");
        Add("删除音乐文件", "Delete music files", "音楽ファイルを削除", "음악 파일 삭제");
        Add("将从设备上删除 {0} 个文件，且无法恢复：\n",
            "{0} files will be deleted from the device. This cannot be undone:\n",
            "デバイスから {0} 個のファイルを削除します。元に戻せません：\n",
            "기기에서 {0}개 파일을 삭제합니다. 되돌릴 수 없습니다:\n");
        Add("\n… 以及另外 {0} 个文件", "\n…and {0} more files", "\n…ほか {0} 件", "\n…그 외 {0}개 파일");
        Add("删除", "Delete", "削除", "삭제");
        Add("正在上传 ({0}/{1})：{2}",
            "Uploading ({0}/{1}): {2}",
            "アップロード中 ({0}/{1})：{2}",
            "업로드 중 ({0}/{1}): {2}");
        Add("上传完成：成功 {0} / {1}",
            "Upload finished: {0} succeeded / {1}",
            "アップロード完了：成功 {0} / {1}",
            "업로드 완료: 성공 {0} / {1}");

        // -------------------------------------------------------------- 歌词页
        Add("重新读取", "Reload", "再読み込み", "다시 불러오기");
        Add("正在读取歌词…", "Loading lyrics…", "歌詞を読み込んでいます…", "가사를 불러오는 중…");
        Add("没有取到歌词", "No lyrics found", "歌詞を取得できませんでした", "가사를 가져오지 못했습니다");
        // Shown by the lyric stage itself when a song has no lyrics.
        Add("暂未找到歌词", "No lyrics found", "歌詞が見つかりません", "가사를 찾을 수 없습니다");
        Add("{0} 行", "{0} lines", "{0} 行", "{0}줄");
        Add(" · 含译文", " · with translation", " · 訳あり", " · 번역 포함");

        // ------------------------------------------------------------ 多房间页
        Add("正在读取…", "Reading…", "読み込み中…", "읽는 중…");
        Add("勾选要同步的设备后点「应用」：本机（主控）播放的本地音乐会同时推送到这些设备。取消勾选即断开该设备，",
            "Tick the devices to sync and click Apply: the music playing on this device (the master) is streamed to them. Unticking a device disconnects it,",
            "同期するデバイスにチェックを入れて「適用」をクリックします。このデバイス（マスター）で再生中の音楽が同時に配信されます。チェックを外すと切断されます。",
            "동기화할 기기를 체크한 뒤 적용을 누르세요. 이 기기(마스터)에서 재생 중인 음악이 함께 전송됩니다. 체크를 해제하면 연결이 끊어집니다,");
        Add("应用", "Apply", "適用", "적용");
        Add("全部断开", "Disconnect all", "すべて切断", "모두 연결 해제");
        Add("断开多房间连接", "Disconnect multi-room", "マルチルーム接続を解除", "멀티룸 연결 해제");
        Add("没有发现可同步的设备", "No syncable devices found", "同期可能なデバイスが見つかりません", "동기화할 수 있는 기기가 없습니다");
        Add("本机正在接收多房间音频",
            "This device is receiving multi-room audio",
            "このデバイスはマルチルーム音声を受信中です",
            "이 기기가 멀티룸 오디오를 수신 중입니다");
        Add("本机正在作为接收端播放（来自 {0}）",
            "This device is playing as a receiver (from {0})",
            "このデバイスは受信側として再生中（{0} から）",
            "이 기기는 수신 기기로 재생 중 ({0}에서)");
        Add("正在断开…", "Disconnecting…", "切断しています…", "연결 해제 중…");
        Add("有 {0} 台设备只报告了 IPv6 地址，勾选后主控仍会尝试连接",
            "{0} device(s) only reported IPv6 addresses; the master will still try to connect",
            "{0} 台のデバイスが IPv6 アドレスのみを通知しています。マスターは接続を試みます",
            "{0}대의 기기가 IPv6 주소만 보고했습니다. 마스터는 계속 연결을 시도합니다");
        Add("可同步", "Syncable", "同期可能", "동기화 가능");
        Add("仅 IPv6 地址", "IPv6 only", "IPv6 のみ", "IPv6만");
        Add("地址未知", "Address unknown", "アドレス不明", "주소 알 수 없음");
        Add("同步中（{0} 台）", "Syncing ({0})", "同期中（{0} 台）", "동기화 중 ({0}대)");
        Add("未开启多房间同步", "Multi-room sync is off", "マルチルーム同期はオフです", "멀티룸 동기화 꺼짐");
        // Status line the device reports while it streams to receivers.
        Add("多房间播放中（{0} 台）",
            "Multi-room playing on {0} device(s)",
            "{0} 台でマルチルーム再生中",
            "{0}대에서 멀티룸 재생 중");

        // -------------------------------------------------------------- 设置页
        Add("设备与曲库", "Device & library", "デバイスとライブラリ", "기기 및 라이브러리");
        Add("设备名称", "Device name", "デバイス名", "기기 이름");
        Add("音乐文件夹", "Music folder", "音楽フォルダ", "음악 폴더");
        Add("音乐文件夹需填写设备上的绝对路径，例如 /storage/emulated/0/Music 或 U 盘挂载路径；修改后设备会自动重新扫描。",
            "Use the absolute path on the device, for example /storage/emulated/0/Music or the mount path of a USB drive. The device rescans automatically after a change.",
            "デバイス上の絶対パスを入力してください（例：/storage/emulated/0/Music、USB ドライブのマウント先）。変更後は自動で再スキャンされます。",
            "기기에서의 절대 경로를 입력하세요(예: /storage/emulated/0/Music 또는 USB 드라이브 마운트 경로). 변경하면 기기가 자동으로 다시 검색합니다.");
        Add("播放", "Playback", "再生", "재생");
        Add("播放方式", "Play mode", "再生モード", "재생 방식");
        Add("顺序播放", "Sequential Play", "順次再生", "순차 재생");
        Add("顺序循环播放", "Folder Loop", "フォルダループ", "폴더 반복");
        Add("随机循环播放", "Shuffle Loop", "シャッフルループ", "셔플 반복");
        Add("单曲循环", "Repeat One", "リピート1曲", "한 곡 반복");
        Add("启动应用时自动播放本地音乐",
            "Play local music automatically on start",
            "起動時にローカル音楽を自動再生",
            "앱 시작 시 로컬 음악 자동 재생");
        Add("左右声道平衡（-1 左 / +1 右）",
            "Channel balance (-1 left / +1 right)",
            "左右チャンネルバランス（-1 左 / +1 右）",
            "좌우 채널 밸런스 (-1 왼쪽 / +1 오른쪽)");
        Add("界面与语言", "Interface & language", "表示と言語", "인터페이스 및 언어");
        Add("背景模糊", "Background blur", "背景ぼかし", "배경 흐림");
        Add("深色", "Dark", "ダーク", "어둡게");
        Add("关闭", "Off", "オフ", "끔");
        Add("开", "On", "オン", "켜기");
        Add("关", "Off", "オフ", "끄기");
        Add("语言", "Language", "言語", "언어");
        Add("播放界面显示应用列表按钮",
            "Show the app list button on the playback screen",
            "再生画面にアプリ一覧ボタンを表示",
            "재생 화면에 앱 목록 버튼 표시");
        Add("在线歌词", "Online lyrics", "オンライン歌詞", "온라인 가사");
        Add("自动在线获取歌词与歌曲信息",
            "Fetch lyrics and song info online automatically",
            "歌詞と曲情報を自動でオンライン取得",
            "가사와 곡 정보를 자동으로 온라인에서 가져오기");
        Add("本地歌词没有词级时间戳、或歌曲缺少标签/封面时，自动从网易云音乐（备用 LRCLIB）补全；仅用于显示，不改动歌曲文件。",
            "When local lyrics have no word timings, or a song is missing tags or cover art, they are completed from NetEase Cloud Music (LRCLIB as fallback). Display only - the song files are never modified.",
            "ローカル歌詞に単語タイミングがない場合や、タグ・カバーが欠けている場合は、NetEase Cloud Music（予備：LRCLIB）から補完します。表示のみで、音楽ファイルは変更しません。",
            "로컬 가사에 단어 타이밍이 없거나 곡에 태그/커버가 없으면 NetEase Cloud Music(LRCLIB 대체)에서 보완합니다. 표시용일 뿐이며 곡 파일은 변경하지 않습니다.");
        Add("均衡器（10 频段，dB）", "Equalizer (10 bands, dB)", "イコライザー（10 バンド、dB）", "이퀄라이저 (10밴드, dB)");
        Add("保存预设", "Save preset", "プリセットを保存", "프리셋 저장");
        Add("加载预设", "Load preset", "プリセットを読み込み", "프리셋 불러오기");
        Add("删除预设", "Delete preset", "プリセットを削除", "프리셋 삭제");
        Add("导出预设", "Export presets", "プリセットを書き出し", "프리셋 내보내기");
        Add("导入预设", "Import presets", "プリセットをインポート", "프리셋 가져오기");
        Add("恢复默认", "Reset to default", "既定に戻す", "기본값 복원");
        Add("保存设置", "Save settings", "設定を保存", "설정 저장");
        Add("读取设置失败，请检查设备连接",
            "Could not read the settings; check the device connection",
            "設定を読み込めませんでした。デバイス接続を確認してください",
            "설정을 읽지 못했습니다. 기기 연결을 확인하세요");
        Add("设备上还没有保存的预设",
            "No presets saved on the device yet",
            "デバイスに保存されたプリセットはまだありません",
            "기기에 저장된 프리셋이 아직 없습니다");
        Add("设备上的 {0} 个预设：", "Presets on the device ({0}):", "デバイス上の {0} 件のプリセット：", "기기의 프리셋 {0}개:");
        Add("预设名称", "Preset name", "プリセット名", "프리셋 이름");
        Add("预设名称不能为空", "The preset name cannot be empty", "プリセット名を入力してください", "프리셋 이름을 입력해야 합니다");
        Add("预设「{0}」已保存", "Preset “{0}” saved", "プリセット「{0}」を保存しました", "프리셋 “{0}” 저장됨");
        Add("预设名称已存在", "That preset name already exists", "同じ名前のプリセットが既に存在します", "같은 이름의 프리셋이 이미 있습니다");
        Add("保存预设失败，请检查连接",
            "Could not save the preset; check the connection",
            "プリセットを保存できませんでした。接続を確認してください",
            "프리셋 저장 실패. 연결을 확인하세요");
        Add("暂无预设", "No presets", "プリセットなし", "프리셋 없음");
        Add("没有选择预设", "No preset selected", "プリセットが選択されていません", "선택된 프리셋이 없습니다");
        Add("已载入预设「{0}」并写入设备",
            "Loaded preset “{0}” and wrote it to the device",
            "プリセット「{0}」を読み込み、デバイスに書き込みました",
            "프리셋 “{0}”를 불러와 기기에 적용했습니다");
        Add("预设「{0}」已删除", "Preset “{0}” deleted", "プリセット「{0}」を削除しました", "프리셋 “{0}” 삭제됨");
        Add("预设已导出到 {0}", "Presets exported to {0}", "プリセットを {0} に書き出しました", "프리셋을 {0}에 내보냈습니다");
        Add("导出失败：", "Export failed: ", "書き出しに失敗：", "내보내기 실패: ");
        Add("预设已导入（{0} 个）", "Imported {0} preset(s)", "プリセットを {0} 件インポートしました", "프리셋 {0}개를 가져왔습니다");
        Add("导入失败：", "Import failed: ", "インポートに失敗：", "가져오기 실패: ");
        Add("确定要恢复默认均衡器设置吗？",
            "Reset the equalizer to its default settings?",
            "イコライザーを既定の設定に戻しますか？",
            "이퀄라이저를 기본 설정으로 되돌릴까요?");
        Add("已恢复默认均衡器", "Equalizer reset to default", "イコライザーを既定に戻しました", "이퀄라이저를 기본값으로 복원했습니다");
        Add("已保存到设备", "Saved to the device", "デバイスに保存しました", "기기에 저장했습니다");
        Add("保存失败，请检查连接", "Save failed; check the connection", "保存に失敗しました。接続を確認してください", "저장 실패. 연결을 확인하세요");
        Add("预设{0}", "Preset {0}", "プリセット{0}", "프리셋{0}");
        Add("预设", "Preset", "プリセット", "프리셋");

        // -------------------------------------------------------------- 关于页
        Add("Android 音乐播放器 / AirPlay 接收 / 多房间同步 的桌面控制端",
            "Desktop console for the Android music player, AirPlay receiver and multi-room sync",
            "Android 音楽プレーヤー / AirPlay 受信 / マルチルーム同期のデスクトップコンソール",
            "Android 음악 플레이어 / AirPlay 수신 / 멀티룸 동기화 데스크톱 콘솔");
        Add("本程序通过局域网与安装了 BukaMusic 的安卓设备通信：UDP 广播自动发现设备，HTTP 接口（/api/*）读取状态、控制播放、上传与删除文件、读写设置。",
            "The console talks to Android devices running BukaMusic over the local network: a UDP broadcast discovers them, and the HTTP API (/api/*) reads the state, controls playback, uploads and deletes files and reads or writes the settings.",
            "このコンソールは LAN 経由で BukaMusic をインストールした Android デバイスと通信します。UDP ブロードキャストで自動検出し、HTTP API（/api/*）で状態取得・再生制御・ファイルのアップロードと削除・設定の読み書きを行います。",
            "이 콘솔은 같은 네트워크에서 BukaMusic이 설치된 Android 기기와 통신합니다. UDP 브로드캐스트로 기기를 찾고, HTTP API(/api/*)로 상태 조회, 재생 제어, 파일 업로드/삭제, 설정 읽기/쓰기를 수행합니다.");
        Add("打开项目主页", "Open project page", "プロジェクトページを開く", "프로젝트 페이지 열기");
        Add("打开设置文件位置", "Open settings folder", "設定ファイルの場所を開く", "설정 파일 위치 열기");
        Add("设备端接口", "Device API", "デバイス側 API", "기기 API");
        Add("发现端口：UDP 47101　·　控制端口：设备上的 8080（若被占用则自动顺延）",
            "Discovery port: UDP 47101　·　Control port: 8080 on the device (falls back to the next free port)",
            "検出ポート：UDP 47101　·　制御ポート：デバイス上の 8080（使用中なら次に空いているポート）",
            "검색 포트: UDP 47101　·　제어 포트: 기기의 8080 (사용 중이면 다음 빈 포트)");
        Add("清空", "Clear", "クリア", "지우기");
        Add("GET /api/info　设备信息（也是 UDP 自动发现的应答内容）\nGET /api/state　播放状态\nGET/POST /api/settings　读取 / 修改设置\nGET /api/library　曲库列表\nPOST /api/control　播放控制（play / pause / next / previous / seek / volume / mode / rescan）\nPOST /api/delete　删除曲库中的文件\nGET /api/cover　当前封面图片\nPOST /upload　上传音乐（网页端与控制台共用）",
            "GET /api/info　device information (also the reply to UDP discovery)\nGET /api/state　playback state\nGET/POST /api/settings　read / change settings\nGET /api/library　library list\nPOST /api/control　playback control (play / pause / next / previous / seek / volume / mode / rescan)\nPOST /api/delete　delete files from the library\nGET /api/cover　current cover image\nPOST /upload　upload music (shared by the web page and this console)",
            "GET /api/info　デバイス情報（UDP 自動検出への応答も兼ねる）\nGET /api/state　再生状態\nGET/POST /api/settings　設定の読み書き\nGET /api/library　ライブラリ一覧\nPOST /api/control　再生コントロール（play / pause / next / previous / seek / volume / mode / rescan）\nPOST /api/delete　ライブラリ内のファイルを削除\nGET /api/cover　現在のカバー画像\nPOST /upload　音楽をアップロード（Web ページと共通）",
            "GET /api/info　기기 정보(UDP 자동 검색 응답 포함)\nGET /api/state　재생 상태\nGET/POST /api/settings　설정 읽기/변경\nGET /api/library　라이브러리 목록\nPOST /api/control　재생 제어(play / pause / next / previous / seek / volume / mode / rescan)\nPOST /api/delete　라이브러리 파일 삭제\nGET /api/cover　현재 커버 이미지\nPOST /upload　음악 업로드(웹 페이지와 공용)");
    }

    /// <summary>Adds an entry whose text is the same in every language.</summary>
    private void AddIdentity(params string[] values)
    {
        foreach (string value in values) _table[value] = new[] { value, value, value, value };
    }

    /// <summary>
    /// Swaps every literal of a page (or of one item container) into the current
    /// language. Elements whose text has no entry are left alone - those are
    /// runtime values such as song titles, not UI copy.
    /// </summary>
    public List<string> Apply(DependencyObject? root)
    {
        var leftovers = new List<string>();
        if (root == null) return leftovers;
        LastTranslatedCount = 0;
        Walk(root, leftovers);
        return leftovers;
    }

    private void Walk(DependencyObject node, List<string> leftovers)
    {
        Translate(node, leftovers);
        // A MarqueeText owns its labels; its inner blocks must not be touched.
        if (node is Controls.MarqueeText) return;
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            Walk(VisualTreeHelper.GetChild(node, i), leftovers);
        }
    }

    /// <summary>
    /// Translates a single element (no recursion). Used for list rows, which are
    /// created long after the page itself was walked.
    /// </summary>
    public void Translate(DependencyObject node, List<string>? leftovers = null)
    {
        List<string>? sink = leftovers;
        switch (node)
        {
            case Controls.MarqueeText marquee:
                marquee.Text = Swap(marquee.Text, sink);
                break;
            case TextBlock text:
                text.Text = Swap(text.Text, sink);
                break;
            case Button button when button.Content is string content:
                button.Content = Swap(content, sink);
                break;
            // ToggleButton/RadioButton derive from ButtonBase, not from Button,
            // so they need their own branch - the sort and blur switches use them.
            case Microsoft.UI.Xaml.Controls.Primitives.ToggleButton toggle
                when toggle.Content is string toggleLabel:
                toggle.Content = Swap(toggleLabel, sink);
                break;
            case RadioButton radio when radio.Content is string radioLabel:
                radio.Content = Swap(radioLabel, sink);
                break;
            case CheckBox check when check.Content is string checkContent:
                check.Content = Swap(checkContent, sink);
                break;
            case HyperlinkButton link when link.Content is string linkContent:
                link.Content = Swap(linkContent, sink);
                break;
            case TextBox box:
                box.PlaceholderText = Swap(box.PlaceholderText, sink);
                if (box.Header is string header) box.Header = Swap(header, sink);
                break;
            case PasswordBox password:
                if (password.Header is string passwordHeader) password.Header = Swap(passwordHeader, sink);
                break;
            case ComboBox combo when combo.PlaceholderText.Length > 0:
                combo.PlaceholderText = Swap(combo.PlaceholderText, sink);
                break;
            case ComboBox combo:
                // The drop-down options are only realised when the list is opened,
                // so they are translated through the items collection instead.
                for (int i = 0; i < combo.Items.Count; i++)
                {
                    if (combo.Items[i] is ComboBoxItem comboItem
                        && comboItem.Content is string itemLabel)
                    {
                        comboItem.Content = Swap(itemLabel, sink);
                    }
                    else if (combo.Items[i] is string itemText)
                    {
                        combo.Items[i] = Swap(itemText, sink);
                    }
                }
                break;
            case ToggleSwitch toggle:
                if (toggle.Header is string toggleHeader) toggle.Header = Swap(toggleHeader, sink);
                if (toggle.OnContent is string on) toggle.OnContent = Swap(on, sink);
                if (toggle.OffContent is string off) toggle.OffContent = Swap(off, sink);
                break;
            case Slider slider when slider.Header is string sliderHeader:
                slider.Header = Swap(sliderHeader, sink);
                break;
            case ToolTip tooltip when tooltip.Content is string tip:
                tooltip.Content = Swap(tip, sink);
                break;
            // Anything else that shows a plain string (combo box items, custom
            // content controls …). Ordered last so the specific cases win.
            case ContentControl generic when generic.Content is string genericText:
                generic.Content = Swap(genericText, sink);
                break;
        }
    }

    /// <summary>Translates one literal, remembering anything that stayed Chinese.</summary>
    private string Swap(string value, List<string>? leftovers)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (_table.TryGetValue(value, out string[]? values))
        {
            LastTranslatedCount++;
            return values[(int)Language];
        }
        // Only English and Korean can be checked this way: Japanese shares the
        // Han characters with Chinese, so a Japanese string would look untranslated.
        if (leftovers != null && (Language == UiLanguage.En || Language == UiLanguage.Ko)
            && HasChinese(value) && !leftovers.Contains(value))
        {
            leftovers.Add(value);
        }
        return value;
    }

    private static bool HasChinese(string value)
    {
        foreach (char c in value)
        {
            if (c >= 0x4E00 && c <= 0x9FFF) return true;
        }
        return false;
    }
}
