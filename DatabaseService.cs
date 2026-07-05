using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Dapper;

namespace Aether;

public class DatabaseService
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public DatabaseService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "Aether");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        
        var coversDir = Path.Combine(dir, "Covers");
        if (!Directory.Exists(coversDir))
        {
            Directory.CreateDirectory(coversDir);
        }

        _dbPath = Path.Combine(dir, "aether.db");
        _connectionString = $"Data Source={_dbPath}";
        
        InitializeDatabase();
    }

    public string CoversDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether", "Covers");

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute(@"
            CREATE TABLE IF NOT EXISTS Games (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Folder TEXT NOT NULL,
                ExePath TEXT NOT NULL,
                CoverPath TEXT,
                PlayTimeSeconds INTEGER NOT NULL DEFAULT 0,
                Tags TEXT
            );
        ");
    }

    public List<Game> GetAllGames()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection.Query<Game>("SELECT * FROM Games").ToList();
    }

    public void InsertGame(Game game)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var sql = @"
            INSERT INTO Games (Title, Folder, ExePath, CoverPath, PlayTimeSeconds, Tags)
            VALUES (@Title, @Folder, @ExePath, @CoverPath, @PlayTimeSeconds, @Tags);
            SELECT last_insert_rowid();";
        var id = connection.QuerySingle<int>(sql, game);
        game.Id = id;
    }

    public void UpdateGame(Game game)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var sql = @"
            UPDATE Games
            SET Title = @Title,
                Folder = @Folder,
                ExePath = @ExePath,
                CoverPath = @CoverPath,
                PlayTimeSeconds = @PlayTimeSeconds,
                Tags = @Tags
            WHERE Id = @Id;";
        connection.Execute(sql, game);
    }

    public void UpdatePlayTime(int id, long seconds)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute("UPDATE Games SET PlayTimeSeconds = @Seconds WHERE Id = @Id;", new { Seconds = seconds, Id = id });
    }

    public void DeleteGame(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute("DELETE FROM Games WHERE Id = @Id;", new { Id = id });
    }
}
