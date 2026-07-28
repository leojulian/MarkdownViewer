using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace MarkdownViewer;

internal sealed class FavoritesManager
{
    private const string FavoriteDirectoryName = ".markdownviewer";
    private string? _favoriteDirectory;
    private string? _favoriteFile;
    private HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastLoadTimeUtc;

    public void SetFolderPath(string? folderPath)
    {
        _favorites.Clear();
        if (string.IsNullOrEmpty(folderPath))
        {
            _favoriteDirectory = null;
            _favoriteFile = null;
            _lastLoadTimeUtc = DateTime.MinValue;
            return;
        }

        _favoriteDirectory = Path.Combine(folderPath, FavoriteDirectoryName);
        _favoriteFile = Path.Combine(_favoriteDirectory, "favorites.json");
        _lastLoadTimeUtc = DateTime.MinValue;
    }

    public void Load()
    {
        if (_favoriteFile == null)
        {
            _favorites.Clear();
            return;
        }

        try
        {
            if (File.Exists(_favoriteFile))
            {
                _favorites = ReadFavorites(_favoriteFile);
                EnsureHiddenDirectory(_favoriteDirectory);
            }
            else
            {
                _favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            _lastLoadTimeUtc = File.Exists(_favoriteFile)
                ? File.GetLastWriteTimeUtc(_favoriteFile)
                : DateTime.MinValue;
        }
        catch
        {
            // 保留当前内存值，避免读取失败后被空列表覆盖。
        }
    }

    public bool Add(string filePath, out string? error) => UpdateFavorites(filePath, add: true, out error);

    public bool Remove(string filePath, out string? error) => UpdateFavorites(filePath, add: false, out error);

    public bool IsFavorite(string filePath)
    {
        ReloadIfChanged();
        return _favorites.Contains(filePath);
    }

    public List<string> GetAll()
    {
        ReloadIfChanged();
        return _favorites.ToList();
    }

    private void ReloadIfChanged()
    {
        if (_favoriteFile == null)
            return;

        if (!File.Exists(_favoriteFile))
        {
            if (_lastLoadTimeUtc != DateTime.MinValue)
            {
                _favorites.Clear();
                _lastLoadTimeUtc = DateTime.MinValue;
            }
            return;
        }

        if (File.GetLastWriteTimeUtc(_favoriteFile) != _lastLoadTimeUtc)
            Load();
    }

    private bool UpdateFavorites(string filePath, bool add, out string? error)
    {
        error = null;
        if (_favoriteFile == null || _favoriteDirectory == null)
        {
            error = "当前未打开文件夹，单文件模式不支持目录收藏。";
            return false;
        }

        using var mutex = new Mutex(false, CreateMutexName(_favoriteFile));
        var lockTaken = false;
        try
        {
            try
            {
                lockTaken = mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                lockTaken = true;
            }

            if (!lockTaken)
            {
                error = "等待其他 MarkdownViewer 实例释放收藏文件超时。";
                return false;
            }

            var latest = ReadFavorites(_favoriteFile);
            if (add)
                latest.Add(filePath);
            else
                latest.Remove(filePath);

            WriteFavoritesAtomically(_favoriteDirectory, _favoriteFile, latest);
            _favorites = latest;
            _lastLoadTimeUtc = File.GetLastWriteTimeUtc(_favoriteFile);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (lockTaken)
                mutex.ReleaseMutex();
        }
    }

    private static HashSet<string> ReadFavorites(string favoriteFile)
    {
        if (!File.Exists(favoriteFile))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var json = File.ReadAllText(favoriteFile, Encoding.UTF8);
        var list = JsonSerializer.Deserialize<List<string>>(json)
            ?? throw new InvalidDataException("favorites.json 内容无效。");
        return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
    }

    private static void WriteFavoritesAtomically(string favoriteDirectory, string favoriteFile,
        HashSet<string> favorites)
    {
        Directory.CreateDirectory(favoriteDirectory);
        EnsureHiddenDirectory(favoriteDirectory);
        var temporaryFile = Path.Combine(favoriteDirectory, $"favorites.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(favorites.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temporaryFile, json, new UTF8Encoding(false));
            File.Move(temporaryFile, favoriteFile, true);
        }
        finally
        {
            if (File.Exists(temporaryFile))
                File.Delete(temporaryFile);
        }
    }

    private static void EnsureHiddenDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory))
            return;

        Directory.CreateDirectory(directory);
        var attributes = File.GetAttributes(directory);
        if ((attributes & FileAttributes.Hidden) == 0)
            File.SetAttributes(directory, attributes | FileAttributes.Hidden);
    }

    private static string CreateMutexName(string favoriteFile)
    {
        var normalizedPath = Path.GetFullPath(favoriteFile).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return $"MarkdownViewer.Favorites.{Convert.ToHexString(hash)}";
    }
}
