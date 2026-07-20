using Microsoft.Maui.Controls;
using SkiaSharp.Views.Maui;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using SkiaSharp.Views.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using System.Text.Json;
using Flashnote.Models;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Windows.System;
using Microsoft.Extensions.Logging;
using SQLite;
using Flashnote_MAUI.Services;
using System.Text.Json.Nodes;

namespace Flashnote.Services
{
    public partial class CardManager
    {
        private void LoadMetadataDefaultMaterial()
        {
            try
            {
                var metaPath = Path.Combine(_tempExtractPath, "metadata.json");
                if (!File.Exists(metaPath)) return;

                var json = File.ReadAllText(metaPath);
                var node = JsonNode.Parse(json);
                if (node == null) return;

                var dmNode = node["defaultMaterial"];
                if (dmNode != null)
                {
                    _defaultMaterial = dmNode.Deserialize<Flashnote.Models.DefaultMaterial>();
                    Debug.WriteLine($"metadata.defaultMaterial 読込: isPDF={_defaultMaterial?.isPDF}, fileName={_defaultMaterial?.fileName}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"metadata読み込みエラー: {ex.Message}");
            }
        }

        // metadata.json を保存（defaultMaterialを含める）
        private async Task SaveMetadataToFile()
        {
            try
            {
                var metaPath = Path.Combine(_tempExtractPath, "metadata.json");
                JsonNode root;

                if (File.Exists(metaPath))
                {
                    var existing = await File.ReadAllTextAsync(metaPath);
                    root = JsonNode.Parse(existing) ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                }

                if (_defaultMaterial != null)
                {
                    root["defaultMaterial"] = JsonSerializer.SerializeToNode(_defaultMaterial, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                }
                else
                {
                    // remove if null
                    if (root is JsonObject jo && jo.ContainsKey("defaultMaterial"))
                    {
                        jo.Remove("defaultMaterial");
                    }
                }

                var outJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(metaPath, outJson);

                Debug.WriteLine($"metadata.json を保存しました: {metaPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"metadata保存エラー: {ex.Message}");
            }
        }
        /// <summary>
        /// metadata.defaultMaterial を設定して保存する
        /// </summary>
        public async Task SetDefaultMaterial(Flashnote.Models.DefaultMaterial material)
        {
            _defaultMaterial = material;
            await SaveMetadataToFile();
        }
    }
}
