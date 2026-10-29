namespace Ymir.VibeMaker.Application.Runtime;

/// <summary>
/// container 內的背景工作（ADR-0034）：image 的 <c>ymir-runtime-init</c> 在 container 每次啟動時恢復
/// <c>/workspace/.ymir/autostart/*.sh</c> 與 <c>/workspace/.ymir/crontab</c>；背景程序以 <c>ymir-env</c> 讀取平台寫入 tmpfs 的模型 key。
/// </summary>
public static class BackgroundWork
{
    /// <summary>container tmpfs 內的模型 key 檔；container 停止即消失，不在 workspace 或 host 磁碟。</summary>
    public const string KeyDirectory = "/tmp/.ymir";

    public const string KeyFile = KeyDirectory + "/model-key";

    /// <summary>
    /// 從 stdin 讀 key 並原子寫入（umask 077）；key 不出現在程序參數（ADR-0004、ADR-0034）。
    /// 已存在的 symlink 一律先移除，不寫到別處。stdin 為空時刪除 key 檔（管理員關閉背景模型存取）。
    /// </summary>
    public const string WriteKeyScript =
        """
        umask 077
        d=/tmp/.ymir
        [ ! -L "$d" ] || rm -f "$d"
        mkdir -p "$d" || exit 3
        t="$d/.model-key.$$"
        cat > "$t" || { rm -f "$t"; exit 4; }
        if [ -s "$t" ]; then mv -f "$t" "$d/model-key"; else rm -f "$t" "$d/model-key"; fi
        """;

    /// <summary>給個人對話的 Agent：怎麼登記會自動恢復的背景工作、怎麼在背景呼叫模型。</summary>
    public const string PersonalPrompt =
        """
        背景工作（這個執行環境會常駐，重新啟動後自動恢復）：
        - 需要一直執行的程式：寫成 /workspace/.ymir/autostart/<名稱>.sh（名稱只用英數、. _ -），平台約 30 秒內啟動，結束後自動重啟；刪除檔案即停止。
        - 定時工作：寫在 /workspace/.ymir/crontab（標準 5 欄 cron 格式，時區 UTC），由 supercronic 執行，檔案修改後自動重新載入。
        - 輸出在 /workspace/.ymir/logs/<名稱>.log（crontab 為 crontab.log）。
        - 背景程序需要呼叫模型時，在指令前加 ymir-env，例如 ymir-env pi -p "..."；費用計入使用者的每月預算。不要把 key 寫進檔案或印出來。
        - 只有使用者要求長時間、定時或自動執行的工作時才建立背景工作，並告訴使用者建立了什麼、如何停止。
        """;

    /// <summary>群組的執行環境是成員共用的（ADR-0026）：可以登記背景工作，但背景程序沒有模型 key。</summary>
    public const string GroupPrompt =
        """
        背景工作：這個群組的執行環境會常駐；/workspace/.ymir/autostart/<名稱>.sh 與 /workspace/.ymir/crontab 會在重新啟動後自動恢復，輸出在 /workspace/.ymir/logs/。
        群組的背景程序不能呼叫模型。只有成員明確要求時才建立背景工作，並說明如何停止。
        """;
}
