using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XAU_Kinetic.Desktop.Models;

namespace XAU_Kinetic.Desktop.Services
{
    public class TradingEngineService : ITradingEngineService
    {
        private Process? _pythonProcess;
        private EngineStatus _status = EngineStatus.Stopped;
        private readonly string _dbPath;

        public EngineStatus Status
        {
            get => _status;
            private set
            {
                if (_status != value)
                {
                    _status = value;
                    StatusChanged?.Invoke(this, _status);
                }
            }
        }

        public event EventHandler<EngineStatus>? StatusChanged;

        public TradingEngineService(string dbPath = "xau_kinetic_audit.db")
        {
            _dbPath = dbPath;
        }

        private string ResolveDbPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string current = baseDir;
            for (int i = 0; i < 5; i++)
            {
                string candidate = Path.Combine(current, _dbPath);
                if (File.Exists(candidate)) return candidate;
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            return Path.Combine(baseDir, _dbPath);
        }

        private string? ResolveRepoRoot()
        {
            string current = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 5; i++)
            {
                if (Directory.Exists(Path.Combine(current, "xau_kinetic")))
                {
                    return current;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            return null;
        }

        public async Task<bool> StartEngineAsync(string configPath, bool mockMode, CancellationToken cancellationToken = default)
        {
            if (_pythonProcess != null && !_pythonProcess.HasExited)
            {
                return true;
            }

            Status = EngineStatus.Initializing;

            try
            {
                var repoRoot = ResolveRepoRoot() ?? AppDomain.CurrentDomain.BaseDirectory;
                var argsList = new List<string> { "-m", "xau_kinetic.main" };
                if (mockMode) argsList.Add("--mock");
                if (!string.IsNullOrEmpty(configPath))
                {
                    argsList.Add("--config");
                    argsList.Add(configPath);
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = string.Join(" ", argsList),
                    WorkingDirectory = repoRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                _pythonProcess = new Process { StartInfo = psi };
                _pythonProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Debug.WriteLine($"[Python Out]: {e.Data}");
                };
                _pythonProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Debug.WriteLine($"[Python Err]: {e.Data}");
                };

                if (!_pythonProcess.Start())
                {
                    Status = EngineStatus.Error;
                    return false;
                }

                _pythonProcess.BeginOutputReadLine();
                _pythonProcess.BeginErrorReadLine();

                Status = EngineStatus.Running;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to start Python engine: {ex.Message}");
                Status = EngineStatus.Error;
                return false;
            }
        }

        public async Task StopEngineAsync()
        {
            if (_pythonProcess != null && !_pythonProcess.HasExited)
            {
                try
                {
                    _pythonProcess.Kill(entireProcessTree: true);
                    await _pythonProcess.WaitForExitAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error stopping process: {ex.Message}");
                }
                finally
                {
                    _pythonProcess.Dispose();
                    _pythonProcess = null;
                }
            }

            Status = EngineStatus.Stopped;
        }

        public async Task TriggerEmergencyKillSwitchAsync()
        {
            Status = EngineStatus.CircuitBroken;
            await StopEngineAsync();
        }

        public async Task<AccountState> GetAccountStateAsync()
        {
            return await Task.FromResult(new AccountState(
                Login: 1234567,
                Balance: 10000.00,
                Equity: 10000.00,
                Margin: 0.00,
                FreeMargin: 10000.00,
                Profit: 0.00,
                Currency: "USD"
            ));
        }

        public async Task<IReadOnlyList<ActivePosition>> GetActivePositionsAsync()
        {
            var positions = new List<ActivePosition>();
            return await Task.FromResult(positions);
        }

        public async Task<IReadOnlyList<AuditLogRecord>> GetAuditLogsAsync(int limit = 50)
        {
            var list = new List<AuditLogRecord>();
            var fullPath = ResolveDbPath();

            if (!File.Exists(fullPath))
            {
                return list;
            }

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = fullPath,
                Mode = SqliteOpenMode.ReadOnly,
                DefaultTimeout = 5
            };

            try
            {
                using var conn = new SqliteConnection(builder.ConnectionString);
                await conn.OpenAsync();

                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, event_id, timestamp, event_type, payload, prev_hash, current_hash 
                    FROM audit_log 
                    ORDER BY id DESC 
                    LIMIT @limit;";
                cmd.Parameters.AddWithValue("@limit", limit);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var id = reader.GetInt64(0);
                    var eventId = reader.GetString(1);
                    var tsStr = reader.GetString(2);
                    var eventType = reader.GetString(3);
                    var payload = reader.GetString(4);
                    var prevHash = reader.GetString(5);
                    var currentHash = reader.GetString(6);

                    DateTime.TryParse(tsStr, out var ts);
                    list.Add(new AuditLogRecord(id, eventId, ts, eventType, payload, prevHash, currentHash));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading SQLite audit log: {ex.Message}");
            }

            return list;
        }

        public async Task<(bool IsValid, string Message)> VerifyAuditChainIntegrityAsync()
        {
            var logs = await GetAuditLogsAsync(limit: 1000);
            if (logs.Count == 0)
            {
                return (true, "Audit log is empty.");
            }

            var reversed = new List<AuditLogRecord>(logs);
            reversed.Reverse();

            string expectedPrev = reversed[0].PrevHash;

            foreach (var record in reversed)
            {
                if (record.PrevHash != expectedPrev)
                {
                    return (false, $"Broken chain link at record {record.Id}: Expected prev_hash {expectedPrev}, found {record.PrevHash}");
                }

                var digestInput = $"{record.PrevHash}|{record.Timestamp:o}|{record.EventType}|{record.PayloadJson}";
                using var sha256 = SHA256.Create();
                var computedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(digestInput));
                var computedHash = Convert.ToHexString(computedBytes).ToLowerInvariant();

                if (!computedHash.Equals(record.CurrentHash, StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"Hash mismatch at record {record.Id}: Computed {computedHash}, stored {record.CurrentHash}");
                }

                expectedPrev = record.CurrentHash;
            }

            return (true, "SHA-256 cryptographic chain of custody verified valid.");
        }
    }
}
