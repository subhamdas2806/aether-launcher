using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Dapper;

namespace GameShelf;

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
                Tags TEXT,
                DateAdded TEXT,
                AccentColorPrimary TEXT,
                AccentColorSecondary TEXT
            );
            CREATE TABLE IF NOT EXISTS PlaySessions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GameId INTEGER NOT NULL,
                StartTime TEXT NOT NULL,
                DurationSeconds INTEGER NOT NULL,
                FOREIGN KEY(GameId) REFERENCES Games(Id) ON DELETE CASCADE
            );
        ");

        try
        {
            connection.Execute("ALTER TABLE Games ADD COLUMN DateAdded TEXT;");
        }
        catch
        {
            // Column already exists or table is new
        }

        try
        {
            connection.Execute("ALTER TABLE Games ADD COLUMN AccentColorPrimary TEXT;");
        }
        catch
        {
            // Column already exists
        }

        try
        {
            connection.Execute("ALTER TABLE Games ADD COLUMN AccentColorSecondary TEXT;");
        }
        catch
        {
            // Column already exists
        }
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
        game.DateAdded = DateTime.UtcNow.ToString("o");
        var sql = @"
            INSERT INTO Games (Title, Folder, ExePath, CoverPath, PlayTimeSeconds, Tags, DateAdded, AccentColorPrimary, AccentColorSecondary)
            VALUES (@Title, @Folder, @ExePath, @CoverPath, @PlayTimeSeconds, @Tags, @DateAdded, @AccentColorPrimary, @AccentColorSecondary);
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
                Tags = @Tags,
                DateAdded = @DateAdded,
                AccentColorPrimary = @AccentColorPrimary,
                AccentColorSecondary = @AccentColorSecondary
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

    public void UpdateGameColors(int id, string primaryHex, string secondaryHex)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute(
            "UPDATE Games SET AccentColorPrimary = @Primary, AccentColorSecondary = @Secondary WHERE Id = @Id;",
            new { Primary = primaryHex, Secondary = secondaryHex, Id = id });
    }

    public void InsertPlaySession(int gameId, DateTime startTime, int durationSeconds)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute(@"
            INSERT INTO PlaySessions (GameId, StartTime, DurationSeconds)
            VALUES (@GameId, @StartTime, @DurationSeconds);",
            new { GameId = gameId, StartTime = startTime.ToString("o"), DurationSeconds = durationSeconds });
    }

    public DateTime? GetLastPlayed(int gameId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var startTimeStr = connection.QueryFirstOrDefault<string>(
            "SELECT StartTime FROM PlaySessions WHERE GameId = @GameId ORDER BY StartTime DESC LIMIT 1",
            new { GameId = gameId });
            
        if (startTimeStr != null && DateTime.TryParse(startTimeStr, out var dt))
        {
            return dt;
        }
        return null;
    }

    public int GetRecentPlayTimeSeconds(int gameId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var twoWeeksAgo = DateTime.UtcNow.AddDays(-14).ToString("o");
        return connection.ExecuteScalar<int>(
            "SELECT COALESCE(SUM(DurationSeconds), 0) FROM PlaySessions WHERE GameId = @GameId AND StartTime >= @TwoWeeksAgo",
            new { GameId = gameId, TwoWeeksAgo = twoWeeksAgo });
    }
}

public class PlaySession
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public string StartTime { get; set; } = "";
    public int DurationSeconds { get; set; }
}
