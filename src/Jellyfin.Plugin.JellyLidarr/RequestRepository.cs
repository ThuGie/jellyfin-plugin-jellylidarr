using Microsoft.Data.Sqlite;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.JellyLidarr;

public sealed class RequestRepository
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RequestRepository(IApplicationPaths paths)
    {
        var directory = Path.Combine(paths.DataPath, "jellylidarr");
        Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "requests.db") }.ToString();
        Initialize();
    }

    private void Initialize()
    {
        using var db = new SqliteConnection(_connectionString);
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS requests (
              id INTEGER PRIMARY KEY AUTOINCREMENT, user_id TEXT NOT NULL, user_name TEXT NOT NULL,
              kind INTEGER NOT NULL, mbid TEXT NOT NULL, name TEXT NOT NULL, artist_name TEXT NULL,
              lidarr_artist_id INTEGER NULL, lidarr_album_id INTEGER NULL, state INTEGER NOT NULL,
              created_at TEXT NOT NULL, updated_at TEXT NOT NULL, approver_id TEXT NULL,
              failure_reason TEXT NULL, retry_count INTEGER NOT NULL DEFAULT 0);
            CREATE UNIQUE INDEX IF NOT EXISTS active_request ON requests(kind, mbid)
              WHERE state IN (0,1,2,3,4);
            CREATE TABLE IF NOT EXISTS audit_events (
              id INTEGER PRIMARY KEY AUTOINCREMENT, request_id INTEGER NOT NULL, actor_id TEXT NOT NULL,
              action TEXT NOT NULL, detail TEXT NULL, created_at TEXT NOT NULL,
              FOREIGN KEY(request_id) REFERENCES requests(id));
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task<MusicRequest> CreateOrGetAsync(CreateRequestDto input, CurrentUser user, RequestState state, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await using var db = new SqliteConnection(_connectionString);
            await db.OpenAsync(token).ConfigureAwait(false);
            await using var tx = await db.BeginTransactionAsync(token).ConfigureAwait(false);
            var existing = await FindActiveAsync(db, input.Kind, input.MusicBrainzId, token).ConfigureAwait(false);
            if (existing is not null) return existing;
            var now = DateTimeOffset.UtcNow;
            await using var cmd = db.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = "INSERT INTO requests(user_id,user_name,kind,mbid,name,artist_name,state,created_at,updated_at) VALUES($u,$un,$k,$m,$n,$a,$s,$c,$c); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$u", user.Id.ToString()); cmd.Parameters.AddWithValue("$un", user.Name);
            cmd.Parameters.AddWithValue("$k", (int)input.Kind); cmd.Parameters.AddWithValue("$m", input.MusicBrainzId);
            cmd.Parameters.AddWithValue("$n", input.Name); cmd.Parameters.AddWithValue("$a", (object?)input.ArtistName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$s", (int)state); cmd.Parameters.AddWithValue("$c", now.ToString("O"));
            var id = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false) ?? 0L);
            await AppendAuditAsync(db, id, user.Id, "created", state.ToString(), token).ConfigureAwait(false);
            await tx.CommitAsync(token).ConfigureAwait(false);
            return new MusicRequest(id, user.Id, user.Name, input.Kind, input.MusicBrainzId, input.Name, input.ArtistName, null, null, state, now, now, null, null, 0);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<MusicRequest>> ListAsync(Guid? userId, CancellationToken token)
    {
        await using var db = new SqliteConnection(_connectionString); await db.OpenAsync(token).ConfigureAwait(false);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT * FROM requests" + (userId.HasValue ? " WHERE user_id=$u" : string.Empty) + " ORDER BY created_at DESC";
        if (userId.HasValue) cmd.Parameters.AddWithValue("$u", userId.Value.ToString());
        var result = new List<MusicRequest>(); await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(Read(reader));
        return result;
    }

    public async Task<MusicRequest?> GetAsync(long id, CancellationToken token)
    {
        await using var db = new SqliteConnection(_connectionString); await db.OpenAsync(token).ConfigureAwait(false);
        await using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT * FROM requests WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task UpdateAsync(long id, RequestState state, Guid actor, string action, string? detail, int? artistId, int? albumId, bool incrementRetry, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await using var db = new SqliteConnection(_connectionString); await db.OpenAsync(token).ConfigureAwait(false);
            await using var tx = await db.BeginTransactionAsync(token).ConfigureAwait(false);
            await using var cmd = db.CreateCommand(); cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = "UPDATE requests SET state=$s,updated_at=$u,approver_id=CASE WHEN $action='approved' THEN $actor ELSE approver_id END,failure_reason=$detail,lidarr_artist_id=COALESCE($aid,lidarr_artist_id),lidarr_album_id=COALESCE($alid,lidarr_album_id),retry_count=retry_count+$retry WHERE id=$id";
            cmd.Parameters.AddWithValue("$s", (int)state); cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$action", action); cmd.Parameters.AddWithValue("$actor", actor.ToString());
            cmd.Parameters.AddWithValue("$detail", (object?)detail ?? DBNull.Value); cmd.Parameters.AddWithValue("$aid", (object?)artistId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$alid", (object?)albumId ?? DBNull.Value); cmd.Parameters.AddWithValue("$retry", incrementRetry ? 1 : 0); cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await AppendAuditAsync(db, id, actor, action, detail, token).ConfigureAwait(false); await tx.CommitAsync(token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private static async Task<MusicRequest?> FindActiveAsync(SqliteConnection db, RequestKind kind, string mbid, CancellationToken token)
    {
        await using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT * FROM requests WHERE kind=$k AND mbid=$m AND state IN (0,1,2,3,4) LIMIT 1";
        cmd.Parameters.AddWithValue("$k", (int)kind); cmd.Parameters.AddWithValue("$m", mbid);
        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false); return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static async Task AppendAuditAsync(SqliteConnection db, long requestId, Guid actor, string action, string? detail, CancellationToken token)
    {
        await using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT INTO audit_events(request_id,actor_id,action,detail,created_at) VALUES($r,$a,$x,$d,$c)";
        cmd.Parameters.AddWithValue("$r", requestId); cmd.Parameters.AddWithValue("$a", actor.ToString()); cmd.Parameters.AddWithValue("$x", action);
        cmd.Parameters.AddWithValue("$d", (object?)detail ?? DBNull.Value); cmd.Parameters.AddWithValue("$c", DateTimeOffset.UtcNow.ToString("O")); await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static MusicRequest Read(SqliteDataReader r) => new(r.GetInt64(r.GetOrdinal("id")), Guid.Parse(r.GetString(r.GetOrdinal("user_id"))), r.GetString(r.GetOrdinal("user_name")),
        (RequestKind)r.GetInt32(r.GetOrdinal("kind")), r.GetString(r.GetOrdinal("mbid")), r.GetString(r.GetOrdinal("name")), Value<string>(r,"artist_name"),
        Value<long>(r,"lidarr_artist_id") is long ai ? (int)ai : null, Value<long>(r,"lidarr_album_id") is long al ? (int)al : null, (RequestState)r.GetInt32(r.GetOrdinal("state")),
        DateTimeOffset.Parse(r.GetString(r.GetOrdinal("created_at"))), DateTimeOffset.Parse(r.GetString(r.GetOrdinal("updated_at"))),
        Value<string>(r,"approver_id") is string ap ? Guid.Parse(ap) : null, Value<string>(r,"failure_reason"), r.GetInt32(r.GetOrdinal("retry_count")));
    private static T? Value<T>(SqliteDataReader r, string name) => r.IsDBNull(r.GetOrdinal(name)) ? default : r.GetFieldValue<T>(r.GetOrdinal(name));
}
