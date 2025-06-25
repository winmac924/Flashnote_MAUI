using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Flashnote.Models;
using System.Text.RegularExpressions;

namespace Flashnote.Services
{
    public class AnkiExporter
    {
        private readonly string _tempExtractPath;
        private readonly List<CardData> _cards;
        private readonly string _deckName;

        public AnkiExporter(string tempExtractPath, List<CardData> cards, string deckName)
        {
            _tempExtractPath = tempExtractPath;
            _cards = cards;
            _deckName = deckName;
        }

        public async Task GenerateApkg(string outputPath)
        {
            string tempAnki2Path = Path.Combine(Path.GetTempPath(), "temp.anki2");
            string tempMediaPath = Path.Combine(Path.GetTempPath(), "media");

            try
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                await CreateCollection(tempAnki2Path);
                var mediaFiles = await PrepareMediaFiles(tempMediaPath);

                using (var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(tempAnki2Path, "collection.anki2");

                    var mediaDict = new Dictionary<string, MediaEntry>();
                    int mediaIndex = 0;
                    foreach (var mediaFile in mediaFiles)
                    {
                        string mediaPath = Path.Combine(tempMediaPath, mediaFile);
                        if (File.Exists(mediaPath))
                        {
                            string entryName = mediaIndex.ToString();
                            zip.CreateEntryFromFile(mediaPath, entryName);
                            
                            var fileInfo = new FileInfo(mediaPath);
                            var sha1 = await CalculateSHA1(mediaPath);
                            
                            mediaDict[entryName] = new MediaEntry
                            {
                                Name = mediaFile,
                                Size = (uint)fileInfo.Length,
                                Sha1 = sha1
                            };
                            mediaIndex++;
                        }
                    }

                    var mediaJson = JsonSerializer.Serialize(mediaDict);
                    var mediaEntry = zip.CreateEntry("media");
                    using (var writer = new StreamWriter(mediaEntry.Open()))
                    {
                        await writer.WriteAsync(mediaJson);
                    }
                }
            }
            catch (Exception ex)
            {
                if (File.Exists(outputPath))
                {
                    try
                    {
                        File.Delete(outputPath);
                    }
                    catch { }
                }
                throw;
            }
            finally
            {
                if (File.Exists(tempAnki2Path))
                    File.Delete(tempAnki2Path);
                if (Directory.Exists(tempMediaPath))
                    Directory.Delete(tempMediaPath, true);
            }
        }

        private async Task<byte[]> CalculateSHA1(string filePath)
        {
            using (var sha1 = SHA1.Create())
            using (var stream = File.OpenRead(filePath))
            {
                return await Task.Run(() => sha1.ComputeHash(stream));
            }
        }

        private async Task CreateCollection(string outputPath)
        {
            using (var db = new SQLite.SQLiteConnection(outputPath))
            {
                // テーブルの作成
                db.Execute("CREATE TABLE IF NOT EXISTS col (id integer PRIMARY KEY, crt integer NOT NULL, mod integer NOT NULL, scm integer NOT NULL, ver integer NOT NULL, dty integer NOT NULL, usn integer NOT NULL, ls integer NOT NULL, conf text NOT NULL, models text NOT NULL, decks text NOT NULL, dconf text NOT NULL, tags text NOT NULL)");
                db.Execute("CREATE TABLE IF NOT EXISTS notes (id integer PRIMARY KEY, guid text NOT NULL, mid integer NOT NULL, mod integer NOT NULL, usn integer NOT NULL, tags text NOT NULL, flds text NOT NULL, sfld integer NOT NULL, csum integer NOT NULL, flags integer NOT NULL, data text NOT NULL)");
                db.Execute("CREATE TABLE IF NOT EXISTS cards (id integer PRIMARY KEY, nid integer NOT NULL, did integer NOT NULL, ord integer NOT NULL, mod integer NOT NULL, usn integer NOT NULL, type integer NOT NULL, queue integer NOT NULL, due integer NOT NULL, ivl integer NOT NULL, factor integer NOT NULL, reps integer NOT NULL, lapses integer NOT NULL, left integer NOT NULL, odue integer NOT NULL, odid integer NOT NULL, flags integer NOT NULL, data text NOT NULL)");
                db.Execute("CREATE TABLE IF NOT EXISTS graves (usn integer NOT NULL, oid integer NOT NULL, type integer NOT NULL)");
                db.Execute("CREATE TABLE IF NOT EXISTS revlog (id integer PRIMARY KEY, cid integer NOT NULL, usn integer NOT NULL, ease integer NOT NULL, ivl integer NOT NULL, lastIvl integer NOT NULL, factor integer NOT NULL, time integer NOT NULL, type integer NOT NULL)");

                // インデックスの作成
                db.Execute("CREATE INDEX IF NOT EXISTS ix_cards_nid ON cards (nid)");
                db.Execute("CREATE INDEX IF NOT EXISTS ix_cards_sched ON cards (did, queue, due)");
                db.Execute("CREATE INDEX IF NOT EXISTS ix_cards_usn ON cards (usn)");
                db.Execute("CREATE INDEX IF NOT EXISTS ix_notes_csum ON notes (csum)");
                db.Execute("CREATE INDEX IF NOT EXISTS ix_notes_usn ON notes (usn)");
                db.Execute("CREATE INDEX IF NOT EXISTS ix_revlog_cid ON revlog (cid)");
                db.Execute("CREATE INDEX IF NOT EXISTS ix_revlog_usn ON revlog (usn)");

                // デフォルトのcolレコードを作成
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var defaultConfig = new
                {
                    @new = new
                    {
                        bury = true,
                        delays = new[] { 1.0, 10.0 },
                        initialFactor = 2500,
                        ints = new[] { 1, 10 },
                        order = 1,
                        perDay = 20,
                        separate = true
                    },
                    rev = new
                    {
                        bury = true,
                        ease4 = 1.3,
                        fuzz = 0.05,
                        ivlFct = 1.0,
                        maxIvl = 36500,
                        minSpace = 1,
                        perDay = 100
                    },
                    lapse = new
                    {
                        delays = new[] { 10.0 },
                        leechAction = 0,
                        leechFails = 8,
                        minInt = 1,
                        mult = 0
                    },
                    timer = 0,
                    maxTaken = 60,
                    autoplay = true,
                    replayq = true,
                    replayv = true
                };
                var defaultModels = new Dictionary<string, object>
                {
                    ["1"] = new
                    {
                        id = 1,
                        name = "Basic",
                        type = 0,
                        css = ".card { font-family: arial; font-size: 20px; text-align: center; color: black; background-color: white; }",
                        flds = new[]
                        {
                            new { name = "Front", ord = 0, sticky = false, rtl = false, font = "Arial", size = 12 },
                            new { name = "Back", ord = 1, sticky = false, rtl = false, font = "Arial", size = 12 }
                        },
                        tmpls = new[]
                        {
                            new
                            {
                                name = "Card 1",
                                ord = 0,
                                qfmt = "{{Front}}",
                                afmt = "{{Front}}<hr>{{Back}}",
                                bqfmt = "",
                                bafmt = ""
                            }
                        },
                        sortf = 0,
                        did = 1,
                        usn = -1,
                        mod = now,
                        vers = new object[] { },
                        tags = new object[] { },
                        req = new[] { new { ord = 0, type = "any", cards = new[] { 0 } } }
                    },
                    ["2"] = new
                    {
                        id = 2,
                        name = "Cloze",
                        type = 1,
                        css = ".card { font-family: arial; font-size: 20px; text-align: left; color: black; background-color: white; }",
                        flds = new[]
                        {
                            new { name = "Text", ord = 0, sticky = false, rtl = false, font = "Arial", size = 12 },
                            new { name = "Back Extra", ord = 1, sticky = false, rtl = false, font = "Arial", size = 12 }
                        },
                        tmpls = new[]
                        {
                            new
                            {
                                name = "Cloze",
                                ord = 0,
                                qfmt = "{{cloze:Text}}",
                                afmt = "{{cloze:Text}}<br>{{Back Extra}}",
                                bqfmt = "",
                                bafmt = ""
                            }
                        },
                        sortf = 0,
                        did = 1,
                        usn = -1,
                        mod = now,
                        vers = new object[] { },
                        tags = new object[] { },
                        req = new[] { new { ord = 0, type = "cloze", cards = new[] { 0 } } }
                    }
                };
                var deckId = Math.Abs(_deckName.GetHashCode());
                var defaultDecks = new Dictionary<string, object>
                {
                    [$"{deckId}"] = new
                    {
                        id = deckId,
                        name = _deckName,
                        desc = "",
                        usn = -1,
                        mod = now,
                        lrnToday = new[] { 0, 0 },
                        revToday = new[] { 0, 0 },
                        newToday = new[] { 0, 0 },
                        timeToday = new[] { 0, 0 },
                        dyn = 0,
                        collapsed = false,
                        browserCollapsed = false,
                        extendNew = 10,
                        extendRev = 50,
                        conf = 1,
                        reviewLimit = 0,
                        newLimit = 0,
                        reviewLimitToday = 0,
                        newLimitToday = 0
                    }
                };

                var defaultDconf = new Dictionary<string, object>
                {
                    ["1"] = new
                    {
                        id = 1,
                        name = "Default",
                        replayq = true,
                        lapse = new
                        {
                            leechFails = 8,
                            minInt = 1,
                            delays = new[] { 10.0 },
                            leechAction = 0,
                            mult = 0
                        },
                        rev = new
                        {
                            perDay = 100,
                            ivlFct = 1.0,
                            maxIvl = 36500,
                            ease4 = 1.3,
                            bury = true,
                            minSpace = 1,
                            fuzz = 0.05
                        },
                        timer = 0,
                        maxTaken = 60,
                        usn = -1,
                        mod = now,
                        @new = new
                        {
                            perDay = 20,
                            delays = new[] { 1.0, 10.0 },
                            separate = true,
                            ints = new[] { 1, 10 },
                            initialFactor = 2500,
                            bury = true,
                            order = 1
                        },
                        autoplay = true
                    }
                };

                db.Execute(@"
                    INSERT OR REPLACE INTO col (id, crt, mod, scm, ver, dty, usn, ls, conf, models, decks, dconf, tags)
                    VALUES (1, ?, ?, ?, 11, 0, -1, 0, ?, ?, ?, ?, ?)
                ",
                    now,
                    now,
                    now,
                    JsonSerializer.Serialize(defaultConfig),
                    JsonSerializer.Serialize(defaultModels),
                    JsonSerializer.Serialize(defaultDecks),
                    JsonSerializer.Serialize(defaultDconf),
                    JsonSerializer.Serialize(new { })
                );

                // カードの追加
                foreach (var cardData in _cards)
                {
                    var noteId = Math.Abs(Guid.NewGuid().GetHashCode());
                    var noteGuid = Guid.NewGuid().ToString();
                    var noteMod = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    // Cloze判定: <<blank|...>>または{{c1::...}}がfront/backに含まれているか
                    bool isCloze = (cardData.front?.Contains("<<blank|") == true) || (cardData.back?.Contains("<<blank|") == true);
                    var front = ReplaceBlanks(cardData.front ?? "");
                    var back = string.IsNullOrEmpty(cardData.back) ? " " : ReplaceBlanks(cardData.back);
                    if (!isCloze)
                    {
                        // 変換後の{{c1::もチェック
                        isCloze = front.Contains("{{c1::") || back.Contains("{{c1::");
                    }
                    int modelId = isCloze ? 2 : 1;
                    string noteFields = $"{front}\x1f{back}";
                    var noteCsum = CalculateChecksum(front);

                    db.Execute(@"
                        INSERT INTO notes (id, guid, mid, mod, usn, tags, flds, sfld, csum, flags, data)
                        VALUES (?, ?, ?, ?, -1, '', ?, ?, ?, 0, '{}')
                    ", noteId, noteGuid, modelId, noteMod, noteFields, front, noteCsum);

                    if (isCloze)
                    {
                        // Clozeカードの場合、穴埋めの数だけカードを生成
                        var clozeMatches = Regex.Matches(front + back, @"{{c(\d+)::");
                        var clozeCount = clozeMatches.Count;
                        for (int i = 0; i < clozeCount; i++)
                        {
                            var cardId = Math.Abs(Guid.NewGuid().GetHashCode());
                            var cardMod = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                            db.Execute(@"
                                INSERT INTO cards (id, nid, did, ord, mod, usn, type, queue, due, ivl, factor, reps, lapses, left, odue, odid, flags, data)
                                VALUES (?, ?, ?, ?, ?, -1, 0, 0, 0, 0, 2500, 0, 0, 0, 0, 1, 0, '{}')
                            ", cardId, noteId, deckId, i, cardMod);
                        }
                    }
                    else
                    {
                        // 通常のカードの場合、1枚のみ生成
                        var cardId = Math.Abs(Guid.NewGuid().GetHashCode());
                        var cardMod = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                        db.Execute(@"
                            INSERT INTO cards (id, nid, did, ord, mod, usn, type, queue, due, ivl, factor, reps, lapses, left, odue, odid, flags, data)
                            VALUES (?, ?, ?, 0, ?, -1, 0, 0, 0, 0, 2500, 0, 0, 0, 0, 1, 0, '{}')
                        ", cardId, noteId, deckId, cardMod);
                    }
                }
            }
        }

        private int CalculateChecksum(string text)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var inputBytes = System.Text.Encoding.UTF8.GetBytes(text);
                var hashBytes = md5.ComputeHash(inputBytes);
                return BitConverter.ToInt32(hashBytes, 0);
            }
        }

        private string ReplaceBlanks(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            int count = 1;
            return Regex.Replace(
                text,
                @"<<blank\|(.*?)>>",
                m => $"{{{{c{count++}::{m.Groups[1].Value}}}}}"
            );
        }

        private async Task<List<string>> PrepareMediaFiles(string mediaPath)
        {
            var mediaFiles = new List<string>();
            if (!Directory.Exists(mediaPath))
                Directory.CreateDirectory(mediaPath);

            var mediaDir = Path.Combine(_tempExtractPath, "media");
            if (Directory.Exists(mediaDir))
            {
                foreach (var file in Directory.GetFiles(mediaDir))
                {
                    string fileName = Path.GetFileName(file);
                    string destPath = Path.Combine(mediaPath, fileName);
                    File.Copy(file, destPath, true);
                    mediaFiles.Add(fileName);
                }
            }

            return mediaFiles;
        }
    }

    public class AnkiNote
    {
        public int Id { get; set; }
        public string Guid { get; set; }
        public int ModelId { get; set; }
        public long Mod { get; set; }
        public int Usn { get; set; }
        public string Tags { get; set; }
        public string Fields { get; set; }
        public string SortField { get; set; }
        public int Checksum { get; set; }
        public int Flags { get; set; }
        public string Data { get; set; }
    }

    public class AnkiCard
    {
        public int Id { get; set; }
        public int NoteId { get; set; }
        public int DeckId { get; set; }
        public int TemplateIdx { get; set; }
        public int Type { get; set; }
        public int Queue { get; set; }
        public long Due { get; set; }
        public int Interval { get; set; }
        public int Factor { get; set; }
        public int Reviews { get; set; }
        public int Lapses { get; set; }
        public int Left { get; set; }
        public long ODue { get; set; }
        public int ODeckId { get; set; }
        public int Flags { get; set; }
        public string Data { get; set; }
        public int Usn { get; set; }
        public long Mod { get; set; }
    }

    public class AnkiDeck
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Config { get; set; }
        public int Usn { get; set; }
        public long Mod { get; set; }
    }

    public class AnkiModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Fields { get; set; }
        public string Templates { get; set; }
        public string Css { get; set; }
        public int Type { get; set; }
        public int SortField { get; set; }
        public int Usn { get; set; }
        public long Mod { get; set; }
        public string Data { get; set; }
    }

    public class MediaEntry
    {
        public string Name { get; set; }
        public uint Size { get; set; }
        public byte[] Sha1 { get; set; }
    }
}
