using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace com.homemade.utils.asset_overwriter.editor
{
    public class AssetOverwriter : AssetPostprocessor
    {
        private class FilePath
        {
            public string Path;
            public string FileName;

            public FilePath(string path)
            {
                Path = path;
                FileName = System.IO.Path.GetFileName(Path);
            }
        }

        private class ExistAsset
        {
            public FilePath Source;
            public FilePath Imported;

            public ExistAsset(FilePath source, FilePath imported)
            {
                Source = source;
                Imported = imported;
            }
        }

        private const string SourceExistFormat =
            "The contents of the file {0} and '{1}' are exactly the same, so the replacement tool will stop working.\\nDo you really want to import?";

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            var count = importedAssets.Length;
            if (count == 0 || Event.current == null || Event.current.type != EventType.DragPerform)
            {
                return;
            }

            var dragAndDropPaths = new List<string>(DragAndDrop.paths);
            for (var i = 0; i < dragAndDropPaths.Count;)
            {
                if (dragAndDropPaths[i].EndsWith(".meta"))
                {
                    dragAndDropPaths.RemoveAt(i);
                    continue;
                }

                ++i;
            }

            if (count != dragAndDropPaths.Count)
            {
                return;
            }

            var sourcePaths = new List<FilePath>(count);
            for (var i = 0; i < count; ++i)
            {
                if (dragAndDropPaths[i].EndsWith(".prefab"))
                {
                    continue;
                }

                sourcePaths.Add(new FilePath(dragAndDropPaths[i]));
            }

            var importedPaths = new List<FilePath>(count);
            for (var i = 0; i < count; ++i)
            {
                if (importedAssets[i].EndsWith(".prefab"))
                {
                    continue;
                }

                importedPaths.Add(new FilePath(importedAssets[i]));
            }

            var matchCnt = 0;
            for (; matchCnt < count; ++matchCnt)
            {
                var source = sourcePaths[matchCnt].FileName;
                var j = 0;
                for (; j < count; ++j)
                {
                    if (source.Contains(importedPaths[j].FileName))
                    {
                        break;
                    }
                }

                if (j == count)
                {
                    break;
                }
            }

            if (matchCnt == count)
            {
                return;
            }

            var isExecutable = true;
            var isDeleteImportedAssets = false;
            for (var i = 0; i < count; i++)
            {
                for (var j = i + 1; j < count; j++)
                {
                    var path1 = sourcePaths[i];
                    var path2 = sourcePaths[j];
                    if (FileCompare(path1.Path, path2.Path))
                    {
                        var message = string.Format(SourceExistFormat, path1.FileName, path2.FileName);
                        isDeleteImportedAssets = !EditorUtility.DisplayDialog("Confirmation", message, "Import", "Cancel");
                        isExecutable = false;
                        break;
                    }
                }

                if (!isExecutable)
                {
                    break;
                }
            }

            if (!isExecutable)
            {
                if (isDeleteImportedAssets)
                {
                    for (var i = 0; i < count; i++)
                    {
                        AssetDatabase.DeleteAsset(importedAssets[i]);
                    }
                }

                return;
            }

            for (var i = 0; i < sourcePaths.Count;)
            {
                var isRemoved = false;
                var source = sourcePaths[i];
                for (var j = 0; j < importedPaths.Count; j++)
                {
                    var imported = importedPaths[j];
                    if (source.FileName != imported.FileName)
                    {
                        continue;
                    }

                    if (!FileCompare(source.Path, imported.Path))
                    {
                        for (var k = 0; k < importedPaths.Count; k++)
                        {
                            if (j == k)
                            {
                                continue;
                            }

                            if (FileCompare(source.Path, importedPaths[k].Path))
                            {
                                var tempPath = imported.Path + "_temp";
                                FileUtil.CopyFileOrDirectory(imported.Path, tempPath);
                                FileUtil.ReplaceFile(importedPaths[k].Path, imported.Path);
                                FileUtil.ReplaceFile(tempPath, importedPaths[k].Path);
                                FileUtil.DeleteFileOrDirectory(tempPath);
                                AssetDatabase.ImportAsset(imported.Path);
                                AssetDatabase.ImportAsset(importedPaths[k].Path);
                                break;
                            }
                        }
                    }

                    sourcePaths.RemoveAt(i);
                    importedPaths.RemoveAt(j);
                    isRemoved = true;
                    break;
                }

                if (!isRemoved)
                {
                    ++i;
                }
            }

            var existAssets = new List<ExistAsset>(sourcePaths.Count);
            for (var i = 0; i < sourcePaths.Count; i++)
            {
                var source = sourcePaths[i];
                for (var j = 0; j < importedPaths.Count; ++j)
                {
                    var imported = importedPaths[j];
                    if (!FileCompare(source.Path, imported.Path))
                    {
                        continue;
                    }

                    existAssets.Add(new ExistAsset(source, imported));
                    importedPaths.RemoveAt(j);
                    break;
                }
            }

            existAssets.Sort((a, b) => string.Compare(a.Source.Path, b.Source.Path, StringComparison.Ordinal));

            var isFirst = true;
            var isSameAction = false;
            var result = 0;

            foreach (var exist in existAssets)
            {
                var importedPath = exist.Imported.Path;
                var importedAssetDirectory = Path.GetDirectoryName(importedPath);
                var existingAssetPath = $"{importedAssetDirectory}/{exist.Source.FileName}";

                if (!isSameAction)
                {
                    result = EditorUtility.DisplayDialogComplex(
                        existingAssetPath.Replace('\\', '/'),
                        "An asset with the same name already exists. Do you want to replace the asset?",
                        "Replace",
                        "Cancel",
                        "Keep both");
                }

                if (result == 0)
                {
                    FileUtil.ReplaceFile(importedPath, existingAssetPath);
                    AssetDatabase.DeleteAsset(importedPath);
                    AssetDatabase.ImportAsset(existingAssetPath);
                }
                else if (result == 1)
                {
                    AssetDatabase.DeleteAsset(importedPath);
                }

                if (isFirst)
                {
                    if (existAssets.Count > 2)
                    {
                        isSameAction = EditorUtility.DisplayDialog(
                            "Confirmation",
                            "Do you want to apply the same operation to all subsequent ones?",
                            "Yes",
                            "No");
                    }

                    isFirst = false;
                }
            }
        }

        private static bool FileCompare(string file1, string file2)
        {
            if (file1 == file2)
            {
                return true;
            }

            var fs1 = new FileStream(file1, FileMode.Open);
            var fs2 = new FileStream(file2, FileMode.Open);
            int byte1;
            int byte2;
            var ret = false;

            try
            {
                if (fs1.Length == fs2.Length)
                {
                    do
                    {
                        byte1 = fs1.ReadByte();
                        byte2 = fs2.ReadByte();
                    } while ((byte1 == byte2) && (byte1 != -1));

                    if (byte1 == byte2)
                    {
                        ret = true;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError(e);
                return false;
            }
            finally
            {
                fs1.Close();
                fs2.Close();
            }

            return ret;
        }
    }
}