using System;

namespace Flashnote.Services.Sync
{
    /// <summary>
    /// Azure Blob Storage上のノート/カード/画像のパス構築を一元管理する。
    /// UUID形式のノート/フォルダ名は、フォルダ階層をBlobの物理パスに反映しない
    /// フラットレイアウト(uid/{id}/...、subFolderを無視)で保存される、という
    /// アプリ全体の規約をここに集約する。
    ///
    /// 以前はこの判定(Guid.TryParseによるフラット判定)が BlobStorageService.cs 内の
    /// 複数メソッド・Edit.xaml.cs・SharingManager.cs に個別実装されており、
    /// 1箇所（SaveNoteAsync）だけ判定が抜けていたためにサブフォルダ内のノートで
    /// cards.txtの保存先と取得先が食い違うバグが発生した。再発防止のため一本化する。
    /// </summary>
    public static class BlobPathResolver
    {
        /// <summary>
        /// noteName（または "{id}/xxx.json" のような複合パスの先頭セグメント）が
        /// UUID形式かどうかを判定する。
        /// </summary>
        public static bool IsFlatUuidLayout(string noteNameOrPath)
        {
            if (string.IsNullOrEmpty(noteNameOrPath)) return false;
            var firstSegment = noteNameOrPath.Split('/')[0];
            return Guid.TryParse(firstSegment, out _);
        }

        private static string GetUserPath(string uid, string subFolder)
            => string.IsNullOrEmpty(subFolder) ? uid : $"{uid}/{subFolder}";

        /// <summary>
        /// ノート本体(cards.txt等)のBlobパスを解決する。
        /// UUID形式なら uid/{noteName}/{fileName}、そうでなければ uid/{subFolder}/{noteName}/{fileName}。
        /// </summary>
        public static string ResolveNoteFilePath(string uid, string noteName, string subFolder, string fileName = "cards.txt")
        {
            if (IsFlatUuidLayout(noteName))
            {
                return $"{uid}/{noteName}/{fileName}";
            }
            var userPath = GetUserPath(uid, subFolder);
            return $"{userPath}/{noteName}/{fileName}";
        }

        /// <summary>
        /// noteName自体が "{id}/metadata.json" のような複合パス（拡張子込み）の場合を含めて
        /// 直接ファイルパスを解決する。
        /// </summary>
        public static string ResolveDirectFilePath(string uid, string compoundNameOrPath, string subFolder)
        {
            if (IsFlatUuidLayout(compoundNameOrPath))
            {
                return $"{uid}/{compoundNameOrPath}";
            }
            var userPath = GetUserPath(uid, subFolder);
            return $"{userPath}/{compoundNameOrPath}";
        }

        /// <summary>
        /// ノート配下の img/cards などのサブパス（プレフィックス）を解決する。
        /// UUID形式なら {noteName}/{subPathSegment}（subFolderを無視）、
        /// そうでなければ {subFolder}/{noteName}/{subPathSegment}（subFolderがあれば）。
        /// </summary>
        public static string ResolveNoteSubPath(string noteName, string subFolder, string subPathSegment)
        {
            if (IsFlatUuidLayout(noteName))
            {
                return $"{noteName}/{subPathSegment}";
            }
            return string.IsNullOrEmpty(subFolder) ? $"{noteName}/{subPathSegment}" : $"{subFolder}/{noteName}/{subPathSegment}";
        }
    }
}
