using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace Aether;

public class LaunchService
{
    private readonly DatabaseService _dbService;

    public LaunchService(DatabaseService dbService)
    {
        _dbService = dbService;
    }

    private void Log(string message)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logPath = Path.Combine(appData, "Aether", "launch_log.txt");
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:o}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public async Task LaunchGameAsync(Game game, DispatcherQueue dispatcherQueue, Action<string>? onStatusChanged, Action<long>? onPlayTimeUpdated)
    {
        Log($"--- Launching game: {game.Title} (Id: {game.Id}) ---");
        var fullExePath = Path.GetFullPath(Path.Combine(game.Folder, game.ExePath));
        Log($"Full EXE path: {fullExePath}");
        Log($"Working directory: {game.Folder}");

        if (!File.Exists(fullExePath))
        {
            Log($"ERROR: Executable not found: {fullExePath}");
            throw new FileNotFoundException("The game executable could not be found.", fullExePath);
        }

        var process = new Process();
        process.StartInfo.FileName = fullExePath;
        process.StartInfo.WorkingDirectory = game.Folder;
        process.StartInfo.UseShellExecute = true;

        var startTime = DateTime.UtcNow;
        Log($"Starting process P1...");
        process.Start();

        dispatcherQueue.TryEnqueue(() =>
        {
            game.IsRunning = true;
            onStatusChanged?.Invoke("Running");
        });

        Process trackedProcess = process;
        Log($"Started P1. ID: {process.Id}, Name: {process.ProcessName}");

        DateTime? p1ExitTime = null;

        // Poll for child processes spawned under the game folder
        Log("Entering unified polling loop...");
        for (int pollIndex = 0; pollIndex < 20; pollIndex++)
        {
            var candidates = new List<Process>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == process.Id) continue;
                    var filename = p.MainModule?.FileName;
                    if (filename != null && IsPathUnderFolder(filename, game.Folder))
                    {
                        candidates.Add(p);
                    }
                }
                catch
                {
                    // Ignore access denied
                }
            }

            if (candidates.Count > 0)
            {
                Log($"Poll #{pollIndex}: Found {candidates.Count} folder-scoped candidates. P1 Exited: {process.HasExited}");
            }

            // Pivot tracking if launcher stub has exited
            if (candidates.Count > 0 && process.HasExited)
            {
                var targetExeName = Path.GetFileName(game.ExePath);
                var exactMatches = candidates.Where(p => {
                    try { return string.Equals(Path.GetFileName(p.MainModule?.FileName), targetExeName, StringComparison.OrdinalIgnoreCase); }
                    catch { return false; }
                }).ToList();

                Process? selected = null;
                if (exactMatches.Count > 0)
                {
                    selected = GetMostRecentProcess(exactMatches);
                    Log($"Selected exact exe match: ID {selected?.Id}, Name {selected?.ProcessName}");
                }
                else
                {
                    selected = GetMostRecentProcess(candidates);
                    Log($"Selected most recent process: ID {selected?.Id}, Name {selected?.ProcessName}");
                }

                if (selected != null)
                {
                    trackedProcess = selected;
                    Log($"Pivoted tracking to child process. ID: {trackedProcess.Id}, Name: {trackedProcess.ProcessName}");
                    break;
                }
            }

            // Wait 1.5s grace period for slow-launching games
            if (process.HasExited)
            {
                if (p1ExitTime == null)
                {
                    p1ExitTime = DateTime.UtcNow;
                    Log($"P1 exited. Starting 1.5s grace period...");
                }
                else if ((DateTime.UtcNow - p1ExitTime.Value).TotalSeconds >= 1.5)
                {
                    Log("Grace period expired. No child found. Ending loop.");
                    break;
                }
            }

            await Task.Delay(500);
        }

        Log($"Entering WaitForExit on tracked process (ID: {trackedProcess.Id}, Name: {trackedProcess.ProcessName}, Exited: {trackedProcess.HasExited})");
        await Task.Run(() => {
            try
            {
                trackedProcess.WaitForExit();
            }
            catch (Exception ex)
            {
                Log($"Exception in WaitForExit: {ex.Message}");
            }
        });
        Log("Tracked process exited.");

        // Compute playtime using actual process exit time
        DateTime sessionEndTime;
        try
        {
            sessionEndTime = trackedProcess.ExitTime.ToUniversalTime();
            Log($"Tracked process ExitTime: {sessionEndTime:o}");
        }
        catch (Exception ex)
        {
            Log($"Could not read ExitTime ({ex.Message}), falling back to DateTime.UtcNow");
            sessionEndTime = DateTime.UtcNow;
        }

        var duration = (long)(sessionEndTime - startTime).TotalSeconds;
        if (duration < 0) duration = 0;
        Log($"Session playtime duration: {duration} seconds");

        _dbService.UpdatePlayTime(game.Id, game.PlayTimeSeconds + duration);

        dispatcherQueue.TryEnqueue(() =>
        {
            game.PlayTimeSeconds += duration;
            game.IsRunning = false;
            Log($"Set game.IsRunning to false. Playtime updated in DB to {game.PlayTimeSeconds}s");

            onPlayTimeUpdated?.Invoke(game.PlayTimeSeconds);
            onStatusChanged?.Invoke("Ready");
            Log("--- Launch sequence finished ---");
        });
    }

    public static bool IsPathUnderFolder(string filePath, string folderPath)
    {
        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(folderPath))
            return false;

        string cleanFile = Path.GetFullPath(filePath);
        string cleanFolder = Path.GetFullPath(folderPath);

        // Ensure trailing separator for exact directory boundary matching
        if (!cleanFolder.EndsWith(Path.DirectorySeparatorChar.ToString()) && 
            !cleanFolder.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
        {
            cleanFolder += Path.DirectorySeparatorChar;
        }

        return cleanFile.StartsWith(cleanFolder, StringComparison.OrdinalIgnoreCase);
    }

    private static Process? GetMostRecentProcess(List<Process> processes)
    {
        Process? newest = null;
        DateTime newestTime = DateTime.MinValue;
        foreach (var p in processes)
        {
            try
            {
                var startTime = p.StartTime;
                if (startTime > newestTime)
                {
                    newestTime = startTime;
                    newest = p;
                }
            }
            catch
            {
                if (newest == null) newest = p;
            }
        }
        return newest;
    }
}
